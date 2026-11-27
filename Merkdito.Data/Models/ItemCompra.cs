namespace Merkdito.Data.Models;

/// <summary>Item da compra com snapshot do nome e custo acordado.</summary>
public sealed class ItemCompra
{
    public int Id { get; set; }
    public int CompraId { get; set; }
    public Compra? Compra { get; set; }
    public int ProdutoId { get; set; }
    public Produto? Produto { get; set; }
    public string NomeProduto { get; set; } = string.Empty;
    public string? CodigoBarrasProduto { get; set; }
    public int Quantidade { get; set; }
    public decimal CustoUnitario { get; set; }
    public decimal TotalItem { get; set; }
}
