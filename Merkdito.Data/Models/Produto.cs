
using System.ComponentModel.DataAnnotations.Schema;

namespace Merkdito.Data.Models;

public class Produto
{
    public int Id { get; set; }

    public string Nome { get; set; } = string.Empty;

    public string? Descricao { get; set; }

    public string? CodigoBarras { get; set; }

    /// <summary>Caminho relativo da foto armazenada pela API.</summary>
    public string? FotoCaminho { get; set; }

    /// <summary>Proprietário do catálogo; o estoque e preço por loja ficam em ProdutoEstabelecimento.</summary>
    public int? ClienteId { get; set; }

    public Cliente? Cliente { get; set; }

    public decimal Preco { get; set; }

    public int QuantidadeEstoque { get; set; }

    public int EstoqueMinimo { get; set; }

    public bool Ativo { get; set; } = true;

    public int CategoriaId { get; set; }

    [ForeignKey(nameof(CategoriaId))]
    public Categoria? Categoria { get; set; }

    public int? EstabelecimentoId { get; set; }

    public Estabelecimento? Estabelecimento { get; set; }

    public ICollection<MovimentacaoEstoque> MovimentacoesEstoque { get; set; }
        = new List<MovimentacaoEstoque>();

    public ICollection<ItemVenda> ItensVenda { get; set; } = new List<ItemVenda>();

    public ICollection<ProdutoEstabelecimento> ProdutosEstabelecimentos { get; set; } = new List<ProdutoEstabelecimento>();
    public ICollection<ItemCompra> ItensCompra { get; set; } = new List<ItemCompra>();
}
