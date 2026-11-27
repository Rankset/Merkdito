using System.Data;
using System.Security.Claims;
using Merkdito.Data.Context;
using Merkdito.Data.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Merkdito.Api.Controllers;

/// <summary>Registro de vendas por funcionário com permissão específica por estabelecimento.</summary>
[ApiController]
[Route("api/funcionario/vendas")]
[Authorize(Roles = "FUNCIONARIO")]
public sealed class FuncionarioVendasController(MerkditoDbContext db) : ControllerBase
{
    private const decimal MaxValor = 9_999_999_999.99m;

    public sealed record ItemVendaRequest(int ProdutoId, int Quantidade);
    public sealed record RegistrarVendaRequest(
        int EstabelecimentoId,
        List<ItemVendaRequest> Itens,
        string? IdVendaExterna = null,
        DateTime? DataVendaUtc = null);

    [HttpGet("estabelecimentos")]
    public async Task<IActionResult> Estabelecimentos(CancellationToken ct)
    {
        if (!TryGetIdentity(out var clienteId, out var funcionarioId)) return Unauthorized();
        var lojas = await db.UsuariosFuncionariosEstabelecimentos.AsNoTracking()
            .Where(a => a.UsuarioFuncionarioId == funcionarioId && a.PodeRegistrarVendas
                && a.UsuarioFuncionario != null && a.UsuarioFuncionario.Ativo && a.UsuarioFuncionario.ClienteId == clienteId
                && a.Estabelecimento != null && a.Estabelecimento.Ativo && a.Estabelecimento.ClienteId == clienteId
                && !a.Estabelecimento.EhEstoqueCentral)
            .OrderBy(a => a.Estabelecimento!.NomeFantasia)
            .Select(a => new { id = a.EstabelecimentoId, nome = a.Estabelecimento!.NomeFantasia })
            .ToListAsync(ct);
        return Ok(lojas);
    }

    [HttpGet("produtos")]
    public async Task<IActionResult> Produtos([FromQuery] int estabelecimentoId, CancellationToken ct)
    {
        if (!TryGetIdentity(out var clienteId, out var funcionarioId)) return Unauthorized();
        if (estabelecimentoId <= 0) return BadRequest(new { mensagem = "Informe um estabelecimento válido." });
        var autorizado = await db.UsuariosFuncionariosEstabelecimentos.AsNoTracking().AnyAsync(a =>
            a.UsuarioFuncionarioId == funcionarioId && a.EstabelecimentoId == estabelecimentoId && a.PodeRegistrarVendas
            && a.UsuarioFuncionario != null && a.UsuarioFuncionario.Ativo && a.UsuarioFuncionario.ClienteId == clienteId
            && a.Estabelecimento != null && a.Estabelecimento.Ativo && a.Estabelecimento.ClienteId == clienteId
            && !a.Estabelecimento.EhEstoqueCentral, ct);
        if (!autorizado) return Forbid();
        var produtos = await db.ProdutosEstabelecimentos.AsNoTracking()
            .Where(pe => pe.EstabelecimentoId == estabelecimentoId && pe.Ativo && pe.QuantidadeEstoque >= 0
                && pe.Estabelecimento != null && pe.Estabelecimento.ClienteId == clienteId
                && pe.Produto != null && pe.Produto.Ativo
                && (pe.Produto.ClienteId == clienteId || (pe.Produto.ClienteId == null && pe.Produto.Estabelecimento != null && pe.Produto.Estabelecimento.ClienteId == clienteId)))
            .OrderBy(pe => pe.Produto!.Nome)
            .Select(pe => new { id = pe.ProdutoId, nome = pe.Produto!.Nome, codigoBarras = pe.Produto.CodigoBarras,
                preco = pe.PrecoVenda, quantidadeEstoque = pe.QuantidadeEstoque })
            .ToListAsync(ct);
        return Ok(produtos);
    }

