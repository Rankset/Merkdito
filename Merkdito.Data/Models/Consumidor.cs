namespace Merkdito.Data.Models;

public sealed class Consumidor {
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public int EstabelecimentoId { get; set; }
    public Estabelecimento? Estabelecimento { get; set; }
    public bool Ativo { get; set; } = true;
    public DateTime CriadoEmUtc { get; set; } = DateTime.UtcNow;
    public ICollection<ConsumidorEstabelecimento> LojasVinculadas { get; set; } = new List<ConsumidorEstabelecimento>();
    public ICollection<PedidoConsumidor> Pedidos { get; set; } = new List<PedidoConsumidor>();
}
