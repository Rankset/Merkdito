namespace Merkdito.Data.Models;

/// <summary>
/// Relação entre um produto do catálogo do cliente e uma loja/depósito.
/// Guarda o saldo, estoque mínimo, disponibilidade e preço praticado naquela unidade.
/// </summary>
public sealed class ProdutoEstabelecimento
{
    public int Id { get; set; }
    public int ProdutoId { get; set; }
    public Produto? Produto { get; set; }
    public int EstabelecimentoId { get; set; }
    public Estabelecimento? Estabelecimento { get; set; }
    public decimal PrecoVenda { get; set; }
    public int QuantidadeEstoque { get; set; }
    public int EstoqueMinimo { get; set; }
    public bool Ativo { get; set; } = true;
    public DateTime CriadoEmUtc { get; set; } = DateTime.UtcNow;
}
