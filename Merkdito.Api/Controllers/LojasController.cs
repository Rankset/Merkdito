using System.Security.Claims;
using Merkdito.Data.Context;
using Merkdito.Data.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Merkdito.Api.Controllers;

/// <summary>Gerencia as lojas de venda pertencentes ao cliente autenticado.</summary>
[Authorize(Roles = "CLIENTE")]
[ApiController]
[Route("api/[controller]")]
public sealed class LojasController(MerkditoDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        var clienteId = ClienteId();
        if (clienteId is null) return Unauthorized();

        var lojas = await db.Estabelecimentos.AsNoTracking()
            .Where(e => e.ClienteId == clienteId.Value && e.Cliente != null && e.Cliente.Ativo)
            .OrderByDescending(e => e.EhEstoqueCentral)
            .ThenBy(e => e.NomeFantasia)
            .Select(e => new
            {
                e.Id, e.NomeFantasia, e.Cnpj, e.Endereco, e.Telefone,
                e.Ativo, e.EhEstoqueCentral, e.CriadoEm,
                quantidadeProdutos = db.ProdutosEstabelecimentos.Count(pe => pe.EstabelecimentoId == e.Id),
                unidadesEstoque = db.ProdutosEstabelecimentos.Where(pe => pe.EstabelecimentoId == e.Id)
                    .Select(pe => (int?)pe.QuantidadeEstoque).Sum() ?? 0
            })
            .ToListAsync();
        return Ok(lojas);
    }

    [HttpGet("estoque-central")]
    public async Task<IActionResult> EstoqueCentral()
    {
        var clienteId = ClienteId();
        if (clienteId is null) return Unauthorized();
        var central = await db.Estabelecimentos.AsNoTracking()
            .Where(e => e.ClienteId == clienteId.Value && e.EhEstoqueCentral)
            .Select(e => new { e.Id, e.NomeFantasia, e.Ativo, e.EhEstoqueCentral })
            .FirstOrDefaultAsync();
        return central is null ? NotFound(new { mensagem = "O estoque central não foi configurado para esta conta." }) : Ok(central);
    }

    [HttpPost]
    public async Task<IActionResult> Criar([FromBody] LojaRequest request)
    {
        var clienteId = ClienteId();
        if (clienteId is null) return Unauthorized();
        var nome = request.NomeFantasia?.Trim();
        if (string.IsNullOrWhiteSpace(nome) || nome.Length > 150)
            return BadRequest(new { mensagem = "Informe o nome da loja com até 150 caracteres." });
        if (request.Cnpj?.Trim().Length > 18)
            return BadRequest(new { mensagem = "O CNPJ deve ter até 18 caracteres." });
        if (request.Endereco?.Trim().Length > 250)
            return BadRequest(new { mensagem = "O endereço deve ter até 250 caracteres." });
        if (request.Telefone?.Trim().Length > 30)
            return BadRequest(new { mensagem = "O telefone deve ter até 30 caracteres." });

        var clienteAtivo = await db.Clientes.AnyAsync(c => c.Id == clienteId.Value && c.Ativo);
        if (!clienteAtivo) return Forbid();
        var temCentral = await db.Estabelecimentos.AnyAsync(e => e.ClienteId == clienteId.Value && e.EhEstoqueCentral);
        if (!temCentral)
            return Conflict(new { mensagem = "A conta ainda não possui estoque central. Solicite a regularização ao administrador." });

        var loja = new Estabelecimento
        {
            ClienteId = clienteId.Value,
            NomeFantasia = nome,
            Cnpj = string.IsNullOrWhiteSpace(request.Cnpj) ? null : request.Cnpj.Trim(),
            Endereco = string.IsNullOrWhiteSpace(request.Endereco) ? null : request.Endereco.Trim(),
            Telefone = string.IsNullOrWhiteSpace(request.Telefone) ? null : request.Telefone.Trim(),
            Ativo = true,
            EhEstoqueCentral = false,
            CriadoEm = DateTime.UtcNow
        };
        db.Estabelecimentos.Add(loja);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Listar), new { id = loja.Id }, new
        {
            loja.Id, loja.NomeFantasia, loja.Cnpj, loja.Endereco, loja.Telefone,
            loja.Ativo, loja.EhEstoqueCentral, loja.CriadoEm
        });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Atualizar(int id, [FromBody] LojaRequest request)
    {
        var clienteId = ClienteId();
        if (clienteId is null) return Unauthorized();
        var loja = await db.Estabelecimentos.FirstOrDefaultAsync(e => e.Id == id && e.ClienteId == clienteId.Value);
        if (loja is null) return NotFound(new { mensagem = "Loja não encontrada nesta conta." });
        if (loja.EhEstoqueCentral)
            return Conflict(new { mensagem = "Os dados e a situação do estoque central não podem ser alterados por este formulário." });

        var nome = request.NomeFantasia?.Trim();
        if (string.IsNullOrWhiteSpace(nome) || nome.Length > 150)
            return BadRequest(new { mensagem = "Informe o nome da loja com até 150 caracteres." });
        if (request.Cnpj?.Trim().Length > 18 || request.Endereco?.Trim().Length > 250 || request.Telefone?.Trim().Length > 30)
            return BadRequest(new { mensagem = "CNPJ, endereço ou telefone excede o limite permitido." });

        loja.NomeFantasia = nome;
        loja.Cnpj = string.IsNullOrWhiteSpace(request.Cnpj) ? null : request.Cnpj.Trim();
        loja.Endereco = string.IsNullOrWhiteSpace(request.Endereco) ? null : request.Endereco.Trim();
        loja.Telefone = string.IsNullOrWhiteSpace(request.Telefone) ? null : request.Telefone.Trim();
        // Uma loja com vendas ou estoque não é apagada: fica inativa, preservando o histórico.
        loja.Ativo = request.Ativo;
        await db.SaveChangesAsync();
        return NoContent();
    }

    private int? ClienteId()
    {
        var raw = User.FindFirstValue("ClienteId");
        return int.TryParse(raw, out var id) && id > 0 ? id : null;
    }
}

public sealed class LojaRequest
{
    public string NomeFantasia { get; set; } = string.Empty;
    public string? Cnpj { get; set; }
    public string? Endereco { get; set; }
    public string? Telefone { get; set; }
    public bool Ativo { get; set; } = true;
}
