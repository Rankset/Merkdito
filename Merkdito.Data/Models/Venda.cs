namespace Merkdito.Data.Models;

/// <summary>
/// Venda confirmada sincronizada pelo aplicativo de vendas.
/// IdVendaExterna é a chave idempotente fornecida pelo sistema de origem.
/// </summary>
public class Venda
{
    public int Id { get; set; }

    public int EstabelecimentoId { get; set; }
    public Estabelecimento? Estabelecimento { get; set; }

    public string IdVendaExterna { get; set; } = string.Empty;
    public DateTime DataVendaUtc { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = "CONFIRMADA";
    public string Origem { get; set; } = "APP_VENDAS";
    public DateTime CriadoEmUtc { get; set; } = DateTime.UtcNow;

    public ICollection<ItemVenda> Itens { get; set; } = new List<ItemVenda>();
}
