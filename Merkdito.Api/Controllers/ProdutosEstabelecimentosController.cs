using System.Data;
using System.Security.Claims;
using Merkdito.Data.Context;
using Merkdito.Data.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Merkdito.Api.Controllers;

/// <summary>Estoque, preço e disponibilidade do produto em cada loja.</summary>
[Authorize(Roles = "CLIENTE")]
[ApiController]
[Route("api/[controller]")]
public sealed class ProdutosEstabelecimentosController(MerkditoDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> ListarPorLoja([FromQuery] int? estabelecimentoId)
    {
        var clienteId = ClienteId();
        if (clienteId is null) return Unauthorized();

        var loja = estabelecimentoId.HasValue
            ? await db.Estabelecimentos.AsNoTracking().FirstOrDefaultAsync(e => e.Id == estabelecimentoId.Value && e.ClienteId == clienteId.Value)
            : await db.Estabelecimentos.AsNoTracking().FirstOrDefaultAsync(e => e.ClienteId == clienteId.Value && e.EhEstoqueCentral);
        if (loja is null) return NotFound(new { mensagem = "Loja não encontrada nesta conta." });

        var itens = await db.ProdutosEstabelecimentos.AsNoTracking()
            .Where(pe => pe.EstabelecimentoId == loja.Id && pe.Produto != null && pe.Produto.ClienteId == clienteId.Value)
            .OrderBy(pe => pe.Produto!.Nome)
            .Select(pe => new
            {
                pe.ProdutoId,
                nome = pe.Produto!.Nome,
                descricao = pe.Produto.Descricao,
                codigoBarras = pe.Produto.CodigoBarras,
                categoriaId = pe.Produto.CategoriaId,
                categoria = pe.Produto.Categoria != null ? pe.Produto.Categoria.Nome : null,
                precoVenda = pe.PrecoVenda,
                quantidadeEstoque = pe.QuantidadeEstoque,
                estoqueMinimo = pe.EstoqueMinimo,
                ativoNaLoja = pe.Ativo,
                produtoAtivo = pe.Produto.Ativo
            }).ToListAsync();

        return Ok(new { loja.Id, loja.NomeFantasia, loja.EhEstoqueCentral, itens });
    }

    /// <summary>
    /// Registra o produto na loja e transfere quantidade do estoque central.
    /// Quantidade zero permite vincular o produto sem transferir unidades ainda.
    /// </summary>
    [HttpPost("transferir")]
    public async Task<IActionResult> Transferir([FromBody] TransferirProdutoLojaRequest request)
    {
        var clienteId = ClienteId();
        if (clienteId is null) return Unauthorized();
        if (request.ProdutoId <= 0 || request.EstabelecimentoDestinoId <= 0)
            return BadRequest(new { mensagem = "Produto e loja de destino são obrigatórios." });
        if (request.Quantidade < 0 || request.Quantidade > 1_000_000)
            return BadRequest(new { mensagem = "A quantidade deve estar entre 0 e 1.000.000." });
        if (request.PrecoVenda is < 0 or > 99999999.99m)
            return BadRequest(new { mensagem = "O preço por loja é inválido." });
        if (request.EstoqueMinimo is < 0 or > 1_000_000)
            return BadRequest(new { mensagem = "O estoque mínimo é inválido." });

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var central = await db.Estabelecimentos.FirstOrDefaultAsync(e => e.ClienteId == clienteId.Value && e.EhEstoqueCentral && e.Ativo);
        if (central is null) return Conflict(new { mensagem = "A conta não possui um estoque central ativo." });
        var destino = await db.Estabelecimentos.FirstOrDefaultAsync(e => e.Id == request.EstabelecimentoDestinoId &&
            e.ClienteId == clienteId.Value && e.Ativo && !e.EhEstoqueCentral);
        if (destino is null) return NotFound(new { mensagem = "A loja de destino não existe, está inativa ou é o estoque central." });
        var produto = await db.Produtos.FirstOrDefaultAsync(p => p.Id == request.ProdutoId && p.ClienteId == clienteId.Value && p.Ativo);
        if (produto is null) return NotFound(new { mensagem = "Produto ativo não encontrado no catálogo desta empresa." });

        var estoqueCentral = await db.ProdutosEstabelecimentos.FirstOrDefaultAsync(pe => pe.ProdutoId == produto.Id && pe.EstabelecimentoId == central.Id);
        if (estoqueCentral is null)
        {
            estoqueCentral = new ProdutoEstabelecimento
            {
                ProdutoId = produto.Id, EstabelecimentoId = central.Id,
                PrecoVenda = produto.Preco, QuantidadeEstoque = Math.Max(0, produto.QuantidadeEstoque),
                EstoqueMinimo = produto.EstoqueMinimo, Ativo = produto.Ativo,
                CriadoEmUtc = DateTime.UtcNow
            };
            db.ProdutosEstabelecimentos.Add(estoqueCentral);
            await db.SaveChangesAsync();
        }
        if (request.Quantidade > estoqueCentral.QuantidadeEstoque)
            return Conflict(new { mensagem = $"Estoque central insuficiente. Disponível: {estoqueCentral.QuantidadeEstoque}; solicitado: {request.Quantidade}." });

        var estoqueDestino = await db.ProdutosEstabelecimentos.FirstOrDefaultAsync(pe => pe.ProdutoId == produto.Id && pe.EstabelecimentoId == destino.Id);
        var anteriorCentral = estoqueCentral.QuantidadeEstoque;
        var anteriorDestino = estoqueDestino?.QuantidadeEstoque ?? 0;

        if (estoqueDestino is null)
        {
            estoqueDestino = new ProdutoEstabelecimento
            {
                ProdutoId = produto.Id,
                EstabelecimentoId = destino.Id,
                PrecoVenda = request.PrecoVenda ?? estoqueCentral.PrecoVenda,
                QuantidadeEstoque = 0,
                EstoqueMinimo = request.EstoqueMinimo ?? estoqueCentral.EstoqueMinimo,
                Ativo = true,
                CriadoEmUtc = DateTime.UtcNow
            };
            db.ProdutosEstabelecimentos.Add(estoqueDestino);
        }
        else
        {
            if (request.PrecoVenda.HasValue) estoqueDestino.PrecoVenda = request.PrecoVenda.Value;
            if (request.EstoqueMinimo.HasValue) estoqueDestino.EstoqueMinimo = request.EstoqueMinimo.Value;
            estoqueDestino.Ativo = true;
        }

        if (request.Quantidade > 0)
        {
            estoqueCentral.QuantidadeEstoque -= request.Quantidade;
            estoqueDestino.QuantidadeEstoque += request.Quantidade;
            db.MovimentacoesEstoque.Add(new MovimentacaoEstoque
            {
                ProdutoId = produto.Id, EstabelecimentoId = central.Id, Tipo = "SAIDA",
                Quantidade = request.Quantidade, QuantidadeAnterior = anteriorCentral,
                QuantidadePosterior = estoqueCentral.QuantidadeEstoque,
                Observacao = $"Transferência para a loja {destino.NomeFantasia}.", CriadoEmUtc = DateTime.UtcNow
            });
            db.MovimentacoesEstoque.Add(new MovimentacaoEstoque
            {
                ProdutoId = produto.Id, EstabelecimentoId = destino.Id, Tipo = "ENTRADA",
                Quantidade = request.Quantidade, QuantidadeAnterior = anteriorDestino,
                QuantidadePosterior = estoqueDestino.QuantidadeEstoque,
                Observacao = $"Transferência recebida do estoque central ({central.NomeFantasia}).", CriadoEmUtc = DateTime.UtcNow
            });
            produto.QuantidadeEstoque = estoqueCentral.QuantidadeEstoque;
        }

        try
        {
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync();
            return Conflict(new { mensagem = "A transferência não foi concluída. Atualize a tela e confira o saldo antes de tentar novamente." });
        }

        return Ok(new
        {
            mensagem = request.Quantidade == 0 ? "Produto vinculado à loja sem movimentar unidades." : "Transferência concluída.",
            produtoId = produto.Id, centralId = central.Id, lojaId = destino.Id,
            quantidadeTransferida = request.Quantidade,
            saldoCentral = estoqueCentral.QuantidadeEstoque,
            saldoLoja = estoqueDestino.QuantidadeEstoque,
            precoVendaLoja = estoqueDestino.PrecoVenda
        });
    }

    /// <summary>Atualiza somente preço, mínimo e disponibilidade do produto na loja; quantidade muda por transferência.</summary>
    [HttpPut("{estabelecimentoId:int}/{produtoId:int}")]
    public async Task<IActionResult> AtualizarConfiguracaoLoja(int estabelecimentoId, int produtoId, [FromBody] AtualizarProdutoLojaRequest request)
    {
        var clienteId = ClienteId();
        if (clienteId is null) return Unauthorized();
        if (request.PrecoVenda < 0 || request.PrecoVenda > 99999999.99m || request.EstoqueMinimo < 0)
            return BadRequest(new { mensagem = "Preço ou estoque mínimo inválido." });

        var loja = await db.Estabelecimentos.FirstOrDefaultAsync(e => e.Id == estabelecimentoId && e.ClienteId == clienteId.Value && e.Ativo && !e.EhEstoqueCentral);
        if (loja is null) return NotFound(new { mensagem = "Loja de venda não encontrada nesta conta." });
        var vinculo = await db.ProdutosEstabelecimentos.FirstOrDefaultAsync(pe => pe.EstabelecimentoId == loja.Id && pe.ProdutoId == produtoId && pe.Produto != null && pe.Produto.ClienteId == clienteId.Value);
        if (vinculo is null) return NotFound(new { mensagem = "O produto ainda não está vinculado a esta loja. Use Vincular/transferir produto." });

        vinculo.PrecoVenda = request.PrecoVenda;
        vinculo.EstoqueMinimo = request.EstoqueMinimo;
        vinculo.Ativo = request.Ativo;
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Lista os produtos cujo saldo está no limite mínimo ou abaixo dele.</summary>
    [HttpGet("alertas")]
    public async Task<IActionResult> AlertasEstoque([FromQuery] int? estabelecimentoId)
    {
        var clienteId = ClienteId();
        if (clienteId is null) return Unauthorized();
        if (estabelecimentoId is <= 0)
            return BadRequest(new { mensagem = "O ID do estabelecimento é inválido." });

        if (estabelecimentoId.HasValue)
        {
            var existe = await db.Estabelecimentos.AnyAsync(e =>
                e.Id == estabelecimentoId.Value && e.ClienteId == clienteId.Value);
            if (!existe) return NotFound(new { mensagem = "Estabelecimento não encontrado nesta conta." });
        }

        var consulta = db.ProdutosEstabelecimentos.AsNoTracking()
            .Where(pe => pe.Estabelecimento != null && pe.Estabelecimento.ClienteId == clienteId.Value &&
                         pe.Estabelecimento.Ativo && pe.Ativo && pe.Produto != null &&
                         (pe.Produto.ClienteId == clienteId.Value ||
                          (pe.Produto.ClienteId == null && pe.Produto.Estabelecimento != null &&
                           pe.Produto.Estabelecimento.ClienteId == clienteId.Value)) &&
                         pe.Produto.Ativo && pe.QuantidadeEstoque <= pe.EstoqueMinimo);
        if (estabelecimentoId.HasValue)
            consulta = consulta.Where(pe => pe.EstabelecimentoId == estabelecimentoId.Value);

        var itens = await consulta
            .OrderBy(pe => pe.QuantidadeEstoque == 0 ? 0 : 1)
            .ThenBy(pe => pe.Estabelecimento!.NomeFantasia)
            .ThenBy(pe => pe.Produto!.Nome)
            .Select(pe => new
            {
                estabelecimentoId = pe.EstabelecimentoId,
                estabelecimento = pe.Estabelecimento!.NomeFantasia,
                ehEstoqueCentral = pe.Estabelecimento.EhEstoqueCentral,
                produtoId = pe.ProdutoId,
                produto = pe.Produto!.Nome,
                codigoBarras = pe.Produto.CodigoBarras,
                quantidadeEstoque = pe.QuantidadeEstoque,
                estoqueMinimo = pe.EstoqueMinimo,
                unidadesParaRepor = pe.EstoqueMinimo > pe.QuantidadeEstoque
                    ? pe.EstoqueMinimo - pe.QuantidadeEstoque : 0,
                situacao = pe.QuantidadeEstoque == 0 ? "ESGOTADO" : "BAIXO"
            }).ToListAsync();

        return Ok(new
        {
            totalAlertas = itens.Count,
            totalEsgotados = itens.Count(x => x.situacao == "ESGOTADO"),
            totalEstoqueBaixo = itens.Count(x => x.situacao == "BAIXO"),
            itens
        });
    }

    private int? ClienteId()
    {
        var raw = User.FindFirstValue("ClienteId");
        return int.TryParse(raw, out var id) && id > 0 ? id : null;
    }
}

public sealed class TransferirProdutoLojaRequest
{
    public int ProdutoId { get; set; }
    public int EstabelecimentoDestinoId { get; set; }
    public int Quantidade { get; set; }
    public decimal? PrecoVenda { get; set; }
    public int? EstoqueMinimo { get; set; }
}

public sealed class AtualizarProdutoLojaRequest
{
    public decimal PrecoVenda { get; set; }
    public int EstoqueMinimo { get; set; }
    public bool Ativo { get; set; } = true;
}
