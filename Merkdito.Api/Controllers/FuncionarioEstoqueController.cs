using System.Data;
using System.Security.Claims;
using Merkdito.Data.Context;
using Merkdito.Data.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Merkdito.Api.Controllers;

/// <summary>
/// Endpoints de estoque para funcionários. Cada operação valida no banco a permissão
/// do funcionário para o estabelecimento e restringe os dados ao cliente do token.
/// </summary>
[ApiController]
[Route("api/funcionario/estoque")]
[Authorize(Roles = "FUNCIONARIO")]
public sealed class FuncionarioEstoqueController(MerkditoDbContext db) : ControllerBase
{
    [HttpGet("estabelecimentos")]
    public async Task<IActionResult> Estabelecimentos(CancellationToken ct)
    {
        if (!TryGetIdentity(out var clienteId, out var funcionarioId)) return Unauthorized();
        var lojas = await db.UsuariosFuncionariosEstabelecimentos.AsNoTracking()
            .Where(a => a.UsuarioFuncionarioId == funcionarioId
                && a.PodeGerenciarEstoque
                && a.UsuarioFuncionario != null && a.UsuarioFuncionario.Ativo
                && a.UsuarioFuncionario.ClienteId == clienteId
                && a.Estabelecimento != null && a.Estabelecimento.Ativo
                && a.Estabelecimento.ClienteId == clienteId)
            .OrderBy(a => a.Estabelecimento!.EhEstoqueCentral ? 0 : 1)
            .ThenBy(a => a.Estabelecimento!.NomeFantasia)
            .Select(a => new
            {
                id = a.EstabelecimentoId,
                nome = a.Estabelecimento!.NomeFantasia,
                ehEstoqueCentral = a.Estabelecimento.EhEstoqueCentral
            })
            .ToListAsync(ct);
        return Ok(lojas);
    }

    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] int estabelecimentoId, CancellationToken ct)
    {
        if (!TryGetIdentity(out var clienteId, out var funcionarioId)) return Unauthorized();
        if (estabelecimentoId <= 0)
            return BadRequest(new { mensagem = "Informe um estabelecimento válido." });

        if (!await TemPermissao(funcionarioId, clienteId, estabelecimentoId, ct)) return Forbid();

        var itens = await db.ProdutosEstabelecimentos.AsNoTracking()
            .Where(pe => pe.EstabelecimentoId == estabelecimentoId
                && pe.Estabelecimento != null && pe.Estabelecimento.ClienteId == clienteId
                && pe.Estabelecimento.Ativo && pe.Ativo
                && pe.Produto != null && pe.Produto.Ativo
                && (pe.Produto.ClienteId == clienteId
                    || (pe.Produto.ClienteId == null && pe.Produto.Estabelecimento != null
                        && pe.Produto.Estabelecimento.ClienteId == clienteId)))
            .OrderBy(pe => pe.Produto!.Nome)
            .Select(pe => new
            {
                produtoId = pe.ProdutoId,
                produto = pe.Produto!.Nome,
                codigoBarras = pe.Produto.CodigoBarras,
                quantidadeEstoque = pe.QuantidadeEstoque,
                estoqueMinimo = pe.EstoqueMinimo,
                precoVenda = pe.PrecoVenda,
                abaixoDoMinimo = pe.QuantidadeEstoque <= pe.EstoqueMinimo,
                estabelecimentoId = pe.EstabelecimentoId
            })
            .ToListAsync(ct);

        return Ok(itens);
    }

    [HttpGet("movimentacoes")]
    public async Task<IActionResult> ListarMovimentacoes(
        [FromQuery] int estabelecimentoId,
        [FromQuery] int? produtoId,
        [FromQuery] int limite = 100,
        CancellationToken ct = default)
    {
        if (!TryGetIdentity(out var clienteId, out var funcionarioId)) return Unauthorized();
        if (estabelecimentoId <= 0 || produtoId is <= 0)
            return BadRequest(new { mensagem = "Estabelecimento ou produto inválido." });
        if (!await TemPermissao(funcionarioId, clienteId, estabelecimentoId, ct)) return Forbid();

        limite = Math.Clamp(limite, 1, 300);
        var consulta = db.MovimentacoesEstoque.AsNoTracking()
            .Where(m => m.EstabelecimentoId == estabelecimentoId
                && m.Estabelecimento != null && m.Estabelecimento.ClienteId == clienteId
                && m.Produto != null
                && (m.Produto.ClienteId == clienteId
                    || (m.Produto.ClienteId == null && m.Produto.Estabelecimento != null
                        && m.Produto.Estabelecimento.ClienteId == clienteId)));
        if (produtoId.HasValue) consulta = consulta.Where(m => m.ProdutoId == produtoId.Value);

        var itens = await consulta.OrderByDescending(m => m.CriadoEmUtc).ThenByDescending(m => m.Id)
            .Take(limite)
            .Select(m => new
            {
                m.Id,
                m.ProdutoId,
                produto = m.Produto!.Nome,
                m.Tipo,
                m.Quantidade,
                m.QuantidadeAnterior,
                m.QuantidadePosterior,
                m.Observacao,
                m.CriadoEmUtc,
                m.EstabelecimentoId
            }).ToListAsync(ct);

        return Ok(itens);
    }

    /// <summary>
    /// Ajusta o saldo para uma quantidade absoluta, exigindo PodeGerenciarEstoque.
    /// Para transferências entre depósito e loja, continue usando o fluxo de transferência do cliente.
    /// </summary>
    [HttpPost("ajustes")]
    public async Task<IActionResult> Ajustar([FromBody] AjustarEstoqueFuncionarioRequest request, CancellationToken ct)
    {
        if (!TryGetIdentity(out var clienteId, out var funcionarioId)) return Unauthorized();
        if (request.EstabelecimentoId <= 0 || request.ProdutoId <= 0)
            return BadRequest(new { mensagem = "Produto e estabelecimento são obrigatórios." });
        if (request.NovaQuantidade < 0 || request.NovaQuantidade > 1_000_000)
            return BadRequest(new { mensagem = "A nova quantidade deve estar entre 0 e 1.000.000." });
        if ((request.Observacao?.Trim().Length ?? 0) > 300)
            return BadRequest(new { mensagem = "A observação deve ter no máximo 300 caracteres." });

        if (!await TemPermissao(funcionarioId, clienteId, request.EstabelecimentoId, ct)) return Forbid();

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var loja = await db.Estabelecimentos.FirstOrDefaultAsync(e =>
            e.Id == request.EstabelecimentoId && e.ClienteId == clienteId && e.Ativo, ct);
        if (loja is null) return NotFound(new { mensagem = "Estabelecimento não encontrado nesta empresa." });

        var produto = await db.Produtos.FirstOrDefaultAsync(p => p.Id == request.ProdutoId && p.Ativo &&
            (p.ClienteId == clienteId || (p.ClienteId == null && p.Estabelecimento != null && p.Estabelecimento.ClienteId == clienteId)), ct);
        if (produto is null) return NotFound(new { mensagem = "Produto não encontrado nesta empresa." });

        var estoque = await db.ProdutosEstabelecimentos.FirstOrDefaultAsync(pe =>
            pe.ProdutoId == produto.Id && pe.EstabelecimentoId == loja.Id, ct);
        if (estoque is null)
        {
            if (!loja.EhEstoqueCentral)
                return Conflict(new { mensagem = "O produto ainda não está vinculado a esta loja. Faça o vínculo/transferência pelo fluxo de estoque central." });

            estoque = new ProdutoEstabelecimento
            {
                ProdutoId = produto.Id,
                EstabelecimentoId = loja.Id,
                PrecoVenda = produto.Preco,
                QuantidadeEstoque = Math.Max(0, produto.QuantidadeEstoque),
                EstoqueMinimo = Math.Max(0, produto.EstoqueMinimo),
                Ativo = produto.Ativo,
                CriadoEmUtc = DateTime.UtcNow
            };
            db.ProdutosEstabelecimentos.Add(estoque);
        }
        if (!estoque.Ativo)
            return Conflict(new { mensagem = "O produto está inativo neste estabelecimento." });

        var anterior = estoque.QuantidadeEstoque;
        var posterior = request.NovaQuantidade;
        if (anterior == posterior)
            return BadRequest(new { mensagem = "A nova quantidade é igual ao saldo atual; nenhum ajuste é necessário." });

        estoque.QuantidadeEstoque = posterior;
        if (loja.EhEstoqueCentral)
        {
            // Mantém os campos legados sincronizados com o saldo do depósito central.
            produto.EstabelecimentoId = loja.Id;
            produto.Preco = estoque.PrecoVenda;
            produto.QuantidadeEstoque = posterior;
            produto.EstoqueMinimo = estoque.EstoqueMinimo;
        }

        var observacaoCompleta = string.IsNullOrWhiteSpace(request.Observacao)
            ? $"Ajuste registrado por funcionário #{funcionarioId}."
            : $"{request.Observacao.Trim()} (ajuste por funcionário #{funcionarioId}).";
        if (observacaoCompleta.Length > 300) observacaoCompleta = observacaoCompleta[..300];

        db.MovimentacoesEstoque.Add(new MovimentacaoEstoque
        {
            ProdutoId = produto.Id,
            EstabelecimentoId = loja.Id,
            Tipo = "AJUSTE",
            Quantidade = Math.Abs(posterior - anterior),
            QuantidadeAnterior = anterior,
            QuantidadePosterior = posterior,
            Observacao = observacaoCompleta,
            CriadoEmUtc = DateTime.UtcNow
        });

        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(ct);
            return Conflict(new { mensagem = "Não foi possível registrar o ajuste. Atualize o saldo e tente novamente." });
        }

        return Ok(new
        {
            mensagem = "Ajuste de estoque registrado.",
            produtoId = produto.Id,
            estabelecimentoId = loja.Id,
            quantidadeAnterior = anterior,
            quantidadePosterior = posterior
        });
    }

    private Task<bool> TemPermissao(int funcionarioId, int clienteId, int estabelecimentoId, CancellationToken ct) =>
        db.UsuariosFuncionariosEstabelecimentos.AsNoTracking().AnyAsync(a =>
            a.UsuarioFuncionarioId == funcionarioId
            && a.EstabelecimentoId == estabelecimentoId
            && a.PodeGerenciarEstoque
            && a.UsuarioFuncionario != null && a.UsuarioFuncionario.Ativo
            && a.UsuarioFuncionario.ClienteId == clienteId
            && a.Estabelecimento != null && a.Estabelecimento.Ativo
            && a.Estabelecimento.ClienteId == clienteId, ct);

    private bool TryGetIdentity(out int clienteId, out int funcionarioId)
    {
        clienteId = 0;
        funcionarioId = 0;
        return int.TryParse(User.FindFirstValue("ClienteId"), out clienteId) && clienteId > 0
            && int.TryParse(User.FindFirstValue("FuncionarioId"), out funcionarioId) && funcionarioId > 0;
    }
}

public sealed class AjustarEstoqueFuncionarioRequest
{
    public int ProdutoId { get; set; }
    public int EstabelecimentoId { get; set; }
    public int NovaQuantidade { get; set; }
    public string? Observacao { get; set; }
}
