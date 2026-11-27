using Merkdito.Data.Context;
using Merkdito.Data.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Merkdito.Api.Controllers;

[Authorize(Roles = "ADMIN")]
[ApiController]
[Route("api/[controller]")]
public class EstabelecimentosController : ControllerBase
{
    private readonly MerkditoDbContext _context;

    public EstabelecimentosController(MerkditoDbContext context)
    {
        _context = context;
    }

    // GET: api/Estabelecimentos
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Estabelecimento>>>
        GetEstabelecimentos()
    {
        var estabelecimentos = await _context.Estabelecimentos
            .AsNoTracking()
            .Include(e => e.Cliente)
            .OrderBy(e => e.NomeFantasia)
            .Select(e => new
            {
                e.Id,
                e.ClienteId,
                ClienteNome = e.Cliente!.Nome,
                e.NomeFantasia,
                e.Cnpj,
                e.Ativo,
                e.CriadoEm
            })
            .ToListAsync();

        return Ok(estabelecimentos);
    }

    // GET: api/Estabelecimentos/5
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetEstabelecimento(int id)
    {
        var estabelecimento = await _context.Estabelecimentos
            .AsNoTracking()
            .Where(e => e.Id == id)
            .Select(e => new
            {
                e.Id,
                e.ClienteId,
                ClienteNome = e.Cliente!.Nome,
                e.NomeFantasia,
                e.Cnpj,
                e.Ativo,
                e.CriadoEm
            })
            .FirstOrDefaultAsync();

        if (estabelecimento is null)
        {
            return NotFound(new
            {
                mensagem = "Estabelecimento não encontrado."
            });
        }

        return Ok(estabelecimento);
    }

    // GET: api/Estabelecimentos/por-cliente/1
    [HttpGet("por-cliente/{clienteId:int}")]
    public async Task<IActionResult> GetPorCliente(int clienteId)
    {
        var clienteExiste = await _context.Clientes
            .AnyAsync(c => c.Id == clienteId);

        if (!clienteExiste)
        {
            return NotFound(new
            {
                mensagem = "Cliente não encontrado."
            });
        }

        var estabelecimentos = await _context.Estabelecimentos
            .AsNoTracking()
            .Where(e => e.ClienteId == clienteId)
            .OrderBy(e => e.NomeFantasia)
            .Select(e => new
            {
                e.Id,
                e.ClienteId,
                e.NomeFantasia,
                e.Cnpj,
                e.Ativo,
                e.CriadoEm
            })
            .ToListAsync();

        return Ok(estabelecimentos);
    }

    // POST: api/Estabelecimentos
    [HttpPost]
    public async Task<IActionResult> CriarEstabelecimento(
        [FromBody] Estabelecimento dados)
    {
        var nome = dados.NomeFantasia?.Trim();

        if (string.IsNullOrWhiteSpace(nome))
        {
            return BadRequest(new
            {
                mensagem = "O nome fantasia é obrigatório."
            });
        }

        var clienteExiste = await _context.Clientes
            .AnyAsync(c => c.Id == dados.ClienteId);

        if (!clienteExiste)
        {
            return BadRequest(new
            {
                mensagem = "O cliente informado não existe."
            });
        }

        var estabelecimento = new Estabelecimento
        {
            ClienteId = dados.ClienteId,
            NomeFantasia = nome,
            Cnpj = dados.Cnpj?.Trim(),
            Ativo = dados.Ativo,
            CriadoEm = DateTime.UtcNow
        };

        _context.Estabelecimentos.Add(estabelecimento);
        await _context.SaveChangesAsync();

        return CreatedAtAction(
            nameof(GetEstabelecimento),
            new { id = estabelecimento.Id },
            new
            {
                estabelecimento.Id,
                estabelecimento.ClienteId,
                estabelecimento.NomeFantasia,
                estabelecimento.Cnpj,
                estabelecimento.Ativo,
                estabelecimento.CriadoEm
            });
    }

    // PUT: api/Estabelecimentos/5
    [HttpPut("{id:int}")]
    public async Task<IActionResult> AtualizarEstabelecimento(
        int id,
        [FromBody] Estabelecimento dados)
    {
        var estabelecimento = await _context.Estabelecimentos
            .FirstOrDefaultAsync(e => e.Id == id);

        if (estabelecimento is null)
        {
            return NotFound(new
            {
                mensagem = "Estabelecimento não encontrado."
            });
        }

        var nome = dados.NomeFantasia?.Trim();

        if (string.IsNullOrWhiteSpace(nome))
        {
            return BadRequest(new
            {
                mensagem = "O nome fantasia é obrigatório."
            });
        }

        var clienteExiste = await _context.Clientes
            .AnyAsync(c => c.Id == dados.ClienteId);

        if (!clienteExiste)
        {
            return BadRequest(new
            {
                mensagem = "O cliente informado não existe."
            });
        }

        estabelecimento.ClienteId = dados.ClienteId;
        estabelecimento.NomeFantasia = nome;
        estabelecimento.Cnpj = dados.Cnpj?.Trim();
        estabelecimento.Ativo = dados.Ativo;

        await _context.SaveChangesAsync();

        return NoContent();
    }

    // DELETE: api/Estabelecimentos/5
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> ExcluirEstabelecimento(int id)
    {
        var estabelecimento = await _context.Estabelecimentos
            .Include(e => e.Produtos)
            .Include(e => e.Categorias)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (estabelecimento is null)
        {
            return NotFound(new
            {
                mensagem = "Estabelecimento não encontrado."
            });
        }

        if (estabelecimento.Produtos.Count > 0 ||
            estabelecimento.Categorias.Count > 0)
        {
            return Conflict(new
            {
                mensagem = "Não é possível excluir um estabelecimento que possui produtos ou categorias associados."
            });
        }

        _context.Estabelecimentos.Remove(estabelecimento);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}
