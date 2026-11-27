namespace Merkdito.Data.Models;

/// <summary>Detalhe de venda com snapshot do produto e preço no momento da venda.</summary>
public class ItemVenda
{
    public int Id { get; set; }

    public int VendaId { get; set; }
    public Venda? Venda { get; set; }

    public int ProdutoId { get; set; }
    public Produto? Produto { get; set; }

    public string NomeProduto { get; set; } = string.Empty;
    public string? CodigoBarrasProduto { get; set; }
    public int Quantidade { get; set; }
    public decimal PrecoUnitario { get; set; }
    public decimal TotalItem { get; set; }
}
