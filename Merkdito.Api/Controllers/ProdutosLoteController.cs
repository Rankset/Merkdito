using System.Security.Claims;
using Merkdito.Data.Context;
using Merkdito.Data.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Merkdito.Api.Controllers;

/// <summary>
/// Processa importação/atualização em lote com uma transação por requisição.
/// Requer JWT já configurado no Program.cs e claim ClienteId emitida pelo login.
/// O cliente nunca informa EstabelecimentoId: o escopo é resolvido a partir do JWT.
/// </summary>
[Authorize(Roles = "CLIENTE")]
[ApiController]
[Route("api/[controller]")]
public sealed class ProdutosLoteController : ControllerBase
{
    private const int MaxItens = 500;
    private readonly MerkditoDbContext _db;

    public ProdutosLoteController(MerkditoDbContext db) => _db = db;

    [HttpPost("importar")]
    public async Task<IActionResult> Importar([FromBody] ImportarProdutosLoteRequest request)
    {
        if (request?.Itens is null || request.Itens.Count == 0)
            return BadRequest(new { mensagem = "Informe pelo menos um produto para importar." });
        if (request.Itens.Count > MaxItens)
            return BadRequest(new { mensagem = $"O limite é de {MaxItens} produtos por lote." });

        var clienteId = ObterClienteId();
        if (!clienteId.HasValue) return Unauthorized();
        var estabelecimentoId = await ObterEstabelecimentoAtivoId(clienteId.Value);
        if (!estabelecimentoId.HasValue)
            return BadRequest(new { mensagem = "Não existe estabelecimento ativo para este cliente." });

        await using var transaction = await _db.Database.BeginTransactionAsync();
        var erros = new List<ErroLinhaLote>();

        for (var i = 0; i < request.Itens.Count; i++)
        {
            var item = request.Itens[i];
            var linha = Linha(item.LinhaArquivo, i);
            if (string.IsNullOrWhiteSpace(item.Nome))
                erros.Add(new(linha, "O nome do produto é obrigatório."));
            else if (item.Nome.Trim().Length > 150)
                erros.Add(new(linha, "O nome deve ter no máximo 150 caracteres."));

            if (item.CodigoBarras is not null && item.CodigoBarras.Trim().Length > 100)
                erros.Add(new(linha, "O código de barras deve ter no máximo 100 caracteres."));
            if (item.Preco <= 0)
                erros.Add(new(linha, "O preço deve ser maior que zero."));
            if (item.QuantidadeEstoque < 0)
                erros.Add(new(linha, "A quantidade em estoque não pode ser negativa."));
            if (item.EstoqueMinimo < 0)
                erros.Add(new(linha, "O estoque mínimo não pode ser negativo."));
            if (item.CategoriaId <= 0)
                erros.Add(new(linha, "Informe uma categoria válida."));
        }

        var barcodeItems = request.Itens
            .Where(x => !string.IsNullOrWhiteSpace(x.CodigoBarras))
            .Select(x => new { Item = x, Code = x.CodigoBarras!.Trim() })
            .ToList();
        foreach (var group in barcodeItems.GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            foreach (var duplicate in group)
                erros.Add(new(Linha(duplicate.Item.LinhaArquivo, request.Itens.IndexOf(duplicate.Item)),
                    $"O código de barras '{group.Key}' aparece mais de uma vez neste lote."));
        }

        var categoryIds = request.Itens.Select(x => x.CategoriaId).Where(id => id > 0).Distinct().ToArray();
        var validCategoryIds = await _db.Categorias.AsNoTracking()
            .Where(c => categoryIds.Contains(c.Id) && c.Ativa && c.EstabelecimentoId == estabelecimentoId.Value)
            .Select(c => c.Id)
            .ToListAsync();
        var validCategories = validCategoryIds.ToHashSet();
        for (var i = 0; i < request.Itens.Count; i++)
        {
            var item = request.Itens[i];
            if (item.CategoriaId > 0 && !validCategories.Contains(item.CategoriaId))
                erros.Add(new(Linha(item.LinhaArquivo, i), "A categoria não existe, está inativa ou não pertence ao seu estabelecimento."));
        }

        var requestedCodes = barcodeItems.Select(x => x.Code).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (requestedCodes.Length > 0)
        {
            // O modelo atual possui índice único global para CodigoBarras; por compatibilidade,
            // a importação respeita essa regra até eventual decisão autorizada sobre migration.
            var existingCodes = await _db.Produtos.AsNoTracking()
                .Where(p => p.CodigoBarras != null && requestedCodes.Contains(p.CodigoBarras))
                .Select(p => p.CodigoBarras!)
                .ToListAsync();
            var existing = existingCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var duplicate in barcodeItems.Where(x => existing.Contains(x.Code)))
                erros.Add(new(Linha(duplicate.Item.LinhaArquivo, request.Itens.IndexOf(duplicate.Item)),
                    $"O código de barras '{duplicate.Code}' já está cadastrado."));
        }

