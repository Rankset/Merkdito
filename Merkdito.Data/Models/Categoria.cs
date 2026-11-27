
namespace Merkdito.Data.Models;

public class Categoria
{
    public int Id { get; set; }

    public string Nome { get; set; } = string.Empty;

    public string? Descricao { get; set; }

    /// <summary>Proprietário do catálogo; a categoria é compartilhada entre as lojas do cliente.</summary>
    public int? ClienteId { get; set; }

    public Cliente? Cliente { get; set; }

    public bool Ativa { get; set; } = true;

    public int? EstabelecimentoId { get; set; }

    public Estabelecimento? Estabelecimento { get; set; }

    public ICollection<Produto> Produtos { get; set; }
        = new List<Produto>();
}
