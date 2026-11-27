namespace Merkdito.Data.Models;

/// <summary>
/// Registro imutável de alteração de saldo de estoque.
/// Quantidade é a magnitude movimentada; para AJUSTE, é a diferença absoluta.
/// QuantidadeAnterior e QuantidadePosterior preservam o saldo antes/depois.
/// </summary>
public class MovimentacaoEstoque
{
    public int Id { get; set; }

    public int ProdutoId { get; set; }
    public Produto? Produto { get; set; }

    public int EstabelecimentoId { get; set; }
    public Estabelecimento? Estabelecimento { get; set; }

    /// <summary>Valores suportados: ENTRADA, SAIDA, AJUSTE.</summary>
    public string Tipo { get; set; } = "AJUSTE";

    public int Quantidade { get; set; }
    public int QuantidadeAnterior { get; set; }
    public int QuantidadePosterior { get; set; }

    public string? Observacao { get; set; }

    /// <summary>Instante UTC em que a movimentação foi registrada.</summary>
    public DateTime CriadoEmUtc { get; set; } = DateTime.UtcNow;
}
