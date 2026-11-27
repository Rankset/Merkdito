using System.Text.Json.Serialization;

namespace Merkdito.Data.Models;

public class Cliente
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Telefone { get; set; }
    public bool Ativo { get; set; } = true;
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

    // Persistido no banco, nunca exposto em respostas JSON.
    [JsonIgnore]
    public string? PasswordHash { get; set; }

    public ICollection<Estabelecimento> Estabelecimentos { get; set; }
        = new List<Estabelecimento>();

    public ICollection<Produto> Produtos { get; set; } = new List<Produto>();
    public ICollection<Categoria> Categorias { get; set; } = new List<Categoria>();
    public ICollection<Fornecedor> Fornecedores { get; set; } = new List<Fornecedor>();
    public ICollection<Compra> Compras { get; set; } = new List<Compra>();
    public ICollection<UsuarioFuncionario> UsuariosFuncionarios { get; set; } = new List<UsuarioFuncionario>();
}
