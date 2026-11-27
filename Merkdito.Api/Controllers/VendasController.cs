using System.Data;
using System.Security.Claims;
using Merkdito.Data.Context;
using Merkdito.Data.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Merkdito.Api.Controllers;

/// <summary>
/// Consulta vendas e recebe vendas confirmadas do aplicativo de PDV.
/// O JWT deve pertencer ao cliente; o estabelecimento é validado contra esse cliente.
/// </summary>
[Authorize(Roles = "CLIENTE")]
[ApiController]
[Route("api/[controller]")]
public sealed class VendasController : ControllerBase
{
    private const decimal MaxValor = 9_999_999_999.99m;
    private readonly MerkditoDbContext _db;

    public VendasController(MerkditoDbContext db) => _db = db;

    /// <summary>
    /// Sincroniza uma venda confirmada. IdVendaExterna torna o envio idempotente:
    /// repetir a mesma venda não deduz o estoque pela segunda vez.
    /// </summary>
    [HttpPost("sincronizar")]
    public async Task<IActionResult> Sincronizar([FromBody] SincronizarVendaRequest request)
    {
        var clienteId = ObterClienteId();
        if (!clienteId.HasValue) return Unauthorized();

        var idExterno = request.IdVendaExterna?.Trim();
        if (string.IsNullOrWhiteSpace(idExterno) || idExterno.Length > 100)
            return BadRequest("IdVendaExterna é obrigatório e deve ter até 100 caracteres.");
        if (request.DataVendaUtc == default)
            return BadRequest("Informe DataVendaUtc em formato ISO 8601.");
        if (request.Itens is null || request.Itens.Count is < 1 or > 200)
            return BadRequest("A venda deve conter de 1 a 200 itens.");
        if (request.Itens.Any(item => item is null))
            return BadRequest("A lista contém um item nulo. Remova itens vazios do payload.");
        if ((request.Origem?.Trim().Length ?? 0) > 30)
            return BadRequest("Origem deve ter até 30 caracteres.");

        var dataVendaUtc = request.DataVendaUtc.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(request.DataVendaUtc, DateTimeKind.Utc)
            : request.DataVendaUtc.ToUniversalTime();

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        var lojasQuery = _db.Estabelecimentos
            .Where(e => e.ClienteId == clienteId.Value && e.Ativo && !e.EhEstoqueCentral);
        if (request.EstabelecimentoId.HasValue)
            lojasQuery = lojasQuery.Where(e => e.Id == request.EstabelecimentoId.Value);
        else
        {
            var quantidadeLojas = await lojasQuery.CountAsync();
            if (quantidadeLojas != 1)
                return BadRequest("Informe EstabelecimentoId da loja de venda. A venda não pode ser lançada no estoque central e a conta possui zero ou várias lojas de venda.");
        }

        var estabelecimento = await lojasQuery.FirstOrDefaultAsync();
        if (estabelecimento is null)
            return BadRequest("A loja de venda não existe, está inativa, é o estoque central ou não pertence à conta autenticada.");

        // O produto é global ao cliente; preço e saldo usados na venda são os da loja selecionada.
        var produtos = await _db.Produtos
            .Where(p => p.ClienteId == clienteId.Value && p.Ativo)
            .ToListAsync();
        var estoquesLoja = await _db.ProdutosEstabelecimentos
            .Where(pe => pe.EstabelecimentoId == estabelecimento.Id)
            .ToDictionaryAsync(pe => pe.ProdutoId);
        var porId = produtos.ToDictionary(p => p.Id);
        var porCodigo = produtos
            .Where(p => !string.IsNullOrWhiteSpace(p.CodigoBarras))
            .GroupBy(p => p.CodigoBarras!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single(), StringComparer.OrdinalIgnoreCase);

        var linhas = new List<LinhaResolvida>(request.Itens.Count);
        foreach (var item in request.Itens)
        {
            if (item.Quantidade <= 0 || item.Quantidade > 1_000_000)
                return BadRequest("A quantidade de cada item deve estar entre 1 e 1.000.000.");
            var codigo = item.CodigoBarras?.Trim();
            Produto? porIdProduto = null;
            Produto? porCodigoProduto = null;
            if (item.ProdutoId.HasValue && item.ProdutoId.Value > 0)
                porId.TryGetValue(item.ProdutoId.Value, out porIdProduto);
            if (!string.IsNullOrWhiteSpace(codigo))
                porCodigo.TryGetValue(codigo, out porCodigoProduto);

            if (item.ProdutoId.HasValue && item.ProdutoId.Value > 0 && porIdProduto is null)
                return BadRequest($"Produto ID {item.ProdutoId.Value} não pertence ao estabelecimento selecionado.");
            if (!string.IsNullOrWhiteSpace(codigo) && porCodigoProduto is null)
                return BadRequest($"Código de barras '{codigo}' não foi encontrado neste estabelecimento.");
            if (porIdProduto is null && porCodigoProduto is null)
                return BadRequest("Cada item precisa informar ProdutoId ou CodigoBarras válido.");
            if (porIdProduto is not null && porCodigoProduto is not null && porIdProduto.Id != porCodigoProduto.Id)
                return BadRequest("ProdutoId e CodigoBarras apontam para produtos diferentes.");

            var produto = porIdProduto ?? porCodigoProduto!;
            if (!estoquesLoja.TryGetValue(produto.Id, out var estoqueLoja) || !estoqueLoja.Ativo)
                return BadRequest($"O produto '{produto.Nome}' não está vinculado ou ativo na loja selecionada.");
            // O preço oficial para esta venda vem da configuração da loja na API,
            // não do valor livre enviado pelo aplicativo. Isso mantém a regra por loja.
            var preco = Math.Round(estoqueLoja.PrecoVenda, 2, MidpointRounding.AwayFromZero);
            var subtotal = preco * item.Quantidade;
            if (subtotal > MaxValor)
                return BadRequest($"O total do item '{produto.Nome}' excede o limite suportado.");
            linhas.Add(new LinhaResolvida(produto, estoqueLoja, item.Quantidade, preco, subtotal));
        }

        var vendaExistente = await _db.Vendas
            .Include(v => v.Itens)
            .FirstOrDefaultAsync(v => v.EstabelecimentoId == estabelecimento.Id && v.IdVendaExterna == idExterno);
        if (vendaExistente is not null)
        {
            if (!PayloadEquivalente(vendaExistente, linhas, dataVendaUtc))
                return Conflict("IdVendaExterna já foi utilizado por outra venda com dados diferentes.");

            return Ok(new
            {
                id = vendaExistente.Id,
                idVendaExterna = vendaExistente.IdVendaExterna,
                dataVendaUtc = vendaExistente.DataVendaUtc,
                total = vendaExistente.Total,
                status = vendaExistente.Status,
                jaSincronizada = true,
                mensagem = "Esta venda já havia sido sincronizada; o estoque não foi alterado novamente."
            });
        }

        var quantidadesPorProduto = linhas
            .GroupBy(l => l.Produto.Id)
            .Select(g => new { Produto = g.First().Produto, EstoqueLoja = g.First().EstoqueLoja, Quantidade = g.Sum(x => x.Quantidade) })
            .ToList();

        foreach (var grupo in quantidadesPorProduto)
        {
            if (grupo.Quantidade > grupo.EstoqueLoja.QuantidadeEstoque)
                return Conflict($"Estoque insuficiente para '{grupo.Produto.Nome}' nesta loja. Saldo atual: {grupo.EstoqueLoja.QuantidadeEstoque}; quantidade vendida: {grupo.Quantidade}.");
        }

        var totalVenda = linhas.Sum(l => l.Subtotal);
        if (totalVenda > MaxValor)
            return BadRequest("O total da venda excede o limite suportado.");

        var venda = new Venda
        {
            EstabelecimentoId = estabelecimento.Id,
            IdVendaExterna = idExterno,
            DataVendaUtc = dataVendaUtc,
            Total = totalVenda,
            Status = "CONFIRMADA",
            Origem = string.IsNullOrWhiteSpace(request.Origem) ? "APP_VENDAS" : request.Origem.Trim(),
            CriadoEmUtc = DateTime.UtcNow
        };

        foreach (var linha in linhas)
        {
            venda.Itens.Add(new ItemVenda
            {
                ProdutoId = linha.Produto.Id,
                NomeProduto = linha.Produto.Nome,
                CodigoBarrasProduto = linha.Produto.CodigoBarras,
                Quantidade = linha.Quantidade,
                PrecoUnitario = linha.PrecoUnitario,
                TotalItem = linha.Subtotal
            });
        }

        // A saída de estoque, o histórico e a venda são persistidos na mesma transação.
        foreach (var grupo in quantidadesPorProduto)
        {
            var anterior = grupo.EstoqueLoja.QuantidadeEstoque;
            var posterior = anterior - grupo.Quantidade;
            grupo.EstoqueLoja.QuantidadeEstoque = posterior;
            _db.MovimentacoesEstoque.Add(new MovimentacaoEstoque
            {
                ProdutoId = grupo.Produto.Id,
                EstabelecimentoId = estabelecimento.Id,
                Tipo = "SAIDA",
                Quantidade = grupo.Quantidade,
                QuantidadeAnterior = anterior,
                QuantidadePosterior = posterior,
                Observacao = $"Venda sincronizada {idExterno}".Length <= 300
                    ? $"Venda sincronizada {idExterno}"
                    : $"Venda sincronizada {idExterno[..Math.Min(idExterno.Length, 260)]}",
                CriadoEmUtc = DateTime.UtcNow
            });
        }

        _db.Vendas.Add(venda);
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        return CreatedAtAction(nameof(Detalhe), new { id = venda.Id }, new
        {
            id = venda.Id,
            idVendaExterna = venda.IdVendaExterna,
            estabelecimentoId = venda.EstabelecimentoId,
            dataVendaUtc = venda.DataVendaUtc,
            total = venda.Total,
            status = venda.Status,
            quantidadeItens = venda.Itens.Count,
            totalUnidades = venda.Itens.Sum(i => i.Quantidade),
            jaSincronizada = false
        });
    }

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] DateOnly? de,
        [FromQuery] DateOnly? ate,
        [FromQuery] string? busca,
        [FromQuery] int? estabelecimentoId,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamanhoPagina = 50,
        [FromQuery] string? origem = null)
    {
        var clienteId = ObterClienteId();
        if (!clienteId.HasValue) return Unauthorized();
        if (de.HasValue && ate.HasValue && de.Value > ate.Value)
            return BadRequest("A data inicial não pode ser posterior à data final.");

        if (estabelecimentoId.HasValue && !await _db.Estabelecimentos.AnyAsync(e =>
            e.Id == estabelecimentoId.Value && e.ClienteId == clienteId.Value && !e.EhEstoqueCentral))
            return NotFound("Loja de venda não encontrada nesta conta.");

        pagina = Math.Max(1, pagina);
        tamanhoPagina = Math.Clamp(tamanhoPagina, 1, 200);
        var consulta = AplicarPeriodo(_db.Vendas.AsNoTracking()
            .Where(v => v.Estabelecimento != null && v.Estabelecimento.ClienteId == clienteId.Value), de, ate);
        if (estabelecimentoId.HasValue)
            consulta = consulta.Where(v => v.EstabelecimentoId == estabelecimentoId.Value);
        // Business portal and mobile use the same Vendas table. Origin is server-filtered
        // so paging does not hide purchases coming from the app.
        if (!string.IsNullOrWhiteSpace(origem))
        {
            var origemExata = origem.Trim();
            if (origemExata.Length > 30) return BadRequest("Origem excede 30 caracteres.");
            consulta = consulta.Where(v => v.Origem == origemExata);
        }
        if (!string.IsNullOrWhiteSpace(busca))
        {
            var termo = busca.Trim();
            consulta = consulta.Where(v => v.IdVendaExterna.Contains(termo));
        }

        var totalRegistros = await consulta.CountAsync();
        var vendas = await consulta.OrderByDescending(v => v.DataVendaUtc).ThenByDescending(v => v.Id)
            .Skip((pagina - 1) * tamanhoPagina)
            .Take(tamanhoPagina)
            .Select(v => new
            {
                v.Id,
                v.IdVendaExterna,
                v.EstabelecimentoId,
                estabelecimento = v.Estabelecimento != null ? v.Estabelecimento.NomeFantasia : null,
                v.DataVendaUtc,
                v.Total,
                v.Status,
                v.Origem,
                quantidadeItens = v.Itens.Count,
                totalUnidades = v.Itens.Sum(i => (int?)i.Quantidade) ?? 0
            }).ToListAsync();

        return Ok(new { totalRegistros, pagina, tamanhoPagina, vendas });
    }

    [HttpGet("relatorio")]
    public async Task<IActionResult> Relatorio([FromQuery] DateOnly? de, [FromQuery] DateOnly? ate, [FromQuery] int? estabelecimentoId)
    {
        var clienteId = ObterClienteId();
        if (!clienteId.HasValue) return Unauthorized();
        if (de.HasValue && ate.HasValue && de.Value > ate.Value)
            return BadRequest("A data inicial não pode ser posterior à data final.");

        if (estabelecimentoId.HasValue && !await _db.Estabelecimentos.AnyAsync(e =>
            e.Id == estabelecimentoId.Value && e.ClienteId == clienteId.Value && !e.EhEstoqueCentral))
            return NotFound("Loja de venda não encontrada nesta conta.");

        var inicioPadrao = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-29));
        var fimPadrao = DateOnly.FromDateTime(DateTime.UtcNow);
        var dataDe = de ?? inicioPadrao;
        var dataAte = ate ?? fimPadrao;
        var consulta = AplicarPeriodo(_db.Vendas.AsNoTracking()
            .Where(v => v.Estabelecimento != null && v.Estabelecimento.ClienteId == clienteId.Value), dataDe, dataAte);
        if (estabelecimentoId.HasValue)
            consulta = consulta.Where(v => v.EstabelecimentoId == estabelecimentoId.Value);

        // A demo order is REGISTERED, not paid. Exclude this status/source pair
        // from financial metrics without hiding it from the operational sales list.
        consulta = consulta.Where(v => !(v.Origem == "APP_CONSUMIDOR" && v.Status == "REGISTRADA"));
        var quantidadeVendas = await consulta.CountAsync();
        var faturamento = await consulta.Select(v => (decimal?)v.Total).SumAsync() ?? 0m;
        var ticketMedio = quantidadeVendas == 0 ? 0m : faturamento / quantidadeVendas;

        var vendasPorDia = await consulta
            .GroupBy(v => v.DataVendaUtc.Date)
            .Select(g => new { dataUtc = g.Key, quantidadeVendas = g.Count(), faturamento = g.Sum(v => v.Total) })
            .OrderBy(g => g.dataUtc)
            .ToListAsync();

        var itensNoPeriodo = _db.ItensVenda.AsNoTracking()
            .Where(i => i.Venda != null && i.Venda.Estabelecimento != null &&
                i.Venda.Estabelecimento.ClienteId == clienteId.Value &&
                i.Venda.DataVendaUtc >= DateTime.SpecifyKind(dataDe.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc) &&
                i.Venda.DataVendaUtc < DateTime.SpecifyKind(dataAte.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc) &&
                !(i.Venda.Origem == "APP_CONSUMIDOR" && i.Venda.Status == "REGISTRADA"));
        if (estabelecimentoId.HasValue)
            itensNoPeriodo = itensNoPeriodo.Where(i => i.Venda!.EstabelecimentoId == estabelecimentoId.Value);

        var produtosMaisVendidos = await itensNoPeriodo
            .GroupBy(i => new { i.ProdutoId, i.NomeProduto })
            .Select(g => new
            {
                produtoId = g.Key.ProdutoId,
                produto = g.Key.NomeProduto,
                unidadesVendidas = g.Sum(i => i.Quantidade),
                faturamento = g.Sum(i => i.TotalItem)
            })
            .OrderByDescending(x => x.unidadesVendidas)
            .Take(10)
            .ToListAsync();

        var unidadesVendidas = await itensNoPeriodo.SumAsync(i => (int?)i.Quantidade) ?? 0;

        return Ok(new
        {
            de = dataDe,
            ate = dataAte,
            quantidadeVendas,
            faturamento,
            ticketMedio,
            unidadesVendidas,
            vendasPorDia,
            produtosMaisVendidos
        });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detalhe(int id)
    {
        var clienteId = ObterClienteId();
        if (!clienteId.HasValue) return Unauthorized();

        var venda = await _db.Vendas.AsNoTracking()
            .Where(v => v.Id == id && v.Estabelecimento != null && v.Estabelecimento.ClienteId == clienteId.Value)
            .Select(v => new
            {
                v.Id,
                v.IdVendaExterna,
                v.EstabelecimentoId,
                estabelecimento = v.Estabelecimento != null ? v.Estabelecimento.NomeFantasia : null,
                v.DataVendaUtc,
                v.Total,
                v.Status,
                v.Origem,
                v.CriadoEmUtc,
                itens = v.Itens.OrderBy(i => i.Id).Select(i => new
                {
                    i.Id,
                    i.ProdutoId,
                    i.NomeProduto,
                    i.CodigoBarrasProduto,
                    i.Quantidade,
                    i.PrecoUnitario,
                    i.TotalItem
                }).ToList()
            }).FirstOrDefaultAsync();
        return venda is null ? NotFound() : Ok(venda);
    }

    private int? ObterClienteId()
    {
        var value = User.FindFirstValue("ClienteId");
        return int.TryParse(value, out var clienteId) && clienteId > 0 ? clienteId : null;
    }

    private static IQueryable<Venda> AplicarPeriodo(IQueryable<Venda> query, DateOnly? de, DateOnly? ate)
    {
        if (de.HasValue)
        {
            var inicio = DateTime.SpecifyKind(de.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            query = query.Where(v => v.DataVendaUtc >= inicio);
        }
        if (ate.HasValue)
        {
            var fim = DateTime.SpecifyKind(ate.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            query = query.Where(v => v.DataVendaUtc < fim);
        }
        return query;
    }

    private static bool PayloadEquivalente(Venda existente, IReadOnlyCollection<LinhaResolvida> linhas, DateTime dataVendaUtc)
    {
        if (existente.DataVendaUtc != dataVendaUtc || existente.Itens.Count == 0 || linhas.Count == 0)
            return false;

        var itensExistentes = existente.Itens
            .GroupBy(i => i.ProdutoId)
            .Select(g => (ProdutoId: g.Key, Quantidade: g.Sum(i => i.Quantidade)))
            .OrderBy(x => x.ProdutoId).ToList();
        var itensRecebidos = linhas
            .GroupBy(i => i.Produto.Id)
            .Select(g => (ProdutoId: g.Key, Quantidade: g.Sum(i => i.Quantidade)))
            .OrderBy(x => x.ProdutoId).ToList();

        return itensExistentes.SequenceEqual(itensRecebidos);
    }

    private sealed record LinhaResolvida(Produto Produto, ProdutoEstabelecimento EstoqueLoja, int Quantidade, decimal PrecoUnitario, decimal Subtotal);
}

public sealed class SincronizarVendaRequest
{
    public string IdVendaExterna { get; set; } = string.Empty;
    public int? EstabelecimentoId { get; set; }
    /// <summary>Timestamp ISO 8601, preferencialmente com sufixo Z (UTC).</summary>
    public DateTime DataVendaUtc { get; set; }
    public string? Origem { get; set; }
    public List<ItemVendaRequest> Itens { get; set; } = new();
}

public sealed class ItemVendaRequest
{
    public int? ProdutoId { get; set; }
    public string? CodigoBarras { get; set; }
    public int Quantidade { get; set; }
}
