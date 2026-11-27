using System.Data;
using System.Security.Claims;
using Merkdito.Data.Context;
using Merkdito.Data.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Merkdito.Api.Controllers;

[Authorize(Roles = "CLIENTE")]
[ApiController]
[Route("api/[controller]")]
public class MovimentacoesEstoqueController : ControllerBase
{
    private readonly MerkditoDbContext _db;

    public MovimentacoesEstoqueController(MerkditoDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] int? produtoId,
        [FromQuery] string? tipo,
        [FromQuery] DateOnly? de,
        [FromQuery] DateOnly? ate,
        [FromQuery] int? estabelecimentoId,
        [FromQuery] int limite = 200)
    {
        var clienteId = ObterClienteId();
        if (!clienteId.HasValue) return Unauthorized();
        if (produtoId is <= 0) return BadRequest("O ID do produto é inválido.");
        if (de.HasValue && ate.HasValue && de.Value > ate.Value)
            return BadRequest("A data inicial não pode ser posterior à data final.");

        var tipoNormalizado = NormalizarTipo(tipo);
        if (tipo is not null && tipo.Trim().Length > 0 && tipoNormalizado is null)
            return BadRequest("Tipo inválido. Utilize ENTRADA, SAIDA ou AJUSTE.");

        var consulta = _db.MovimentacoesEstoque.AsNoTracking()
            .Where(m => m.Estabelecimento != null && m.Estabelecimento.ClienteId == clienteId.Value);

        if (estabelecimentoId.HasValue)
        {
            var lojaPertenceCliente = await _db.Estabelecimentos.AnyAsync(e =>
                e.Id == estabelecimentoId.Value && e.ClienteId == clienteId.Value);
            if (!lojaPertenceCliente) return NotFound("Loja não encontrada nesta conta.");
            consulta = consulta.Where(m => m.EstabelecimentoId == estabelecimentoId.Value);
        }
        if (produtoId.HasValue) consulta = consulta.Where(m => m.ProdutoId == produtoId.Value);
        if (tipoNormalizado is not null) consulta = consulta.Where(m => m.Tipo == tipoNormalizado);
        if (de.HasValue)
        {
            var inicioUtc = DateTime.SpecifyKind(de.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            consulta = consulta.Where(m => m.CriadoEmUtc >= inicioUtc);
        }
        if (ate.HasValue)
        {
            var diaSeguinteUtc = DateTime.SpecifyKind(ate.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            consulta = consulta.Where(m => m.CriadoEmUtc < diaSeguinteUtc);
        }

        limite = Math.Clamp(limite, 1, 500);
        var registros = await consulta.OrderByDescending(m => m.CriadoEmUtc).ThenByDescending(m => m.Id)
            .Take(limite)
            .Select(m => new
            {
                m.Id, m.ProdutoId, ProdutoNome = m.Produto != null ? m.Produto.Nome : "Produto removido",
                CodigoBarras = m.Produto != null ? m.Produto.CodigoBarras : null,
                m.EstabelecimentoId,
                Estabelecimento = m.Estabelecimento != null ? m.Estabelecimento.NomeFantasia : "Loja removida",
                EhEstoqueCentral = m.Estabelecimento != null && m.Estabelecimento.EhEstoqueCentral,
                m.Tipo, m.Quantidade, m.QuantidadeAnterior, m.QuantidadePosterior,
                m.Observacao, m.CriadoEmUtc
            }).ToListAsync();
        return Ok(registros);
    }

    [HttpPost]
    public async Task<IActionResult> Criar([FromBody] CriarMovimentacaoRequest request)
    {
        var clienteId = ObterClienteId();
        if (!clienteId.HasValue) return Unauthorized();
        if (request.ProdutoId <= 0) return BadRequest("Selecione um produto válido.");
        if (request.Quantidade < 0) return BadRequest("A quantidade não pode ser negativa.");

        var tipo = NormalizarTipo(request.Tipo);
        if (tipo is null) return BadRequest("Tipo inválido. Utilize ENTRADA, SAIDA ou AJUSTE.");
        if (tipo != "AJUSTE" && request.Quantidade == 0)
            return BadRequest("Entradas e saídas precisam ter quantidade maior que zero.");
        if ((request.Observacao?.Trim().Length ?? 0) > 300)
            return BadRequest("A observação deve ter no máximo 300 caracteres.");

        await using var transacao = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        var lojaQuery = _db.Estabelecimentos.Where(e => e.ClienteId == clienteId.Value && e.Ativo);
        var loja = request.EstabelecimentoId.HasValue
            ? await lojaQuery.FirstOrDefaultAsync(e => e.Id == request.EstabelecimentoId.Value)
            : await lojaQuery.FirstOrDefaultAsync(e => e.EhEstoqueCentral);
        if (loja is null) return NotFound("Loja ou estoque central não encontrado nesta conta.");

        var produto = await _db.Produtos.FirstOrDefaultAsync(p => p.Id == request.ProdutoId &&
            (p.ClienteId == clienteId.Value || (p.ClienteId == null && p.Estabelecimento != null && p.Estabelecimento.ClienteId == clienteId.Value)));
        if (produto is null) return NotFound("Produto não encontrado no catálogo da empresa autenticada.");

        var estoque = await _db.ProdutosEstabelecimentos.FirstOrDefaultAsync(pe =>
            pe.ProdutoId == produto.Id && pe.EstabelecimentoId == loja.Id);
        if (estoque is null)
        {
            if (!loja.EhEstoqueCentral)
                return Conflict("Este produto ainda não está vinculado à loja. Vincule-o e transfira unidades a partir do estoque central.");

            estoque = new ProdutoEstabelecimento
            {
                ProdutoId = produto.Id,
                EstabelecimentoId = loja.Id,
                PrecoVenda = produto.Preco,
                QuantidadeEstoque = produto.QuantidadeEstoque,
                EstoqueMinimo = produto.EstoqueMinimo,
                Ativo = produto.Ativo,
                CriadoEmUtc = DateTime.UtcNow
            };
            _db.ProdutosEstabelecimentos.Add(estoque);
        }

        // Entradas em lojas de venda devem ser feitas por transferência do depósito central,
        // para não criar estoque sem retirada correspondente do estoque geral.
        if (!loja.EhEstoqueCentral && tipo == "ENTRADA")
            return BadRequest("Para acrescentar unidades a uma loja de venda, utilize Lojas > Produtos por loja e transfira do estoque central.");

        var anterior = estoque.QuantidadeEstoque;
        int posterior;
        int quantidadeMovimentada;

        switch (tipo)
        {
            case "ENTRADA":
                if (request.Quantidade > int.MaxValue - anterior)
                    return BadRequest("A quantidade resultante ultrapassa o limite suportado.");
                posterior = anterior + request.Quantidade;
                quantidadeMovimentada = request.Quantidade;
                break;
            case "SAIDA":
                if (request.Quantidade > anterior)
                    return Conflict($"Estoque insuficiente em '{loja.NomeFantasia}'. Saldo atual: {anterior}.");
                posterior = anterior - request.Quantidade;
                quantidadeMovimentada = request.Quantidade;
                break;
            default:
                posterior = request.Quantidade;
                quantidadeMovimentada = Math.Abs(posterior - anterior);
                if (posterior == anterior)
                    return BadRequest("O saldo informado é igual ao saldo atual; não há ajuste para registrar.");
                break;
        }

        estoque.QuantidadeEstoque = posterior;
        if (loja.EhEstoqueCentral)
        {
            // Os campos antigos são mantidos como espelho do depósito central por compatibilidade.
            produto.EstabelecimentoId = loja.Id;
            produto.Preco = estoque.PrecoVenda;
            produto.QuantidadeEstoque = posterior;
            produto.EstoqueMinimo = estoque.EstoqueMinimo;
        }

        var movimento = new MovimentacaoEstoque
        {
            ProdutoId = produto.Id,
            EstabelecimentoId = loja.Id,
            Tipo = tipo,
            Quantidade = quantidadeMovimentada,
            QuantidadeAnterior = anterior,
            QuantidadePosterior = posterior,
            Observacao = string.IsNullOrWhiteSpace(request.Observacao) ? null : request.Observacao.Trim(),
            CriadoEmUtc = DateTime.UtcNow
        };

        _db.MovimentacoesEstoque.Add(movimento);
        await _db.SaveChangesAsync();
        await transacao.CommitAsync();

        return CreatedAtAction(nameof(Listar), new { produtoId = produto.Id, estabelecimentoId = loja.Id }, new
        {
            movimento.Id, movimento.ProdutoId, ProdutoNome = produto.Nome, movimento.EstabelecimentoId,
            Estabelecimento = loja.NomeFantasia, movimento.Tipo, movimento.Quantidade,
            movimento.QuantidadeAnterior, movimento.QuantidadePosterior, movimento.Observacao, movimento.CriadoEmUtc
        });
    }

    private int? ObterClienteId()
    {
        var valor = User.FindFirstValue("ClienteId");
        return int.TryParse(valor, out var id) ? id : null;
    }

    private static string? NormalizarTipo(string? tipo)
    {
        var valor = tipo?.Trim().ToUpperInvariant();
        return valor is "ENTRADA" or "SAIDA" or "AJUSTE" ? valor : null;
    }
}

public sealed class CriarMovimentacaoRequest
{
    public int ProdutoId { get; set; }
    public int? EstabelecimentoId { get; set; }
    public string Tipo { get; set; } = string.Empty;
    /// <summary>ENTRADA/SAIDA = unidades; AJUSTE = saldo final desejado.</summary>
    public int Quantidade { get; set; }
    public string? Observacao { get; set; }
}
