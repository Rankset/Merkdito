using System.Data;
using System.Security.Claims;
using Merkdito.Data.Context;
using Merkdito.Data.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Merkdito.Api.Controllers;

/// <summary>
/// Compras são criadas como PENDENTE. O estoque só aumenta ao chamar /{id}/receber,
/// que é transacional e só pode ser executado uma vez.
/// </summary>
[Authorize(Roles = "CLIENTE")]
[ApiController]
[Route("api/[controller]")]
public sealed class ComprasController(MerkditoDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] string? status = null,
        [FromQuery] DateOnly? de = null,
        [FromQuery] DateOnly? ate = null,
        [FromQuery] int limite = 200)
    {
        var clienteId = ClienteId();
        if (clienteId is null) return Unauthorized();
        if (de.HasValue && ate.HasValue && de.Value > ate.Value)
            return BadRequest(new { mensagem = "A data inicial não pode ser posterior à data final." });
        var estado = NormalizarStatus(status);
        if (!string.IsNullOrWhiteSpace(status) && estado is null)
            return BadRequest(new { mensagem = "Status inválido. Use PENDENTE, RECEBIDA ou CANCELADA." });

        var q = db.Compras.AsNoTracking().Where(c => c.ClienteId == clienteId.Value);
        if (estado is not null) q = q.Where(c => c.Status == estado);
        if (de.HasValue)
        {
            var inicio = DateTime.SpecifyKind(de.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            q = q.Where(c => c.CriadoEmUtc >= inicio);
        }
        if (ate.HasValue)
        {
            var fim = DateTime.SpecifyKind(ate.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            q = q.Where(c => c.CriadoEmUtc < fim);
        }
        limite = Math.Clamp(limite, 1, 500);
        var itens = await q.OrderByDescending(c => c.CriadoEmUtc).Take(limite)
            .Select(c => new
            {
                c.Id, c.FornecedorId,
                Fornecedor = c.Fornecedor != null ? c.Fornecedor.Nome : "Fornecedor indisponível",
                c.EstabelecimentoId,
                Estabelecimento = c.Estabelecimento != null ? c.Estabelecimento.NomeFantasia : "Estoque indisponível",
                c.NumeroDocumento, c.DataCompraUtc, c.Status, c.Observacao, c.Total, c.CriadoEmUtc, c.RecebidaEmUtc,
                QuantidadeItens = c.Itens.Count
            }).ToListAsync();
        return Ok(itens);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Buscar(int id)
    {
        var clienteId = ClienteId();
        if (clienteId is null) return Unauthorized();
        var compra = await db.Compras.AsNoTracking()
            .Where(c => c.Id == id && c.ClienteId == clienteId.Value)
            .Select(c => new
            {
                c.Id, c.ClienteId, c.FornecedorId,
                Fornecedor = c.Fornecedor != null ? c.Fornecedor.Nome : null,
                c.EstabelecimentoId,
                Estabelecimento = c.Estabelecimento != null ? c.Estabelecimento.NomeFantasia : null,
                c.NumeroDocumento, c.DataCompraUtc, c.Status, c.Observacao, c.Total, c.CriadoEmUtc, c.RecebidaEmUtc,
                Itens = c.Itens.OrderBy(i => i.Id).Select(i => new
                {
                    i.Id, i.ProdutoId, i.NomeProduto, i.CodigoBarrasProduto,
                    i.Quantidade, i.CustoUnitario, i.TotalItem
                }).ToList()
            }).FirstOrDefaultAsync();
        return compra is null ? NotFound(new { mensagem = "Compra não encontrada nesta conta." }) : Ok(compra);
    }

    [HttpPost]
    public async Task<IActionResult> Criar([FromBody] CriarCompraRequest request)
    {
        var clienteId = ClienteId();
        if (clienteId is null) return Unauthorized();
        if (request.FornecedorId <= 0) return BadRequest(new { mensagem = "Selecione um fornecedor válido." });
        if (request.Itens is null || request.Itens.Count == 0 || request.Itens.Count > 200)
            return BadRequest(new { mensagem = "A compra deve conter entre 1 e 200 itens." });
        if ((request.NumeroDocumento?.Trim().Length ?? 0) > 80)
            return BadRequest(new { mensagem = "O número do documento deve ter até 80 caracteres." });
        if ((request.Observacao?.Trim().Length ?? 0) > 500)
            return BadRequest(new { mensagem = "A observação deve ter até 500 caracteres." });
        if (request.Itens.Any(i => i.ProdutoId <= 0 || i.Quantidade <= 0 || i.Quantidade > 1_000_000 ||
                                  i.CustoUnitario < 0 || i.CustoUnitario > 999_999_999m))
            return BadRequest(new { mensagem = "Há item com produto, quantidade ou custo inválido." });
        if (request.Itens.GroupBy(i => i.ProdutoId).Any(g => g.Count() > 1))
            return BadRequest(new { mensagem = "Não repita o mesmo produto. Some as quantidades em uma única linha." });

        var fornecedor = await db.Fornecedores.FirstOrDefaultAsync(f =>
            f.Id == request.FornecedorId && f.ClienteId == clienteId.Value && f.Ativo);
        if (fornecedor is null) return NotFound(new { mensagem = "Fornecedor ativo não encontrado nesta conta." });

        var central = await db.Estabelecimentos.FirstOrDefaultAsync(e =>
            e.ClienteId == clienteId.Value && e.EhEstoqueCentral && e.Ativo);
        if (central is null) return Conflict(new { mensagem = "A conta não possui um estoque central ativo." });

        var ids = request.Itens.Select(i => i.ProdutoId).ToArray();
        var produtos = await db.Produtos.Where(p => ids.Contains(p.Id) &&
            (p.ClienteId == clienteId.Value ||
             (p.ClienteId == null && p.Estabelecimento != null && p.Estabelecimento.ClienteId == clienteId.Value)))
            .ToDictionaryAsync(p => p.Id);
        if (produtos.Count != ids.Length)
            return BadRequest(new { mensagem = "Um ou mais produtos não pertencem ao catálogo desta empresa." });

        decimal total = 0;
        var linhas = new List<ItemCompra>();
        foreach (var item in request.Itens)
        {
            var linhaTotal = decimal.Round(item.Quantidade * item.CustoUnitario, 2, MidpointRounding.AwayFromZero);
            total += linhaTotal;
            if (linhaTotal > 9_999_999_999.99m || total > 9_999_999_999.99m)
                return BadRequest(new { mensagem = "O total da compra ultrapassa o limite suportado." });
            var produto = produtos[item.ProdutoId];
            linhas.Add(new ItemCompra
            {
                ProdutoId = produto.Id,
                NomeProduto = produto.Nome,
                CodigoBarrasProduto = produto.CodigoBarras,
                Quantidade = item.Quantidade,
                CustoUnitario = item.CustoUnitario,
                TotalItem = linhaTotal
            });
        }

        var compra = new Compra
        {
            ClienteId = clienteId.Value,
            FornecedorId = fornecedor.Id,
            EstabelecimentoId = central.Id,
            NumeroDocumento = Texto(request.NumeroDocumento),
            DataCompraUtc = request.DataCompraUtc.HasValue
                ? DateTime.SpecifyKind(request.DataCompraUtc.Value, DateTimeKind.Utc)
                : DateTime.UtcNow,
            Status = "PENDENTE",
            Observacao = Texto(request.Observacao),
            Total = total,
            CriadoEmUtc = DateTime.UtcNow,
            Itens = linhas
        };
        db.Compras.Add(compra);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Buscar), new { id = compra.Id }, new
        {
            compra.Id, compra.Status, compra.Total, mensagem = "Compra registrada como pendente. O estoque ainda não foi alterado."
        });
    }

    [HttpPost("{id:int}/receber")]
    public async Task<IActionResult> Receber(int id)
    {
        var clienteId = ClienteId();
        if (clienteId is null) return Unauthorized();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        var compra = await db.Compras.Include(c => c.Itens)
            .FirstOrDefaultAsync(c => c.Id == id && c.ClienteId == clienteId.Value);
        if (compra is null) return NotFound(new { mensagem = "Compra não encontrada nesta conta." });
        if (compra.Status != "PENDENTE")
            return Conflict(new { mensagem = $"Somente compras pendentes podem ser recebidas. Status atual: {compra.Status}." });

        var central = await db.Estabelecimentos.FirstOrDefaultAsync(e =>
            e.Id == compra.EstabelecimentoId && e.ClienteId == clienteId.Value && e.EhEstoqueCentral && e.Ativo);
        if (central is null) return Conflict(new { mensagem = "O estoque central vinculado à compra não está disponível." });

        var idsCompra = compra.Itens.Select(i => i.ProdutoId).Distinct().ToArray();
        var produtos = await db.Produtos.Where(p => idsCompra.Contains(p.Id) &&
            (p.ClienteId == clienteId.Value ||
             (p.ClienteId == null && p.Estabelecimento != null && p.Estabelecimento.ClienteId == clienteId.Value)))
            .ToDictionaryAsync(p => p.Id);
        if (produtos.Count != compra.Itens.Select(i => i.ProdutoId).Distinct().Count())
            return Conflict(new { mensagem = "Um produto da compra não está mais disponível no catálogo desta empresa. Nenhum estoque foi alterado." });

        var idsProdutos = compra.Itens.Select(i => i.ProdutoId).Distinct().ToArray();
        var saldos = await db.ProdutosEstabelecimentos
            .Where(pe => pe.EstabelecimentoId == central.Id && idsProdutos.Contains(pe.ProdutoId))
            .ToDictionaryAsync(pe => pe.ProdutoId);

        foreach (var item in compra.Itens)
        {
            var produto = produtos[item.ProdutoId];
            if (!saldos.TryGetValue(item.ProdutoId, out var estoque))
            {
                estoque = new ProdutoEstabelecimento
                {
                    ProdutoId = produto.Id, EstabelecimentoId = central.Id,
                    PrecoVenda = produto.Preco, QuantidadeEstoque = 0,
                    EstoqueMinimo = produto.EstoqueMinimo, Ativo = produto.Ativo,
                    CriadoEmUtc = DateTime.UtcNow
                };
                db.ProdutosEstabelecimentos.Add(estoque);
                saldos[item.ProdutoId] = estoque;
            }

            var anterior = estoque.QuantidadeEstoque;
            if (item.Quantidade > int.MaxValue - anterior)
                return BadRequest(new { mensagem = $"A quantidade recebida de '{produto.Nome}' ultrapassa o limite suportado. Nenhum estoque foi alterado." });

            estoque.QuantidadeEstoque = anterior + item.Quantidade;
            produto.EstabelecimentoId = central.Id;
            produto.QuantidadeEstoque = estoque.QuantidadeEstoque;
            produto.EstoqueMinimo = estoque.EstoqueMinimo;
            produto.Preco = estoque.PrecoVenda;

            db.MovimentacoesEstoque.Add(new MovimentacaoEstoque
            {
                ProdutoId = produto.Id, EstabelecimentoId = central.Id, Tipo = "ENTRADA",
                Quantidade = item.Quantidade, QuantidadeAnterior = anterior,
                QuantidadePosterior = estoque.QuantidadeEstoque,
                Observacao = $"Recebimento da compra #{compra.Id}" +
                    (string.IsNullOrWhiteSpace(compra.NumeroDocumento) ? "" : $" (documento {compra.NumeroDocumento})"),
                CriadoEmUtc = DateTime.UtcNow
            });
        }

        compra.Status = "RECEBIDA";
        compra.RecebidaEmUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Ok(new { compra.Id, compra.Status, compra.Total, compra.RecebidaEmUtc, mensagem = "Compra recebida. O estoque central e o histórico de movimentações foram atualizados." });
    }

    [HttpPost("{id:int}/cancelar")]
    public async Task<IActionResult> Cancelar(int id)
    {
        var clienteId = ClienteId();
        if (clienteId is null) return Unauthorized();
        var compra = await db.Compras.FirstOrDefaultAsync(c => c.Id == id && c.ClienteId == clienteId.Value);
        if (compra is null) return NotFound(new { mensagem = "Compra não encontrada nesta conta." });
        if (compra.Status != "PENDENTE")
            return Conflict(new { mensagem = "Somente compras pendentes podem ser canceladas." });
        compra.Status = "CANCELADA";
        await db.SaveChangesAsync();
        return Ok(new { compra.Id, compra.Status });
    }

    private int? ClienteId() => int.TryParse(User.FindFirstValue("ClienteId"), out var id) ? id : null;
    private static string? Texto(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? NormalizarStatus(string? value)
    {
        var status = value?.Trim().ToUpperInvariant();
        return status is "PENDENTE" or "RECEBIDA" or "CANCELADA" ? status : null;
    }
}

public sealed class CriarCompraRequest
{
    public int FornecedorId { get; set; }
    public string? NumeroDocumento { get; set; }
    public DateTime? DataCompraUtc { get; set; }
    public string? Observacao { get; set; }
    public List<ItemCompraRequest> Itens { get; set; } = new();
}

public sealed class ItemCompraRequest
{
    public int ProdutoId { get; set; }
    public int Quantidade { get; set; }
    public decimal CustoUnitario { get; set; }
}