    [HttpPost]
    public async Task<IActionResult> Registrar([FromBody] RegistrarVendaRequest request, CancellationToken ct)
    {
        if (!TryGetIdentity(out var clienteId, out var funcionarioId)) return Unauthorized();
        if (request.EstabelecimentoId <= 0)
            return BadRequest(new { mensagem = "Informe um estabelecimento válido." });
        if (request.Itens is null || request.Itens.Count is < 1 or > 200)
            return BadRequest(new { mensagem = "A venda deve conter de 1 a 200 itens." });
        if (request.Itens.Any(i => i is null || i.ProdutoId <= 0 || i.Quantidade <= 0 || i.Quantidade > 1_000_000))
            return BadRequest(new { mensagem = "Cada item deve ter um produto válido e quantidade entre 1 e 1.000.000." });
        if ((request.IdVendaExterna?.Trim().Length ?? 0) > 100)
            return BadRequest(new { mensagem = "IdVendaExterna deve ter até 100 caracteres." });

        var idExterno = string.IsNullOrWhiteSpace(request.IdVendaExterna)
            ? $"FUNC-{funcionarioId}-{Guid.NewGuid():N}"
            : request.IdVendaExterna.Trim();
        var dataVendaUtc = request.DataVendaUtc.HasValue
            ? (request.DataVendaUtc.Value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(request.DataVendaUtc.Value, DateTimeKind.Utc)
                : request.DataVendaUtc.Value.ToUniversalTime())
            : DateTime.UtcNow;

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var loja = await db.Estabelecimentos.FirstOrDefaultAsync(e =>
            e.Id == request.EstabelecimentoId && e.ClienteId == clienteId && e.Ativo && !e.EhEstoqueCentral, ct);
        if (loja is null)
            return NotFound(new { mensagem = "Loja de venda não encontrada, inativa ou pertencente a outra empresa." });

        var autorizado = await db.UsuariosFuncionariosEstabelecimentos.AsNoTracking().AnyAsync(a =>
            a.UsuarioFuncionarioId == funcionarioId && a.EstabelecimentoId == loja.Id && a.PodeRegistrarVendas &&
            a.UsuarioFuncionario != null && a.UsuarioFuncionario.Ativo && a.UsuarioFuncionario.ClienteId == clienteId &&
            a.Estabelecimento != null && a.Estabelecimento.Ativo && a.Estabelecimento.ClienteId == clienteId &&
            !a.Estabelecimento.EhEstoqueCentral, ct);
        if (!autorizado) return Forbid();

        var existente = await db.Vendas.AsNoTracking().FirstOrDefaultAsync(v =>
            v.EstabelecimentoId == loja.Id && v.IdVendaExterna == idExterno, ct);
        if (existente is not null)
            return Conflict(new { mensagem = "IdVendaExterna já foi utilizado nesta loja. Informe uma chave diferente para uma nova venda.", vendaId = existente.Id });

        var produtoIds = request.Itens.Select(i => i.ProdutoId).Distinct().ToArray();
        var estoques = await db.ProdutosEstabelecimentos
            .Include(pe => pe.Produto)
            .Where(pe => pe.EstabelecimentoId == loja.Id && produtoIds.Contains(pe.ProdutoId)
                && pe.Ativo && pe.Produto != null && pe.Produto.Ativo
                && (pe.Produto.ClienteId == clienteId
                    || (pe.Produto.ClienteId == null && pe.Produto.Estabelecimento != null
                        && pe.Produto.Estabelecimento.ClienteId == clienteId)))
            .ToDictionaryAsync(pe => pe.ProdutoId, ct);

        if (estoques.Count != produtoIds.Length)
            return BadRequest(new { mensagem = "Um ou mais produtos não estão ativos e vinculados a esta loja." });

        var quantidades = request.Itens.GroupBy(i => i.ProdutoId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantidade));
        foreach (var par in quantidades)
        {
            if (!estoques.TryGetValue(par.Key, out var estoque))
                return BadRequest(new { mensagem = $"Produto {par.Key} não encontrado nesta loja." });
            if (par.Value > estoque.QuantidadeEstoque)
                return Conflict(new { mensagem = $"Estoque insuficiente para '{estoque.Produto!.Nome}'. Saldo: {estoque.QuantidadeEstoque}; solicitado: {par.Value}." });
        }

        var venda = new Venda
        {
            EstabelecimentoId = loja.Id,
            IdVendaExterna = idExterno,
            DataVendaUtc = dataVendaUtc,
            Status = "CONFIRMADA",
            Origem = $"FUNCIONARIO:{funcionarioId}",
            CriadoEmUtc = DateTime.UtcNow
        };

        foreach (var item in request.Itens)
        {
            var estoque = estoques[item.ProdutoId];
            var preco = Math.Round(estoque.PrecoVenda, 2, MidpointRounding.AwayFromZero);
            var subtotal = preco * item.Quantidade;
            if (subtotal > MaxValor)
                return BadRequest(new { mensagem = $"O total do item '{estoque.Produto!.Nome}' excede o limite suportado." });
            venda.Itens.Add(new ItemVenda
            {
                ProdutoId = estoque.ProdutoId,
                NomeProduto = estoque.Produto!.Nome,
                CodigoBarrasProduto = estoque.Produto.CodigoBarras,
                Quantidade = item.Quantidade,
                PrecoUnitario = preco,
                TotalItem = subtotal
            });
        }

        venda.Total = venda.Itens.Sum(i => i.TotalItem);
        if (venda.Total > MaxValor)
            return BadRequest(new { mensagem = "O total da venda excede o limite suportado." });

        foreach (var par in quantidades)
        {
            var estoque = estoques[par.Key];
            var anterior = estoque.QuantidadeEstoque;
            var posterior = anterior - par.Value;
            estoque.QuantidadeEstoque = posterior;
            var observacao = $"Venda por funcionário #{funcionarioId}: {idExterno}";
            db.MovimentacoesEstoque.Add(new MovimentacaoEstoque
            {
                ProdutoId = estoque.ProdutoId,
                EstabelecimentoId = loja.Id,
                Tipo = "SAIDA",
                Quantidade = par.Value,
                QuantidadeAnterior = anterior,
                QuantidadePosterior = posterior,
                Observacao = observacao.Length <= 300 ? observacao : observacao[..300],
                CriadoEmUtc = DateTime.UtcNow
            });
        }

        db.Vendas.Add(venda);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(ct);
            return Conflict(new { mensagem = "Não foi possível registrar a venda. Verifique se a chave já foi utilizada e tente novamente." });
        }

        return Created($"/api/funcionario/relatorios/vendas/{venda.Id}?estabelecimentoId={loja.Id}", new
        {
            vendaId = venda.Id,
            venda.IdVendaExterna,
            venda.EstabelecimentoId,
            venda.DataVendaUtc,
            venda.Total,
            venda.Status,
            quantidadeItens = venda.Itens.Count,
            totalUnidades = venda.Itens.Sum(i => i.Quantidade),
            estoqueBaixado = true
        });
    }

    private bool TryGetIdentity(out int clienteId, out int funcionarioId)
    {
        clienteId = 0;
        funcionarioId = 0;
        return int.TryParse(User.FindFirstValue("ClienteId"), out clienteId) && clienteId > 0
            && int.TryParse(User.FindFirstValue("FuncionarioId"), out funcionarioId) && funcionarioId > 0;
    }
}