        if (erros.Count > 0)
            return BadRequest(new
            {
                mensagem = "Nenhum produto foi cadastrado. Corrija os erros informados e envie o lote novamente.",
                erros = DeduplicarErros(erros)
            });

        var novos = request.Itens.Select(x => new Produto
        {
            Nome = x.Nome.Trim(),
            CodigoBarras = string.IsNullOrWhiteSpace(x.CodigoBarras) ? null : x.CodigoBarras.Trim(),
            Descricao = string.IsNullOrWhiteSpace(x.Descricao) ? null : x.Descricao.Trim(),
            Preco = x.Preco,
            QuantidadeEstoque = x.QuantidadeEstoque,
            EstoqueMinimo = x.EstoqueMinimo,
            CategoriaId = x.CategoriaId,
            Ativo = x.Ativo,
            ClienteId = clienteId.Value,
            EstabelecimentoId = estabelecimentoId.Value
        }).ToList();

        try
        {
            _db.Produtos.AddRange(novos);
            await _db.SaveChangesAsync();

            _db.ProdutosEstabelecimentos.AddRange(novos.Select(p => new ProdutoEstabelecimento
            {
                ProdutoId = p.Id,
                EstabelecimentoId = estabelecimentoId.Value,
                PrecoVenda = p.Preco,
                QuantidadeEstoque = p.QuantidadeEstoque,
                EstoqueMinimo = p.EstoqueMinimo,
                Ativo = p.Ativo,
                CriadoEmUtc = DateTime.UtcNow
            }));
            await _db.SaveChangesAsync();

            var saldosIniciais = novos
                .Where(p => p.QuantidadeEstoque > 0 && p.EstabelecimentoId.HasValue)
                .Select(p => new MovimentacaoEstoque
                {
                    ProdutoId = p.Id,
                    EstabelecimentoId = p.EstabelecimentoId!.Value,
                    Tipo = "ENTRADA",
                    Quantidade = p.QuantidadeEstoque,
                    QuantidadeAnterior = 0,
                    QuantidadePosterior = p.QuantidadeEstoque,
                    Observacao = "Saldo inicial registrado na importação de produtos.",
                    CriadoEmUtc = DateTime.UtcNow
                })
                .ToList();
            if (saldosIniciais.Count > 0)
            {
                _db.MovimentacoesEstoque.AddRange(saldosIniciais);
                await _db.SaveChangesAsync();
            }
            await transaction.CommitAsync();
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync();
            return Conflict(new
            {
                mensagem = "Nenhum produto foi confirmado. O lote entrou em conflito com dados existentes (por exemplo, código de barras duplicado). Atualize o catálogo e revise o arquivo antes de tentar novamente."
            });
        }

