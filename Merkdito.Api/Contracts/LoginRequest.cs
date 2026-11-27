using System.ComponentModel.DataAnnotations;

namespace Merkdito.Api.Contracts;

public sealed class LoginRequest
{
    [Required, EmailAddress, MaxLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(1), MaxLength(200)]
    public string Senha { get; set; } = string.Empty;
}
