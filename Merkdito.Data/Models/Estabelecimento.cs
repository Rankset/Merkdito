
namespace Merkdito.Data.Models;

public class Estabelecimento
{
    public int Id { get; set; }

    public int ClienteId { get; set; }

    public string NomeFantasia { get; set; } = string.Empty;

    public string? Cnpj { get; set; }

    public string? Endereco { get; set; }

    public string? Telefone { get; set; }

    /// <summary>Marca o depósito central do cliente. Cada cliente deve ter exatamente um.</summary>
    public bool EhEstoqueCentral { get; set; }

    public bool Ativo { get; set; } = true;

    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

    public Cliente? Cliente { get; set; }

    public ICollection<Categoria> Categorias { get; set; }
        = new List<Categoria>();

    public ICollection<Produto> Produtos { get; set; }
        = new List<Produto>();

    public ICollection<MovimentacaoEstoque> MovimentacoesEstoque { get; set; }
        = new List<MovimentacaoEstoque>();

    public ICollection<Venda> Vendas { get; set; } = new List<Venda>();

    public ICollection<ProdutoEstabelecimento> ProdutosEstabelecimentos { get; set; } = new List<ProdutoEstabelecimento>();
    public ICollection<Compra> Compras { get; set; } = new List<Compra>();
    public ICollection<UsuarioFuncionarioEstabelecimento> AcessosFuncionarios { get; set; } = new List<UsuarioFuncionarioEstabelecimento>();
}
