using System.Text.Json.Serialization;

namespace Merkdito.Data.Models;

/// <summary>Conta de funcionário pertencente a um cliente do Merkdito.</summary>
public class UsuarioFuncionario
{
    public int Id { get; set; }
    public int ClienteId { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool Ativo { get; set; } = true;
    public DateTime CriadoEmUtc { get; set; } = DateTime.UtcNow;
    public DateTime? AtualizadoEmUtc { get; set; }
    public Cliente? Cliente { get; set; }
    public ICollection<UsuarioFuncionarioEstabelecimento> Acessos { get; set; } = new List<UsuarioFuncionarioEstabelecimento>();
}
