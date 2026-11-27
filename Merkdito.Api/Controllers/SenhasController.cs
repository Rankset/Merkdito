using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Merkdito.Api.Security;
using Merkdito.Data.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Merkdito.Api.Controllers;

/// <summary>
/// Fluxos de alteração e redefinição de senha. Senhas nunca são retornadas pela API.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class SenhasController(MerkditoDbContext context) : ControllerBase
{
    /// <summary>
    /// Permite ao cliente autenticado alterar a própria senha. O ID do cliente vem do JWT,
    /// nunca do corpo da requisição.
    /// </summary>
    [Authorize(Roles = "CLIENTE")]
    [HttpPost("alterar")]
    public async Task<IActionResult> AlterarSenha([FromBody] AlterarSenhaRequest request)
    {
        var claimClienteId = User.FindFirstValue("ClienteId");
        if (!int.TryParse(claimClienteId, out var clienteId))
            return Unauthorized(new { mensagem = "O token não contém um cliente válido." });

        var cliente = await context.Clientes.FirstOrDefaultAsync(c => c.Id == clienteId);
        if (cliente is null || !cliente.Ativo)
            return Unauthorized(new { mensagem = "Cliente não encontrado ou inativo." });

        if (!PasswordHashing.Verify(request.SenhaAtual, cliente.PasswordHash))
            return BadRequest(new { mensagem = "A senha atual está incorreta." });

        if (PasswordHashing.Verify(request.NovaSenha, cliente.PasswordHash))
            return BadRequest(new { mensagem = "A nova senha deve ser diferente da senha atual." });

        if (!SenhaAtendePolitica(request.NovaSenha))
            return BadRequest(new
            {
                mensagem = "A nova senha deve ter pelo menos 12 caracteres e conter maiúscula, minúscula, número e símbolo."
            });

        cliente.PasswordHash = PasswordHashing.Hash(request.NovaSenha);
        await context.SaveChangesAsync();

        return Ok(new
        {
            mensagem = "Senha alterada com sucesso. Use a nova senha nos próximos logins."
        });
    }

    /// <summary>
    /// Redefine a senha a pedido do administrador. A senha temporária chega na requisição,
    /// é armazenada somente como hash e não é devolvida pela API.
    /// </summary>
    [Authorize(Roles = "ADMIN")]
    [HttpPost("clientes/{clienteId:int}/redefinir")]
    public async Task<IActionResult> RedefinirSenha(
        int clienteId,
        [FromBody] RedefinirSenhaRequest request)
    {
        var cliente = await context.Clientes.FirstOrDefaultAsync(c => c.Id == clienteId);
        if (cliente is null)
            return NotFound(new { mensagem = "Cliente não encontrado." });

        if (!SenhaAtendePolitica(request.SenhaTemporaria))
            return BadRequest(new
            {
                mensagem = "A senha temporária deve ter pelo menos 12 caracteres e conter maiúscula, minúscula, número e símbolo."
            });

        cliente.PasswordHash = PasswordHashing.Hash(request.SenhaTemporaria);
        await context.SaveChangesAsync();

        return Ok(new { mensagem = "Senha redefinida. Entregue a senha temporária ao cliente por um canal seguro." });
    }

    private static bool SenhaAtendePolitica(string senha) =>
        senha.Length >= 12 &&
        senha.Any(char.IsUpper) &&
        senha.Any(char.IsLower) &&
        senha.Any(char.IsDigit) &&
        senha.Any(c => !char.IsLetterOrDigit(c));
}

public sealed class AlterarSenhaRequest
{
    [Required, StringLength(200, MinimumLength = 1)]
    public string SenhaAtual { get; set; } = string.Empty;

    [Required, StringLength(128, MinimumLength = 12)]
    public string NovaSenha { get; set; } = string.Empty;
}

public sealed class RedefinirSenhaRequest
{
    [Required, StringLength(128, MinimumLength = 12)]
    public string SenhaTemporaria { get; set; } = string.Empty;
}
