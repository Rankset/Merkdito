using System.ComponentModel.DataAnnotations;

namespace Merkdito.Api.Contracts;

public sealed class CadastroCompletoClienteRequest
{
    [Required, StringLength(150, MinimumLength = 2)]
    public string Nome { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = string.Empty;

    [StringLength(30)]
    public string? Telefone { get; set; }

    [Required, StringLength(150, MinimumLength = 2)]
    public string NomeFantasia { get; set; } = string.Empty;

    [Required, StringLength(18, MinimumLength = 14)]
    public string Cnpj { get; set; } = string.Empty;

    [Required, MinLength(12), MaxLength(128)]
    public string SenhaInicial { get; set; } = string.Empty;
}
