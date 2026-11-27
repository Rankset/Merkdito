using System.Security.Claims;
using Merkdito.Data.Context;
using Merkdito.Data.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Merkdito.Api.Controllers;

[Authorize(Roles = "ADMIN,CLIENTE")]
[ApiController]
[Route("api/[controller]")]
public sealed class CategoriasController(MerkditoDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        var categorias = await CategoriasVisiveis().AsNoTracking()
            .OrderBy(c => c.Nome)
            .Select(c => new { c.Id, c.Nome, c.Descricao, c.Ativa, c.EstabelecimentoId, c.ClienteId })
            .ToListAsync();
        return Ok(categorias);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Buscar(int id)
    {
        var categoria = await CategoriasVisiveis().AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new { c.Id, c.Nome, c.Descricao, c.Ativa, c.EstabelecimentoId, c.ClienteId })
            .FirstOrDefaultAsync();
        return categoria is null ? NotFound() : Ok(categoria);
    }

    [HttpPost]
    public async Task<IActionResult> Criar([FromBody] CriarCategoriaRequest dados)
    {
        if (dados.Id != 0)
            return BadRequest(new { mensagem = "Não informe o ID ao cadastrar." });
        if (string.IsNullOrWhiteSpace(dados.Nome) || dados.Nome.Trim().Length > 100)
            return BadRequest(new { mensagem = "Informe um nome válido de até 100 caracteres." });

        var estabelecimento = await ResolverEstabelecimentoParaGravacao(dados.EstabelecimentoId);
        if (estabelecimento.Erro is not null)
            return estabelecimento.Erro;

        var nome = dados.Nome.Trim();
        var clienteId = User.IsInRole("CLIENTE") ? ObterClienteId() :
            await db.Estabelecimentos.Where(e => e.Id == estabelecimento.Id).Select(e => (int?)e.ClienteId).FirstOrDefaultAsync();
        if (await NomeEmUso(nome, clienteId, estabelecimento.Id, null))
            return Conflict(new { mensagem = "Já existe uma categoria com esse nome no catálogo desta empresa." });

        var categoria = new Categoria
        {
            Nome = nome,
            Descricao = NormalizarTexto(dados.Descricao),
            Ativa = dados.Ativa,
            ClienteId = clienteId,
            EstabelecimentoId = estabelecimento.Id
        };
        db.Categorias.Add(categoria);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(Buscar), new { id = categoria.Id }, new
        {
            categoria.Id, categoria.Nome, categoria.Descricao, categoria.Ativa, categoria.EstabelecimentoId, categoria.ClienteId
        });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Atualizar(int id, [FromBody] AtualizarCategoriaRequest dados)
    {
        if (id != dados.Id)
            return BadRequest(new { mensagem = "O ID da URL não corresponde à categoria." });
        if (string.IsNullOrWhiteSpace(dados.Nome) || dados.Nome.Trim().Length > 100)
            return BadRequest(new { mensagem = "Nome da categoria inválido." });

        var categoria = await CategoriasVisiveis().FirstOrDefaultAsync(c => c.Id == id);
        if (categoria is null)
            return NotFound(new { mensagem = "Categoria não encontrada no catálogo autorizado." });

        var nome = dados.Nome.Trim();
        if (await NomeEmUso(nome, categoria.ClienteId, categoria.EstabelecimentoId, id))
            return Conflict(new { mensagem = "Já existe uma categoria com esse nome no catálogo desta empresa." });

        // O estabelecimento original é preservado; o DTO não pode transferir a categoria.
        categoria.Nome = nome;
        categoria.Descricao = NormalizarTexto(dados.Descricao);
        categoria.Ativa = dados.Ativa;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Excluir(int id)
    {
        var categoria = await CategoriasVisiveis().FirstOrDefaultAsync(c => c.Id == id);
        if (categoria is null)
            return NotFound(new { mensagem = "Categoria não encontrada no catálogo autorizado." });

        if (await db.Produtos.AnyAsync(p => p.CategoriaId == id))
            return Conflict(new { mensagem = "Não é possível excluir uma categoria que possui produtos; desative-a em vez disso." });

        db.Categorias.Remove(categoria);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private IQueryable<Categoria> CategoriasVisiveis()
    {
        var query = db.Categorias.AsQueryable();
        if (User.IsInRole("ADMIN"))
            return query;

        var clienteId = ObterClienteId();
        if (!clienteId.HasValue)
            return query.Where(_ => false);

        return query.Where(c => c.ClienteId == clienteId.Value ||
            (c.ClienteId == null && c.Estabelecimento != null &&
             c.Estabelecimento.ClienteId == clienteId.Value && c.Estabelecimento.Cliente != null &&
             c.Estabelecimento.Cliente.Ativo));
    }

    private async Task<(int? Id, IActionResult? Erro)> ResolverEstabelecimentoParaGravacao(int? idRecebido)
    {
        if (User.IsInRole("ADMIN"))
        {
            if (!idRecebido.HasValue)
                return (null, null); // Mantém categorias globais criadas pelo administrador.
            var existe = await db.Estabelecimentos.AnyAsync(e => e.Id == idRecebido.Value);
            return existe
                ? (idRecebido.Value, null)
                : (null, BadRequest(new { mensagem = "Estabelecimento inexistente." }));
        }

        var clienteId = ObterClienteId();
        if (!clienteId.HasValue)
            return (null, Unauthorized(new { mensagem = "O token não contém ClienteId válido." }));

        var estabelecimentoId = await db.Estabelecimentos
            .Where(e => e.ClienteId == clienteId.Value && e.Ativo && e.EhEstoqueCentral && e.Cliente != null && e.Cliente.Ativo)
            .Select(e => (int?)e.Id)
            .FirstOrDefaultAsync();

        return estabelecimentoId.HasValue
            ? (estabelecimentoId.Value, null)
            : (null, BadRequest(new { mensagem = "O cliente não possui estabelecimento ativo." }));
    }

    private Task<bool> NomeEmUso(string nome, int? clienteId, int? estabelecimentoId, int? ignorarId)
    {
        var query = db.Categorias.AsQueryable();
        if (clienteId.HasValue)
            query = query.Where(c => c.ClienteId == clienteId.Value);
        else
            query = query.Where(c => c.EstabelecimentoId == estabelecimentoId && c.ClienteId == null);
        if (ignorarId.HasValue)
            query = query.Where(c => c.Id != ignorarId.Value);
        var nomeNormalizado = nome.ToLower();
        return query.AnyAsync(c => c.Nome.ToLower() == nomeNormalizado);
    }

    private int? ObterClienteId()
    {
        var raw = User.FindFirstValue("ClienteId");
        return int.TryParse(raw, out var id) && id > 0 ? id : null;
    }

    private static string? NormalizarTexto(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public class CriarCategoriaRequest
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string? Descricao { get; set; }
    public bool Ativa { get; set; } = true;
    public int? EstabelecimentoId { get; set; }
}

public sealed class AtualizarCategoriaRequest : CriarCategoriaRequest { }
