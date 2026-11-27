namespace Merkdito.Data.Models;

/// <summary>Vínculo escolhido pelo consumidor entre uma ou mais empresas e seus PDVs.</summary>
public sealed class ConsumidorEstabelecimento
{
    public int Id { get; set; }
    public int ConsumidorId { get; set; }
    public Consumidor? Consumidor { get; set; }
    public int EstabelecimentoId { get; set; }
    public Estabelecimento? Estabelecimento { get; set; }
    public DateTime CriadoEmUtc { get; set; } = DateTime.UtcNow;
}
