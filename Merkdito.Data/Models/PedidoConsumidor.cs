namespace Merkdito.Data.Models;

public sealed class PedidoConsumidor {
    public int Id { get; set; }
    public int ConsumidorId { get; set; }
    public Consumidor? Consumidor { get; set; }
    public int VendaId { get; set; }
    public Venda? Venda { get; set; }
    public int EstabelecimentoId { get; set; }
    public string ChaveIdempotencia { get; set; } = string.Empty;
    public string ResumoItensSha256 { get; set; } = string.Empty;
    public DateTime CriadoEmUtc { get; set; } = DateTime.UtcNow;
}
