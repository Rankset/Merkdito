using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Merkdito.Data.Context;
using Merkdito.Data.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Merkdito.Api.Controllers;

/// <summary>
/// Permite que um cliente autenticado consulte e atualize somente os próprios dados.
/// O ID do cliente sempre é obtido do JWT; nenhum ID recebido do navegador é usado
/// para determinar o proprietário dos dados.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "CLIENTE")]
public sealed class MeuPerfilController(MerkditoDbContext context) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Consultar()
    {
        var clienteId = ObterClienteId();
        if (clienteId is null)
            return Unauthorized(new { mensagem = "Token sem identificação de cliente válida." });

        var cliente = await context.Clientes
            .AsNoTracking()
            .Include(c => c.Estabelecimentos)
            .FirstOrDefaultAsync(c => c.Id == clienteId.Value);

        if (cliente is null || !cliente.Ativo)
            return Unauthorized(new { mensagem = "Cliente não encontrado ou inativo." });

        var estabelecimento = cliente.Estabelecimentos
            .OrderBy(e => e.Id)
            .FirstOrDefault();

        return Ok(new
        {
            cliente.Id,
            cliente.Nome,
            cliente.Email,
            cliente.Telefone,
            cliente.Ativo,
            cliente.CriadoEm,
            Estabelecimento = estabelecimento is null ? null : new
            {
                estabelecimento.Id,
                estabelecimento.NomeFantasia,
                estabelecimento.Cnpj,
                estabelecimento.Ativo,
                estabelecimento.CriadoEm
            }
        });
    }

    [HttpPut]
    public async Task<IActionResult> Atualizar([FromBody] AtualizarMeuPerfilRequest dados)
    {
        var clienteId = ObterClienteId();
        if (clienteId is null)
            return Unauthorized(new { mensagem = "Token sem identificação de cliente válida." });

        var nome = dados.Nome?.Trim();
        var email = dados.Email?.Trim();
        var nomeFantasia = dados.NomeFantasia?.Trim();
        var telefone = dados.Telefone?.Trim();

        if (string.IsNullOrWhiteSpace(nome) || nome.Length > 150)
            return BadRequest(new { mensagem = "Informe um nome válido de até 150 caracteres." });

        if (string.IsNullOrWhiteSpace(email) || email.Length > 254 ||
            !new EmailAddressAttribute().IsValid(email))
            return BadRequest(new { mensagem = "Informe um endereço de e-mail válido." });

        if (nomeFantasia is not null && nomeFantasia.Length > 150)
            return BadRequest(new { mensagem = "O nome fantasia deve ter até 150 caracteres." });

        if (telefone is not null && telefone.Length > 30)
            return BadRequest(new { mensagem = "O telefone deve ter até 30 caracteres." });

        if (string.IsNullOrWhiteSpace(nomeFantasia))
            return BadRequest(new { mensagem = "O nome fantasia é obrigatório." });

        var cliente = await context.Clientes
            .Include(c => c.Estabelecimentos)
            .FirstOrDefaultAsync(c => c.Id == clienteId.Value);

        if (cliente is null || !cliente.Ativo)
            return Unauthorized(new { mensagem = "Cliente não encontrado ou inativo." });

        var emailEmUso = await context.Clientes.AnyAsync(c =>
            c.Id != clienteId.Value && c.Email.ToLower() == email.ToLower());
        if (emailEmUso)
            return Conflict(new { mensagem = "Este e-mail já está sendo utilizado por outra conta." });

        // O modelo atual não define um fluxo de troca de CNPJ. Por segurança, o CNPJ
        // é retornado apenas para consulta e não é aceito neste contrato de atualização.
        var estabelecimentoPrincipal = cliente.Estabelecimentos
            .OrderBy(e => e.Id)
            .FirstOrDefault();

        if (estabelecimentoPrincipal is null)
            return Conflict(new { mensagem = "Não existe estabelecimento vinculado a este cliente." });

        cliente.Nome = nome;
        cliente.Email = email;
        cliente.Telefone = string.IsNullOrWhiteSpace(telefone) ? null : telefone;
        estabelecimentoPrincipal.NomeFantasia = nomeFantasia;

        await context.SaveChangesAsync();
        return NoContent();
    }

    private int? ObterClienteId()
    {
        var valor = User.FindFirstValue("ClienteId");
        return int.TryParse(valor, out var clienteId) && clienteId > 0
            ? clienteId
            : null;
    }
}

public sealed class AtualizarMeuPerfilRequest
{
    public string? Nome { get; set; }
    public string? Email { get; set; }
    public string? Telefone { get; set; }
    public string? NomeFantasia { get; set; }
}
