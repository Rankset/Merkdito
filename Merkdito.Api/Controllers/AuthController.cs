using Merkdito.Api.Contracts;
using Merkdito.Api.Security;
using Merkdito.Data.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Merkdito.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthController(
    MerkditoDbContext context,
    IConfiguration configuration,
    JwtTokenService tokens) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var email = request.Email.Trim();
        var adminEmail = configuration["AdminAuth:Email"]?.Trim();
        var adminHash = configuration["AdminAuth:PasswordHash"];

        if (!string.IsNullOrWhiteSpace(adminEmail) &&
            string.Equals(email, adminEmail, StringComparison.OrdinalIgnoreCase))
        {
            if (!PasswordHashing.Verify(request.Senha, adminHash))
                return Unauthorized(new { mensagem = "Credenciais inválidas." });

            var adminToken = tokens.CreateToken(0, "Administrador", adminEmail, "ADMIN");
            return Ok(new
            {
                token = adminToken.Token,
                nome = "Administrador",
                email = adminEmail,
                perfil = "ADMIN",
                expiresAtUtc = adminToken.ExpiresAtUtc
            });
        }

        // O login usa apenas e-mail e senha, sem seletor de empresa. Portanto, se o
        // mesmo e-mail estiver associado a mais de uma conta, não escolhemos uma conta
        // arbitrariamente: isso evita autenticar no tenant errado.
        var funcionarios = await context.UsuariosFuncionarios.AsNoTracking()
            .Where(u => u.Email.ToLower() == email.ToLower())
            .Select(u => new
            {
                u.Id, u.ClienteId, u.Nome, u.Email, u.PasswordHash, u.Ativo,
                ClienteAtivo = u.Cliente != null && u.Cliente.Ativo
            })
            .Take(2)
            .ToListAsync();

        var clientesPorEmail = await context.Clientes.AsNoTracking()
            .Where(c => c.Email.ToLower() == email.ToLower())
            .Select(c => new { c.Id, c.Nome, c.Email, c.PasswordHash, c.Ativo })
            .Take(2)
            .ToListAsync();

        if (funcionarios.Count > 1 || clientesPorEmail.Count > 1 ||
            (funcionarios.Count == 1 && clientesPorEmail.Count == 1))
            return Unauthorized(new { mensagem = "Credenciais inválidas. O e-mail está associado a mais de uma conta; contate o administrador." });

        if (funcionarios.Count == 1)
        {
            var funcionario = funcionarios[0];
            if (!funcionario.Ativo || !funcionario.ClienteAtivo ||
                !PasswordHashing.Verify(request.Senha, funcionario.PasswordHash))
                return Unauthorized(new { mensagem = "Credenciais inválidas." });

            var funcionarioToken = tokens.CreateToken(
                funcionario.Id, funcionario.Nome, funcionario.Email, "FUNCIONARIO",
                funcionario.ClienteId, funcionario.PasswordHash, funcionario.Id);

            return Ok(new
            {
                token = funcionarioToken.Token,
                nome = funcionario.Nome,
                email = funcionario.Email,
                perfil = "FUNCIONARIO",
                clienteId = funcionario.ClienteId,
                funcionarioId = funcionario.Id,
                expiresAtUtc = funcionarioToken.ExpiresAtUtc
            });
        }

        var cliente = clientesPorEmail.FirstOrDefault();

        if (cliente is null || !cliente.Ativo ||
            !PasswordHashing.Verify(request.Senha, cliente.PasswordHash))
            return Unauthorized(new { mensagem = "Credenciais inválidas." });

        var resultado = tokens.CreateToken(
            cliente.Id, cliente.Nome, cliente.Email, "CLIENTE", cliente.Id, cliente.PasswordHash);

        return Ok(new
        {
            token = resultado.Token,
            nome = cliente.Nome,
            email = cliente.Email,
            perfil = "CLIENTE",
            clienteId = cliente.Id,
            expiresAtUtc = resultado.ExpiresAtUtc
        });
    }
}