        return Ok(new
        {
            mensagem = "Importação concluída: todos os produtos válidos foram cadastrados.",
            totalCriado = novos.Count,
            itens = novos.Select(p => new { p.Id, p.Nome, p.CodigoBarras, p.CategoriaId })
        });
    }

    [HttpPut("atualizar-estoque")]
    public async Task<IActionResult> AtualizarEstoque([FromBody] AtualizarEstoqueLoteRequest request)
    {
        if (request?.Itens is null || request.Itens.Count == 0)
            return BadRequest(new { mensagem = "Informe ao menos um produto para atualizar." });
        if (request.Itens.Count > MaxItens)
            return BadRequest(new { mensagem = $"O limite é de {MaxItens} produtos por lote." });

        var erros = new List<ErroLinhaLote>();
        for (var i = 0; i < request.Itens.Count; i++)
        {
            var item = request.Itens[i];
            var linha = Linha(item.LinhaArquivo, i);
            if (item.ProdutoId <= 0) erros.Add(new(linha, "ID de produto inválido."));
            if (item.QuantidadeEstoque < 0) erros.Add(new(linha, "A quantidade final não pode ser negativa."));
        }
        foreach (var group in request.Itens.Select((item, index) => new { item, index })
                     .GroupBy(x => x.item.ProdutoId).Where(g => g.Key > 0 && g.Count() > 1))
        {
            foreach (var duplicate in group)
                erros.Add(new(Linha(duplicate.item.LinhaArquivo, duplicate.index),
                    $"O produto ID {group.Key} aparece mais de uma vez neste lote."));
        }
        if (erros.Count > 0)
            return BadRequest(new { mensagem = "Nenhum saldo foi alterado. Corrija os erros do lote.", erros = DeduplicarErros(erros) });

        var clienteId = ObterClienteId();
        if (!clienteId.HasValue) return Unauthorized();
        var estabelecimentoId = await ObterEstabelecimentoAtivoId(clienteId.Value);
        if (!estabelecimentoId.HasValue)
            return BadRequest(new { mensagem = "Não existe estabelecimento ativo para este cliente." });

        await using var transaction = await _db.Database.BeginTransactionAsync();
        var ids = request.Itens.Select(x => x.ProdutoId).Distinct().ToArray();
        var produtos = await _db.Produtos
            .Where(p => (p.ClienteId == clienteId.Value || p.EstabelecimentoId == estabelecimentoId.Value) && ids.Contains(p.Id))
            .ToListAsync();
        var porId = produtos.ToDictionary(p => p.Id);
        var estoquesCentrais = await _db.ProdutosEstabelecimentos
            .Where(pe => pe.EstabelecimentoId == estabelecimentoId.Value && ids.Contains(pe.ProdutoId))
            .ToDictionaryAsync(pe => pe.ProdutoId);
        for (var i = 0; i < request.Itens.Count; i++)
        {
            var item = request.Itens[i];
            if (!porId.ContainsKey(item.ProdutoId))
                erros.Add(new(Linha(item.LinhaArquivo, i), "Produto não encontrado no catálogo desta empresa."));
            else if (!estoquesCentrais.ContainsKey(item.ProdutoId))
                erros.Add(new(Linha(item.LinhaArquivo, i), "O produto não possui vínculo com o estoque central."));
        }
        if (erros.Count > 0)
            return BadRequest(new { mensagem = "Nenhum saldo foi alterado. Um ou mais produtos não pertencem ao catálogo desta empresa.", erros = DeduplicarErros(erros) });

        foreach (var item in request.Itens)
        {
            var produto = porId[item.ProdutoId];
            var estoque = estoquesCentrais[item.ProdutoId];
            var anterior = estoque.QuantidadeEstoque;
            var posterior = item.QuantidadeEstoque;
            if (anterior == posterior)
                continue;

            estoque.QuantidadeEstoque = posterior;
            produto.QuantidadeEstoque = posterior; // campo legado espelha somente o estoque central
            _db.MovimentacoesEstoque.Add(new MovimentacaoEstoque
            {
                ProdutoId = produto.Id,
                EstabelecimentoId = estabelecimentoId.Value,
                Tipo = "AJUSTE",
                Quantidade = Math.Abs(posterior - anterior),
                QuantidadeAnterior = anterior,
                QuantidadePosterior = posterior,
                Observacao = "Ajuste realizado por atualização de estoque em lote no estoque central.",
                CriadoEmUtc = DateTime.UtcNow
            });
        }

        try
        {
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync();
            return Conflict(new { mensagem = "Nenhum saldo foi confirmado. Atualize o catálogo e tente novamente após revisar os dados." });
        }

        return Ok(new
        {
            mensagem = "Atualização de estoque concluída: todos os saldos foram salvos.",
            totalAtualizado = request.Itens.Count,
            itens = request.Itens.Select(x => new { x.LinhaArquivo, x.ProdutoId, x.QuantidadeEstoque })
        });
    }

    [HttpPut("atualizar-cadastro")]
    public async Task<IActionResult> AtualizarCadastro([FromBody] AtualizarCadastroProdutosLoteRequest request)
    {
        if (request?.Itens is null || request.Itens.Count == 0)
            return BadRequest(new { mensagem = "Informe ao menos um produto para atualizar." });
        if (request.Itens.Count > MaxItens)
            return BadRequest(new { mensagem = $"O limite é de {MaxItens} produtos por lote." });

        var erros = new List<ErroLinhaLote>();
        for (var i = 0; i < request.Itens.Count; i++)
        {
            var item = request.Itens[i];
            var linha = Linha(item.LinhaArquivo, i);
            if (item.ProdutoId <= 0) erros.Add(new(linha, "ID de produto inválido."));
            if (item.Nome is not null && (string.IsNullOrWhiteSpace(item.Nome) || item.Nome.Trim().Length > 150))
                erros.Add(new(linha, "O nome, quando informado, deve ter entre 1 e 150 caracteres."));
            if (item.Preco.HasValue && item.Preco.Value < 0)
                erros.Add(new(linha, "O preço não pode ser negativo."));
            if (item.EstoqueMinimo.HasValue && item.EstoqueMinimo.Value < 0)
                erros.Add(new(linha, "O estoque mínimo não pode ser negativo."));
            if (item.Nome is null && item.Descricao is null && !item.Preco.HasValue &&
                !item.EstoqueMinimo.HasValue && !item.Ativo.HasValue)
                erros.Add(new(linha, "A linha não contém nenhum campo para atualizar."));
        }
        foreach (var group in request.Itens.Select((item, index) => new { item, index })
                     .GroupBy(x => x.item.ProdutoId).Where(g => g.Key > 0 && g.Count() > 1))
        {
            foreach (var duplicate in group)
                erros.Add(new(Linha(duplicate.item.LinhaArquivo, duplicate.index),
                    $"O produto ID {group.Key} aparece mais de uma vez neste lote."));
        }
        if (erros.Count > 0)
            return BadRequest(new { mensagem = "Nenhuma alteração cadastral foi salva. Corrija os erros do lote.", erros = DeduplicarErros(erros) });

        var clienteId = ObterClienteId();
        if (!clienteId.HasValue) return Unauthorized();
        var estabelecimentoId = await ObterEstabelecimentoAtivoId(clienteId.Value);
        if (!estabelecimentoId.HasValue)
            return BadRequest(new { mensagem = "Não existe estabelecimento ativo para este cliente." });

        await using var transaction = await _db.Database.BeginTransactionAsync();
        var ids = request.Itens.Select(x => x.ProdutoId).Distinct().ToArray();
        var produtos = await _db.Produtos
            .Where(p => (p.ClienteId == clienteId.Value || p.EstabelecimentoId == estabelecimentoId.Value) && ids.Contains(p.Id))
            .ToListAsync();
        var porId = produtos.ToDictionary(p => p.Id);
        var estoquesCentrais = await _db.ProdutosEstabelecimentos
            .Where(pe => pe.EstabelecimentoId == estabelecimentoId.Value && ids.Contains(pe.ProdutoId))
            .ToDictionaryAsync(pe => pe.ProdutoId);
        for (var i = 0; i < request.Itens.Count; i++)
        {
            var item = request.Itens[i];
            if (!porId.ContainsKey(item.ProdutoId))
                erros.Add(new(Linha(item.LinhaArquivo, i), "Produto não encontrado no catálogo desta empresa."));
        }
        if (erros.Count > 0)
            return BadRequest(new { mensagem = "Nenhuma alteração cadastral foi salva. Um ou mais produtos não pertencem ao catálogo desta empresa.", erros = DeduplicarErros(erros) });

        foreach (var item in request.Itens)
        {
            var produto = porId[item.ProdutoId];
            if (item.Nome is not null) produto.Nome = item.Nome.Trim();
            if (item.Descricao is not null)
                produto.Descricao = string.IsNullOrWhiteSpace(item.Descricao) ? null : item.Descricao.Trim();
            if (item.Preco.HasValue) produto.Preco = item.Preco.Value;
            if (item.EstoqueMinimo.HasValue) produto.EstoqueMinimo = item.EstoqueMinimo.Value;
            if (item.Ativo.HasValue) produto.Ativo = item.Ativo.Value;
            if (estoquesCentrais.TryGetValue(produto.Id, out var estoqueCentralProduto))
            {
                if (item.Preco.HasValue) estoqueCentralProduto.PrecoVenda = item.Preco.Value;
                if (item.EstoqueMinimo.HasValue) estoqueCentralProduto.EstoqueMinimo = item.EstoqueMinimo.Value;
                if (item.Ativo.HasValue) estoqueCentralProduto.Ativo = item.Ativo.Value;
            }
        }

        try
        {
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync();
            return Conflict(new { mensagem = "Nenhuma alteração cadastral foi confirmada. Atualize o catálogo e revise os dados antes de tentar novamente." });
        }

        return Ok(new
        {
            mensagem = "Atualização cadastral concluída: todas as alterações foram salvas.",
            totalAtualizado = request.Itens.Count,
            itens = request.Itens.Select(x => new { x.LinhaArquivo, x.ProdutoId })
        });
    }

    private int? ObterClienteId()
    {
        var valor = User.FindFirstValue("ClienteId");
        return int.TryParse(valor, out var clienteId) && clienteId > 0 ? clienteId : null;
    }

    private Task<int?> ObterEstabelecimentoAtivoId(int clienteId) => _db.Estabelecimentos
        .AsNoTracking()
        .Where(e => e.ClienteId == clienteId && e.Ativo && e.EhEstoqueCentral)
        .Select(e => (int?)e.Id)
        .FirstOrDefaultAsync();

    private static int Linha(int linhaArquivo, int index) => linhaArquivo > 0 ? linhaArquivo : index + 1;

    private static List<ErroLinhaLote> DeduplicarErros(IEnumerable<ErroLinhaLote> erros) =>
        erros.DistinctBy(e => (e.LinhaArquivo, e.Mensagem)).ToList();
}

