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
public sealed class FornecedoresController(MerkditoDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] bool incluirInativos = false)
    {
        var clienteId = ClienteId();
        if (clienteId is null) return Unauthorized();
        var q = db.Fornecedores.AsNoTracking().Where(f => f.ClienteId == clienteId.Value);
        if (!incluirInativos) q = q.Where(f => f.Ativo);
        var lista = await q.OrderBy(f => f.Nome)
            .Select(f => new { f.Id, f.Nome, f.Cnpj, f.Email, f.Telefone, f.Endereco, f.Ativo, f.CriadoEmUtc })
            .ToListAsync();
        return Ok(lista);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Buscar(int id)
    {
        var clienteId = ClienteId();
        if (clienteId is null) return Unauthorized();
        var f = await db.Fornecedores.AsNoTracking()
            .Where(x => x.Id == id && x.ClienteId == clienteId.Value)
            .Select(x => new { x.Id, x.Nome, x.Cnpj, x.Email, x.Telefone, x.Endereco, x.Ativo, x.CriadoEmUtc })
            .FirstOrDefaultAsync();
        return f is null ? NotFound(new { mensagem = "Fornecedor não encontrado nesta conta." }) : Ok(f);
    }

    [HttpPost]
    public async Task<IActionResult> Criar([FromBody] FornecedorRequest request)
    {
        var clienteId = ClienteId();
        if (clienteId is null) return Unauthorized();
        var erro = Validar(request);
        if (erro is not null) return BadRequest(new { mensagem = erro });
        var cnpj = Texto(request.Cnpj);
        if (cnpj is not null && await db.Fornecedores.AnyAsync(f => f.ClienteId == clienteId && f.Cnpj == cnpj))
            return Conflict(new { mensagem = "Já existe um fornecedor com esse CNPJ nesta conta." });

        var fornecedor = new Fornecedor
        {
            ClienteId = clienteId.Value, Nome = request.Nome.Trim(), Cnpj = cnpj,
            Email = Texto(request.Email), Telefone = Texto(request.Telefone),
            Endereco = Texto(request.Endereco), Ativo = request.Ativo, CriadoEmUtc = DateTime.UtcNow
        };
        db.Fornecedores.Add(fornecedor);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { return Conflict(new { mensagem = "Não foi possível cadastrar. Verifique se o CNPJ já está vinculado a outro fornecedor." }); }
        return CreatedAtAction(nameof(Buscar), new { id = fornecedor.Id }, new { fornecedor.Id, fornecedor.Nome });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Atualizar(int id, [FromBody] FornecedorRequest request)
    {
        var clienteId = ClienteId();
        if (clienteId is null) return Unauthorized();
        var erro = Validar(request);
        if (erro is not null) return BadRequest(new { mensagem = erro });
        var fornecedor = await db.Fornecedores.FirstOrDefaultAsync(f => f.Id == id && f.ClienteId == clienteId.Value);
        if (fornecedor is null) return NotFound(new { mensagem = "Fornecedor não encontrado nesta conta." });
        var cnpj = Texto(request.Cnpj);
        if (cnpj is not null && await db.Fornecedores.AnyAsync(f => f.ClienteId == clienteId && f.Id != id && f.Cnpj == cnpj))
            return Conflict(new { mensagem = "Já existe outro fornecedor com esse CNPJ nesta conta." });
        fornecedor.Nome = request.Nome.Trim(); fornecedor.Cnpj = cnpj;
        fornecedor.Email = Texto(request.Email); fornecedor.Telefone = Texto(request.Telefone);
        fornecedor.Endereco = Texto(request.Endereco); fornecedor.Ativo = request.Ativo;
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { return Conflict(new { mensagem = "Não foi possível salvar os dados do fornecedor." }); }
        return NoContent();
    }

    // Fornecedores vinculados a compras são inativados, nunca removidos fisicamente.
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Inativar(int id)
    {
        var clienteId = ClienteId();
        if (clienteId is null) return Unauthorized();
        var fornecedor = await db.Fornecedores.FirstOrDefaultAsync(f => f.Id == id && f.ClienteId == clienteId.Value);
        if (fornecedor is null) return NotFound(new { mensagem = "Fornecedor não encontrado nesta conta." });
        fornecedor.Ativo = false;
        await db.SaveChangesAsync();
        return NoContent();
    }

    private int? ClienteId() => int.TryParse(User.FindFirstValue("ClienteId"), out var id) ? id : null;
    private static string? Texto(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? Validar(FornecedorRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Nome) || r.Nome.Trim().Length > 150) return "Informe o nome do fornecedor (até 150 caracteres).";
        if ((r.Cnpj?.Trim().Length ?? 0) > 18) return "O CNPJ deve ter no máximo 18 caracteres.";
        if ((r.Email?.Trim().Length ?? 0) > 254) return "O e-mail deve ter no máximo 254 caracteres.";
        if ((r.Telefone?.Trim().Length ?? 0) > 30) return "O telefone deve ter no máximo 30 caracteres.";
        if ((r.Endereco?.Trim().Length ?? 0) > 250) return "O endereço deve ter no máximo 250 caracteres.";
        return null;
    }
}

public sealed class FornecedorRequest
{
    public string Nome { get; set; } = string.Empty;
    public string? Cnpj { get; set; }
    public string? Email { get; set; }
    public string? Telefone { get; set; }
    public string? Endereco { get; set; }
    public bool Ativo { get; set; } = true;
}
