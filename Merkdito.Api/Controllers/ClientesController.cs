using System.ComponentModel.DataAnnotations;
using System.Data;
using Merkdito.Api.Contracts;
using Merkdito.Api.Security;
using Merkdito.Data.Context;
using Merkdito.Data.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Merkdito.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "ADMIN")]
public sealed class ClientesController(MerkditoDbContext context) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetClientes()
    {
        // Retorna apenas os campos necessários para o painel administrativo.
        // PasswordHash não faz parte deste DTO nem da resposta JSON.
        var clientes = await context.Clientes.AsNoTracking()
            .OrderBy(c => c.Nome)
            .Select(c => new ClienteListaResponse
            {
                Id = c.Id,
                Nome = c.Nome,
                Email = c.Email,
                Telefone = c.Telefone,
                Ativo = c.Ativo,
                CriadoEm = c.CriadoEm,
                Estabelecimento = c.Estabelecimentos
                    .OrderBy(e => e.Id)
                    .Select(e => new EstabelecimentoResumoResponse
                    {
                        Id = e.Id,
                        NomeFantasia = e.NomeFantasia,
                        Cnpj = e.Cnpj,
                        Ativo = e.Ativo
                    })
                    .FirstOrDefault()
            })
            .ToListAsync();

        return Ok(clientes);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetCliente(int id)
    {
        var cliente = await context.Clientes.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new ClienteDetalheResponse
            {
                Id = c.Id,
                Nome = c.Nome,
                Email = c.Email,
                Telefone = c.Telefone,
                Ativo = c.Ativo,
                CriadoEm = c.CriadoEm,
                Estabelecimentos = c.Estabelecimentos
                    .OrderBy(e => e.Id)
                    .Select(e => new EstabelecimentoResumoResponse
                    {
                        Id = e.Id,
                        NomeFantasia = e.NomeFantasia,
                        Cnpj = e.Cnpj,
                        Ativo = e.Ativo
                    })
                    .ToList()
            })
            .FirstOrDefaultAsync();

        return cliente is null
            ? NotFound(new { mensagem = "Cliente não encontrado." })
            : Ok(cliente);
    }

    [HttpPost("cadastro-completo")]
    public async Task<IActionResult> CriarCadastroCompleto(
        [FromBody] CadastroCompletoClienteRequest request)
    {
        if (!SenhaAtendePolitica(request.SenhaInicial))
            return BadRequest(new { mensagem = "A senha inicial deve ter entre 12 e 128 caracteres e conter maiúscula, minúscula, número e símbolo." });

        var email = request.Email.Trim();
        var cnpj = NormalizarCnpj(request.Cnpj);
        if (cnpj.Length != 14 || !CnpjValido(cnpj))
            return BadRequest(new { mensagem = "Informe um CNPJ válido." });

        if (await context.Clientes.AnyAsync(c => c.Email.ToLower() == email.ToLower()) ||
            await context.UsuariosFuncionarios.AnyAsync(u => u.Email.ToLower() == email.ToLower()))
            return Conflict(new { mensagem = "Este e-mail já está associado a uma conta do Merkdito." });

        var cnpjDuplicado = await context.Estabelecimentos
            .Where(e => e.Cnpj != null)
            .AnyAsync(e => e.Cnpj!.Trim().Replace(".", "").Replace("/", "").Replace("-", "").Replace(" ", "").ToUpper() == cnpj);
        if (cnpjDuplicado)
            return Conflict(new { mensagem = "Este CNPJ já está cadastrado." });

        var cliente = new Cliente
        {
            Nome = request.Nome.Trim(), Email = email,
            Telefone = request.Telefone?.Trim(), Ativo = true,
            CriadoEm = DateTime.UtcNow,
            PasswordHash = PasswordHashing.Hash(request.SenhaInicial)
        };
        var nomesCategoriasPadrao = new[]
        {
            "Alimentos e Mercearia", "Bebidas", "Hortifruti", "Açougue e Aves",
            "Peixaria e Frutos do Mar", "Frios e Laticínios", "Padaria e Confeitaria",
            "Congelados", "Doces, Chocolates e Snacks", "Higiene Pessoal",
            "Perfumaria e Cosméticos", "Limpeza", "Utilidades Domésticas",
            "Descartáveis e Embalagens", "Bebê e Infantil", "Pet Shop",
            "Papelaria e Escritório", "Informática", "Eletrônicos e Acessórios",
            "Eletrodomésticos", "Vestuário e Moda", "Calçados e Acessórios",
            "Móveis e Decoração", "Ferramentas e Construção", "Automotivo",
            "Esporte e Lazer", "Brinquedos e Jogos", "Jardinagem e Agro",
            "Saúde e Farmácia", "Outros"
        };

        var estabelecimento = new Estabelecimento
        {
            NomeFantasia = request.NomeFantasia.Trim(), Cnpj = cnpj,
            Ativo = true, EhEstoqueCentral = true, CriadoEm = DateTime.UtcNow, Cliente = cliente,
            Categorias = nomesCategoriasPadrao
                .Select(nome => new Categoria { Nome = nome, Ativa = true, Cliente = cliente })
                .ToList()
        };

        await using var transaction = await context.Database.BeginTransactionAsync();
        context.Clientes.Add(cliente);
        context.Estabelecimentos.Add(estabelecimento);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();

        return CreatedAtAction(nameof(GetCliente), new { id = cliente.Id }, new
        {
            cliente.Id, cliente.Nome, cliente.Email, cliente.Telefone, cliente.Ativo,
            cliente.CriadoEm,
            Estabelecimento = new { estabelecimento.Id, estabelecimento.NomeFantasia, estabelecimento.Cnpj }
        });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> AtualizarCliente(int id, [FromBody] AtualizarClienteRequest dados)
    {
        var cliente = await context.Clientes
            .Include(c => c.Estabelecimentos)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (cliente is null)
            return NotFound(new { mensagem = "Cliente não encontrado." });

        var nome = dados.Nome?.Trim();
        var email = dados.Email?.Trim();
        var nomeFantasia = dados.NomeFantasia?.Trim();
        var cnpj = NormalizarCnpj(dados.Cnpj);

        if (string.IsNullOrWhiteSpace(nome) || string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(nomeFantasia))
            return BadRequest(new { mensagem = "Nome, e-mail e nome fantasia são obrigatórios." });

        if (!CnpjValido(cnpj))
            return BadRequest(new { mensagem = "O CNPJ informado é inválido." });

        if (await context.Clientes.AnyAsync(c => c.Id != id && c.Email.ToLower() == email.ToLower()) ||
            await context.UsuariosFuncionarios.AnyAsync(u => u.Email.ToLower() == email.ToLower()))
            return Conflict(new { mensagem = "Este e-mail já está associado a outra conta do Merkdito." });

        var estabelecimento = cliente.Estabelecimentos
    .OrderBy(e => e.Id)
    .FirstOrDefault();

var cnpjDuplicado = await context.Estabelecimentos
    .Where(e => e.ClienteId != id && e.Cnpj != null)
    .AnyAsync(e =>
        e.Cnpj!.Trim()
            .Replace(".", "")
            .Replace("/", "")
            .Replace("-", "")
            .Replace(" ", "")
            .ToUpper() == cnpj);

if (cnpjDuplicado)
    return Conflict(new
    {
        mensagem = "Este CNPJ já está cadastrado para outra empresa."
    });

        cliente.Nome = nome;
        cliente.Email = email;
        cliente.Telefone = dados.Telefone?.Trim();
        cliente.Ativo = dados.Ativo;

        if (estabelecimento is null)
        {
            estabelecimento = new Estabelecimento
            {
                ClienteId = cliente.Id,
                NomeFantasia = nomeFantasia,
                Cnpj = cnpj,
                Ativo = true,
                CriadoEm = DateTime.UtcNow
            };
            context.Estabelecimentos.Add(estabelecimento);
        }
        else
        {
            estabelecimento.NomeFantasia = nomeFantasia;
            estabelecimento.Cnpj = cnpj;
        }

        await context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> ExcluirCliente(int id)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        var cliente = await context.Clientes
            .Include(c => c.Estabelecimentos)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (cliente is null)
            return NotFound(new { mensagem = "Cliente não encontrado." });

        var estabelecimentoIds = cliente.Estabelecimentos.Select(e => e.Id).ToArray();
        var categoriaIds = estabelecimentoIds.Length == 0
            ? Array.Empty<int>()
            : await context.Categorias
                .Where(c => c.EstabelecimentoId.HasValue && estabelecimentoIds.Contains(c.EstabelecimentoId.Value))
                .Select(c => c.Id)
                .ToArrayAsync();

        var possuiProdutos = await context.Produtos.AnyAsync(p =>
            (p.EstabelecimentoId.HasValue && estabelecimentoIds.Contains(p.EstabelecimentoId.Value)) ||
            categoriaIds.Contains(p.CategoriaId));
        var possuiVendas = estabelecimentoIds.Length > 0 &&
            await context.Vendas.AnyAsync(v => estabelecimentoIds.Contains(v.EstabelecimentoId));
        var possuiMovimentacoes = estabelecimentoIds.Length > 0 &&
            await context.MovimentacoesEstoque.AnyAsync(m => estabelecimentoIds.Contains(m.EstabelecimentoId));

        if (possuiProdutos || possuiVendas || possuiMovimentacoes)
        {
            return Conflict(new
            {
                mensagem = "Este cliente possui produtos, vendas ou movimentações de estoque. Para preservar o histórico operacional, a exclusão permanente não é permitida; use Editar para desativar o acesso."
            });
        }

        if (categoriaIds.Length > 0)
        {
            var categorias = await context.Categorias
                .Where(c => categoriaIds.Contains(c.Id))
                .ToListAsync();
            context.Categorias.RemoveRange(categorias);
        }

        if (cliente.Estabelecimentos.Count > 0)
            context.Estabelecimentos.RemoveRange(cliente.Estabelecimentos);

        context.Clientes.Remove(cliente);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();

        return NoContent();
    }

    private static bool SenhaAtendePolitica(string? senha) =>
        !string.IsNullOrEmpty(senha) &&
        senha.Length >= 12 && senha.Length <= 128 &&
        senha.Any(char.IsUpper) &&
        senha.Any(char.IsLower) &&
        senha.Any(char.IsDigit) &&
        senha.Any(c => !char.IsLetterOrDigit(c));

    // Compatível com o CNPJ numérico tradicional e com o alfanumérico implantado em 2026.
    // No formato alfanumérico, as 12 primeiras posições usam o valor ASCII menos 48;
    // os dois dígitos verificadores continuam numéricos (módulo 11, pesos 2 a 9).
    private static string NormalizarCnpj(string? value) =>
        new((value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static bool CnpjValido(string cnpj)
    {
        cnpj = NormalizarCnpj(cnpj);
        if (cnpj.Length != 14 || cnpj.Distinct().Count() == 1)
            return false;
        if (!cnpj[..12].All(char.IsAsciiLetterOrDigit) ||
            cnpj[12] is < '0' or > '9' || cnpj[13] is < '0' or > '9')
            return false;

        static int Digito(string baseCnpj, int[] pesos)
        {
            var soma = 0;
            for (var i = 0; i < pesos.Length; i++) soma += (baseCnpj[i] - '0') * pesos[i];
            var resto = soma % 11;
            return resto < 2 ? 0 : 11 - resto;
        }
        int[] p1 = [5,4,3,2,9,8,7,6,5,4,3,2];
        int[] p2 = [6,5,4,3,2,9,8,7,6,5,4,3,2];
        return Digito(cnpj[..12], p1) == cnpj[12] - '0' &&
               Digito(cnpj[..13], p2) == cnpj[13] - '0';
    }
}

public sealed class ClienteListaResponse
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Telefone { get; set; }
    public bool Ativo { get; set; }
    public DateTime CriadoEm { get; set; }
    public EstabelecimentoResumoResponse? Estabelecimento { get; set; }
}

public sealed class ClienteDetalheResponse
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Telefone { get; set; }
    public bool Ativo { get; set; }
    public DateTime CriadoEm { get; set; }
    public List<EstabelecimentoResumoResponse> Estabelecimentos { get; set; } = new();
}

public sealed class EstabelecimentoResumoResponse
{
    public int Id { get; set; }
    public string NomeFantasia { get; set; } = string.Empty;
    public string? Cnpj { get; set; }
    public bool Ativo { get; set; }
}

public sealed class AtualizarClienteRequest
{
    [Required, StringLength(150, MinimumLength = 2)]
    public string? Nome { get; set; }

    [Required, EmailAddress, StringLength(254)]
    public string? Email { get; set; }

    [StringLength(30)]
    public string? Telefone { get; set; }

    [Required, StringLength(150, MinimumLength = 2)]
    public string? NomeFantasia { get; set; }

    [Required, StringLength(18, MinimumLength = 14)]
    public string? Cnpj { get; set; }

    public bool Ativo { get; set; } = true;
}
