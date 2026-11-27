namespace Merkdito.Data.Models;

/// <summary>Pedido de compra do cliente. O recebimento é aplicado ao estoque central uma única vez.</summary>
public sealed class Compra
{
    public int Id { get; set; }
    public int ClienteId { get; set; }
    public Cliente? Cliente { get; set; }
    public int FornecedorId { get; set; }
    public Fornecedor? Fornecedor { get; set; }
    public int EstabelecimentoId { get; set; }
    public Estabelecimento? Estabelecimento { get; set; }
    public string? NumeroDocumento { get; set; }
    public DateTime DataCompraUtc { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "PENDENTE";
    public string? Observacao { get; set; }
    public decimal Total { get; set; }
    public DateTime CriadoEmUtc { get; set; } = DateTime.UtcNow;
    public DateTime? RecebidaEmUtc { get; set; }
    public ICollection<ItemCompra> Itens { get; set; } = new List<ItemCompra>();
}