public sealed class ImportarProdutosLoteRequest
{
    public List<ImportarProdutoLoteItem> Itens { get; set; } = new();
}

public sealed class ImportarProdutoLoteItem
{
    public int LinhaArquivo { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string? CodigoBarras { get; set; }
    public string? Descricao { get; set; }
    public decimal Preco { get; set; }
    public int QuantidadeEstoque { get; set; }
    public int EstoqueMinimo { get; set; }
    public int CategoriaId { get; set; }
    public bool Ativo { get; set; } = true;
}

public sealed class AtualizarEstoqueLoteRequest
{
    public List<AtualizarEstoqueLoteItem> Itens { get; set; } = new();
}

public sealed class AtualizarEstoqueLoteItem
{
    public int LinhaArquivo { get; set; }
    public int ProdutoId { get; set; }
    public int QuantidadeEstoque { get; set; }
}

public sealed class AtualizarCadastroProdutosLoteRequest
{
    public List<AtualizarCadastroProdutosLoteItem> Itens { get; set; } = new();
}

public sealed class AtualizarCadastroProdutosLoteItem
{
    public int LinhaArquivo { get; set; }
    public int ProdutoId { get; set; }
    public string? Nome { get; set; }
    /// <summary>Null/ausente mantém a descrição; string vazia limpa a descrição atual.</summary>
    public string? Descricao { get; set; }
    public decimal? Preco { get; set; }
    public int? EstoqueMinimo { get; set; }
    public bool? Ativo { get; set; }
}

public sealed record ErroLinhaLote(int LinhaArquivo, string Mensagem);
