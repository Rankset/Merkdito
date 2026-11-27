using System.Security.Claims;
using Merkdito.Data.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Merkdito.Api.Controllers;

/// <summary>
/// Endpoints de consulta e edição por estabelecimento para funcionários.
/// Cada operação verifica a permissão no banco e mantém os dados isolados por cliente.
/// </summary>
[ApiController]
[Route("api/funcionario/produtos")]
[Authorize(Roles = "FUNCIONARIO")]
public sealed class FuncionarioProdutosController(MerkditoDbContext db) : ControllerBase
{
    [HttpGet("estabelecimentos")]
    public async Task<IActionResult> Estabelecimentos(CancellationToken ct)
    {
        if (!TryGetIdentity(out var clienteId, out var funcionarioId)) return Unauthorized();
        var lojas = await db.UsuariosFuncionariosEstabelecimentos.AsNoTracking()
            .Where(a => a.UsuarioFuncionarioId == funcionarioId
                && (a.PodeVerProdutos || a.PodeEditarProdutos)
                && a.UsuarioFuncionario != null && a.UsuarioFuncionario.Ativo
                && a.UsuarioFuncionario.ClienteId == clienteId
                && a.Estabelecimento != null && a.Estabelecimento.Ativo
                && a.Estabelecimento.ClienteId == clienteId)
            .OrderBy(a => a.Estabelecimento!.NomeFantasia)
            .Select(a => new { id = a.EstabelecimentoId, nome = a.Estabelecimento!.NomeFantasia, podeEditar = a.PodeEditarProdutos })
            .ToListAsync(ct);
        return Ok(lojas);
    }

    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] int estabelecimentoId, CancellationToken ct)
    {
        if (!TryGetIdentity(out var clienteId, out var funcionarioId)) return Unauthorized();
        if (estabelecimentoId <= 0) return BadRequest(new { mensagem = "Informe um estabelecimento válido." });

        var autorizado = await db.UsuariosFuncionariosEstabelecimentos.AsNoTracking()
            .AnyAsync(a => a.UsuarioFuncionarioId == funcionarioId
                && a.EstabelecimentoId == estabelecimentoId
                && (a.PodeVerProdutos || a.PodeEditarProdutos)
                && a.Estabelecimento != null
                && a.Estabelecimento.ClienteId == clienteId
                && a.Estabelecimento.Ativo
                && a.UsuarioFuncionario != null
                && a.UsuarioFuncionario.Ativo
                && a.UsuarioFuncionario.ClienteId == clienteId, ct);
        if (!autorizado) return Forbid();

        var produtos = await db.ProdutosEstabelecimentos.AsNoTracking()
            .Where(pe => pe.EstabelecimentoId == estabelecimentoId
                && pe.Ativo
                && pe.Estabelecimento != null
                && pe.Estabelecimento.ClienteId == clienteId
                && pe.Produto != null
                && pe.Produto.Ativo
                && (pe.Produto.ClienteId == clienteId
                    || (pe.Produto.ClienteId == null && pe.Produto.Estabelecimento != null
                        && pe.Produto.Estabelecimento.ClienteId == clienteId)))
            .OrderBy(pe => pe.Produto!.Nome)
            .Select(pe => new
            {
                id = pe.ProdutoId,
                nome = pe.Produto!.Nome,
                descricao = pe.Produto.Descricao,
                codigoBarras = pe.Produto.CodigoBarras,
                preco = pe.PrecoVenda,
                quantidadeEstoque = pe.QuantidadeEstoque,
                estoqueMinimo = pe.EstoqueMinimo,
                ativo = pe.Ativo,
                categoriaId = pe.Produto.CategoriaId,
                categoria = pe.Produto.Categoria != null ? pe.Produto.Categoria.Nome : null,
                estabelecimentoId = pe.EstabelecimentoId
            })
            .ToListAsync(ct);

        return Ok(produtos);
    }

    [HttpGet("{produtoId:int}")]
    public async Task<IActionResult> Buscar(int produtoId, [FromQuery] int estabelecimentoId, CancellationToken ct)
    {
        if (!TryGetIdentity(out var clienteId, out var funcionarioId)) return Unauthorized();
        if (produtoId <= 0 || estabelecimentoId <= 0)
            return BadRequest(new { mensagem = "Informe produto e estabelecimento válidos." });

        var autorizado = await db.UsuariosFuncionariosEstabelecimentos.AsNoTracking()
            .AnyAsync(a => a.UsuarioFuncionarioId == funcionarioId
                && a.EstabelecimentoId == estabelecimentoId
                && (a.PodeVerProdutos || a.PodeEditarProdutos)
                && a.Estabelecimento != null
                && a.Estabelecimento.ClienteId == clienteId
                && a.Estabelecimento.Ativo
                && a.UsuarioFuncionario != null
                && a.UsuarioFuncionario.Ativo
                && a.UsuarioFuncionario.ClienteId == clienteId, ct);
        if (!autorizado) return Forbid();

        var produto = await db.ProdutosEstabelecimentos.AsNoTracking()
            .Where(pe => pe.ProdutoId == produtoId
                && pe.EstabelecimentoId == estabelecimentoId
                && pe.Ativo
                && pe.Estabelecimento != null
                && pe.Estabelecimento.ClienteId == clienteId
                && pe.Produto != null
                && pe.Produto.Ativo
                && (pe.Produto.ClienteId == clienteId
                    || (pe.Produto.ClienteId == null && pe.Produto.Estabelecimento != null
                        && pe.Produto.Estabelecimento.ClienteId == clienteId)))
            .Select(pe => new
            {
                id = pe.ProdutoId,
                nome = pe.Produto!.Nome,
                descricao = pe.Produto.Descricao,
                codigoBarras = pe.Produto.CodigoBarras,
                preco = pe.PrecoVenda,
                quantidadeEstoque = pe.QuantidadeEstoque,
                estoqueMinimo = pe.EstoqueMinimo,
                ativo = pe.Ativo,
                categoriaId = pe.Produto.CategoriaId,
                categoria = pe.Produto.Categoria != null ? pe.Produto.Categoria.Nome : null,
                estabelecimentoId = pe.EstabelecimentoId
            })
            .FirstOrDefaultAsync(ct);

        return produto is null ? NotFound() : Ok(produto);
    }

    [HttpPut("{produtoId:int}")]
    public async Task<IActionResult> AtualizarNoEstabelecimento(
        int produtoId,
        [FromBody] AtualizarProdutoFuncionarioRequest request,
        CancellationToken ct)
    {
        if (!TryGetIdentity(out var clienteId, out var funcionarioId)) return Unauthorized();
        if (produtoId <= 0 || request.EstabelecimentoId <= 0)
            return BadRequest(new { mensagem = "Produto e estabelecimento são obrigatórios." });
        if (request.PrecoVenda < 0 || request.PrecoVenda > 9_999_999_999.99m)
            return BadRequest(new { mensagem = "O preço deve estar entre 0 e 9.999.999.999,99." });
        if (request.EstoqueMinimo < 0 || request.EstoqueMinimo > 1_000_000)
            return BadRequest(new { mensagem = "O estoque mínimo deve estar entre 0 e 1.000.000." });

        var autorizado = await db.UsuariosFuncionariosEstabelecimentos.AsNoTracking()
            .AnyAsync(a => a.UsuarioFuncionarioId == funcionarioId
                && a.EstabelecimentoId == request.EstabelecimentoId
                && a.PodeEditarProdutos
                && a.UsuarioFuncionario != null && a.UsuarioFuncionario.Ativo
                && a.UsuarioFuncionario.ClienteId == clienteId
                && a.Estabelecimento != null && a.Estabelecimento.Ativo
                && a.Estabelecimento.ClienteId == clienteId, ct);
        if (!autorizado) return Forbid();

        var estoque = await db.ProdutosEstabelecimentos
            .Include(pe => pe.Produto)
            .Include(pe => pe.Estabelecimento)
            .FirstOrDefaultAsync(pe => pe.ProdutoId == produtoId
                && pe.EstabelecimentoId == request.EstabelecimentoId
                && pe.Ativo
                && pe.Estabelecimento != null && pe.Estabelecimento.ClienteId == clienteId
                && pe.Produto != null && pe.Produto.Ativo
                && (pe.Produto.ClienteId == clienteId
                    || (pe.Produto.ClienteId == null && pe.Produto.Estabelecimento != null
                        && pe.Produto.Estabelecimento.ClienteId == clienteId)), ct);
        if (estoque is null) return NotFound(new { mensagem = "Produto não encontrado ou inativo neste estabelecimento." });

        estoque.PrecoVenda = decimal.Round(request.PrecoVenda, 2, MidpointRounding.AwayFromZero);
        estoque.EstoqueMinimo = request.EstoqueMinimo;
        if (estoque.Estabelecimento?.EhEstoqueCentral == true && estoque.Produto is not null)
        {
            // Mantém os campos legados sincronizados com o preço e o mínimo do depósito central.
            estoque.Produto.Preco = estoque.PrecoVenda;
            estoque.Produto.EstoqueMinimo = estoque.EstoqueMinimo;
        }
        await db.SaveChangesAsync(ct);
        return Ok(new
        {
            mensagem = "Dados do produto atualizados para este estabelecimento.",
            produtoId,
            estabelecimentoId = request.EstabelecimentoId,
            precoVenda = estoque.PrecoVenda,
            estoqueMinimo = estoque.EstoqueMinimo
        });
    }

    private bool TryGetIdentity(out int clienteId, out int funcionarioId)
    {
        clienteId = 0;
        funcionarioId = 0;
        var clienteValido = int.TryParse(User.FindFirstValue("ClienteId"), out var parsedClienteId)
            && parsedClienteId > 0;
        var funcionarioValido = int.TryParse(User.FindFirstValue("FuncionarioId"), out var parsedFuncionarioId)
            && parsedFuncionarioId > 0;
        if (clienteValido) clienteId = parsedClienteId;
        if (funcionarioValido) funcionarioId = parsedFuncionarioId;
        return clienteValido && funcionarioValido;
    }
}


public sealed class AtualizarProdutoFuncionarioRequest
{
    public int EstabelecimentoId { get; set; }
    public decimal PrecoVenda { get; set; }
    public int EstoqueMinimo { get; set; }
}
