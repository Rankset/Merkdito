namespace Merkdito.Data.Models;

/// <summary>Permissões de um funcionário em um estabelecimento específico.</summary>
public class UsuarioFuncionarioEstabelecimento
{
    public int Id { get; set; }
    public int UsuarioFuncionarioId { get; set; }
    public int EstabelecimentoId { get; set; }
    public bool PodeVerProdutos { get; set; }
    public bool PodeEditarProdutos { get; set; }
    public bool PodeGerenciarEstoque { get; set; }
    public bool PodeRegistrarVendas { get; set; }
    public bool PodeVerRelatorios { get; set; }
    public bool PodeGerenciarCompras { get; set; }
    public bool PodeGerenciarFornecedores { get; set; }
    public UsuarioFuncionario? UsuarioFuncionario { get; set; }
    public Estabelecimento? Estabelecimento { get; set; }
}
