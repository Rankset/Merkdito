using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Merkdito.Data.Context;
using Merkdito.Data.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace Merkdito.Api.Controllers;

/// <summary>
/// Produtos acessíveis pelo administrador ou pelo cliente dono do estabelecimento.
/// O corpo HTTP usa DTOs e não permite enviar entidades de navegação do EF Core.
/// </summary>
[Authorize(Roles = "ADMIN,CLIENTE")]
[ApiController]
[Route("api/[controller]")]
public sealed class ProdutosController(MerkditoDbContext db, IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        var mostrarEstoqueCentral = User.IsInRole("CLIENTE");
        var produtos = await ProdutosVisiveis().AsNoTracking()
            .OrderBy(p => p.Nome)
            .Select(p => new
            {
                p.Id, p.Nome, p.Descricao, p.CodigoBarras,
                Preco = mostrarEstoqueCentral
                    ? db.ProdutosEstabelecimentos.Where(pe => pe.ProdutoId == p.Id && pe.Estabelecimento != null && pe.Estabelecimento.EhEstoqueCentral).Select(pe => (decimal?)pe.PrecoVenda).FirstOrDefault() ?? p.Preco
                    : p.Preco,
                QuantidadeEstoque = mostrarEstoqueCentral
                    ? db.ProdutosEstabelecimentos.Where(pe => pe.ProdutoId == p.Id && pe.Estabelecimento != null && pe.Estabelecimento.EhEstoqueCentral).Select(pe => (int?)pe.QuantidadeEstoque).FirstOrDefault() ?? p.QuantidadeEstoque
                    : p.QuantidadeEstoque,
                EstoqueMinimo = mostrarEstoqueCentral
                    ? db.ProdutosEstabelecimentos.Where(pe => pe.ProdutoId == p.Id && pe.Estabelecimento != null && pe.Estabelecimento.EhEstoqueCentral).Select(pe => (int?)pe.EstoqueMinimo).FirstOrDefault() ?? p.EstoqueMinimo
                    : p.EstoqueMinimo,
                p.Ativo, p.CategoriaId, p.FotoCaminho,
                Categoria = p.Categoria != null ? p.Categoria.Nome : null,
                p.EstabelecimentoId, p.ClienteId
            })
            .ToListAsync();
        return Ok(produtos);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Buscar(int id)
    {
        var mostrarEstoqueCentral = User.IsInRole("CLIENTE");
        var produto = await ProdutosVisiveis().AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new
            {
                p.Id, p.Nome, p.Descricao, p.CodigoBarras,
                Preco = mostrarEstoqueCentral
                    ? db.ProdutosEstabelecimentos.Where(pe => pe.ProdutoId == p.Id && pe.Estabelecimento != null && pe.Estabelecimento.EhEstoqueCentral).Select(pe => (decimal?)pe.PrecoVenda).FirstOrDefault() ?? p.Preco
                    : p.Preco,
                QuantidadeEstoque = mostrarEstoqueCentral
                    ? db.ProdutosEstabelecimentos.Where(pe => pe.ProdutoId == p.Id && pe.Estabelecimento != null && pe.Estabelecimento.EhEstoqueCentral).Select(pe => (int?)pe.QuantidadeEstoque).FirstOrDefault() ?? p.QuantidadeEstoque
                    : p.QuantidadeEstoque,
                EstoqueMinimo = mostrarEstoqueCentral
                    ? db.ProdutosEstabelecimentos.Where(pe => pe.ProdutoId == p.Id && pe.Estabelecimento != null && pe.Estabelecimento.EhEstoqueCentral).Select(pe => (int?)pe.EstoqueMinimo).FirstOrDefault() ?? p.EstoqueMinimo
                    : p.EstoqueMinimo,
                p.Ativo, p.CategoriaId, p.FotoCaminho,
                Categoria = p.Categoria != null ? p.Categoria.Nome : null,
                p.EstabelecimentoId, p.ClienteId
            })
            .FirstOrDefaultAsync();
        return produto is null ? NotFound() : Ok(produto);
    }

    [HttpPost]
    public async Task<IActionResult> Criar([FromBody] CriarProdutoRequest dados)
    {
        if (dados.Id != 0)
            return BadRequest(new { mensagem = "Não informe o ID ao cadastrar." });
        if (string.IsNullOrWhiteSpace(dados.Nome) || dados.Nome.Trim().Length > 150)
            return BadRequest(new { mensagem = "Informe um nome válido de até 150 caracteres." });
        if (dados.Preco < 0 || dados.QuantidadeEstoque < 0 || dados.EstoqueMinimo < 0)
            return BadRequest(new { mensagem = "Preço e quantidades não podem ser negativos." });

        var estabelecimento = await ResolverEstabelecimentoParaGravacao(dados.EstabelecimentoId);
        if (estabelecimento.Erro is not null)
            return estabelecimento.Erro;

        var clienteId = ObterClienteId();
        var categoria = await db.Categorias.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == dados.CategoriaId && c.Ativa);
        if (categoria is null)
            return BadRequest(new { mensagem = "Categoria inexistente ou inativa." });

        if (User.IsInRole("CLIENTE"))
        {
            if (!clienteId.HasValue || categoria.ClienteId != clienteId.Value)
                return BadRequest(new { mensagem = "A categoria não pertence ao catálogo desta empresa." });
        }
        else if (categoria.EstabelecimentoId != estabelecimento.Id)
        {
            return BadRequest(new { mensagem = "A categoria não pertence ao estabelecimento selecionado." });
        }

        var codigo = NormalizarCodigo(dados.CodigoBarras);
        if (codigo is not null && await db.Produtos.AnyAsync(p => p.CodigoBarras == codigo))
            return Conflict(new { mensagem = "Código de barras já cadastrado. O banco mantém unicidade global para esse campo." });

        var produto = new Produto
        {
            Nome = dados.Nome.Trim(),
            Descricao = NormalizarTexto(dados.Descricao),
            CodigoBarras = codigo,
            Preco = dados.Preco,
            QuantidadeEstoque = dados.QuantidadeEstoque,
            EstoqueMinimo = dados.EstoqueMinimo,
            Ativo = dados.Ativo,
            CategoriaId = dados.CategoriaId,
            ClienteId = clienteId ?? (estabelecimento.Id.HasValue ? await db.Estabelecimentos.Where(e => e.Id == estabelecimento.Id.Value).Select(e => (int?)e.ClienteId).FirstOrDefaultAsync() : null),
            EstabelecimentoId = estabelecimento.Id
        };

        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            db.Produtos.Add(produto);
            await db.SaveChangesAsync();

            if (produto.EstabelecimentoId.HasValue)
            {
                db.ProdutosEstabelecimentos.Add(new ProdutoEstabelecimento
                {
                    ProdutoId = produto.Id,
                    EstabelecimentoId = produto.EstabelecimentoId.Value,
                    PrecoVenda = produto.Preco,
                    QuantidadeEstoque = produto.QuantidadeEstoque,
                    EstoqueMinimo = produto.EstoqueMinimo,
                    Ativo = produto.Ativo,
                    CriadoEmUtc = DateTime.UtcNow
                });
            }

            if (produto.EstabelecimentoId.HasValue && produto.QuantidadeEstoque > 0)
            {
                db.MovimentacoesEstoque.Add(new MovimentacaoEstoque
                {
                    ProdutoId = produto.Id,
                    EstabelecimentoId = produto.EstabelecimentoId.Value,
                    Tipo = "ENTRADA",
                    Quantidade = produto.QuantidadeEstoque,
                    QuantidadeAnterior = 0,
                    QuantidadePosterior = produto.QuantidadeEstoque,
                    Observacao = "Saldo inicial informado no cadastro do produto.",
                    CriadoEmUtc = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }

            await transaction.CommitAsync();
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync();
            // Protege também contra corrida entre a consulta prévia e o índice único do banco.
            return Conflict(new { mensagem = "Não foi possível cadastrar o produto. Verifique o código de barras e os vínculos informados." });
        }

        return CreatedAtAction(nameof(Buscar), new { id = produto.Id }, new { produto.Id, produto.Nome });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Atualizar(int id, [FromBody] AtualizarProdutoRequest dados)
    {
        if (id != dados.Id)
            return BadRequest(new { mensagem = "O ID da URL não corresponde ao produto." });
        if (string.IsNullOrWhiteSpace(dados.Nome) || dados.Nome.Trim().Length > 150 ||
            dados.Preco < 0 || dados.QuantidadeEstoque < 0 || dados.EstoqueMinimo < 0)
            return BadRequest(new { mensagem = "Dados do produto inválidos." });

        var produto = await ProdutosVisiveis().FirstOrDefaultAsync(p => p.Id == id);
        if (produto is null)
            return NotFound(new { mensagem = "Produto não encontrado no catálogo autorizado." });

        // Não modificamos EstabelecimentoId a partir do corpo; o vínculo existente é preservado.
        var clienteId = ObterClienteId();
        var categoria = await db.Categorias.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == dados.CategoriaId && c.Ativa);
        if (categoria is null)
            return BadRequest(new { mensagem = "Categoria inexistente ou inativa." });
        if (User.IsInRole("CLIENTE"))
        {
            if (!clienteId.HasValue || categoria.ClienteId != clienteId.Value)
                return BadRequest(new { mensagem = "A categoria não pertence ao catálogo desta empresa." });
        }
        else if (categoria.EstabelecimentoId != produto.EstabelecimentoId)
        {
            return BadRequest(new { mensagem = "A categoria não pertence ao estabelecimento do produto." });
        }

        var codigo = NormalizarCodigo(dados.CodigoBarras);
        if (codigo is not null && await db.Produtos.AnyAsync(p => p.Id != id && p.CodigoBarras == codigo))
            return Conflict(new { mensagem = "Código de barras já cadastrado. O banco mantém unicidade global para esse campo." });

        var quantidadeAnterior = produto.QuantidadeEstoque;
        produto.Nome = dados.Nome.Trim();
        produto.Descricao = NormalizarTexto(dados.Descricao);
        produto.CodigoBarras = codigo;
        produto.Preco = dados.Preco;
        produto.QuantidadeEstoque = dados.QuantidadeEstoque;
        produto.EstoqueMinimo = dados.EstoqueMinimo;
        produto.Ativo = dados.Ativo;
        produto.CategoriaId = dados.CategoriaId;

        if (produto.EstabelecimentoId.HasValue)
        {
            var estoqueCentral = await db.ProdutosEstabelecimentos.FirstOrDefaultAsync(pe =>
                pe.ProdutoId == produto.Id && pe.EstabelecimentoId == produto.EstabelecimentoId.Value);
            if (estoqueCentral is null)
            {
                estoqueCentral = new ProdutoEstabelecimento
                {
                    ProdutoId = produto.Id,
                    EstabelecimentoId = produto.EstabelecimentoId.Value,
                    CriadoEmUtc = DateTime.UtcNow
                };
                db.ProdutosEstabelecimentos.Add(estoqueCentral);
            }
            estoqueCentral.PrecoVenda = produto.Preco;
            estoqueCentral.QuantidadeEstoque = produto.QuantidadeEstoque;
            estoqueCentral.EstoqueMinimo = produto.EstoqueMinimo;
            estoqueCentral.Ativo = produto.Ativo;
        }

        if (produto.EstabelecimentoId.HasValue && quantidadeAnterior != produto.QuantidadeEstoque)
        {
            db.MovimentacoesEstoque.Add(new MovimentacaoEstoque
            {
                ProdutoId = produto.Id,
                EstabelecimentoId = produto.EstabelecimentoId.Value,
                Tipo = "AJUSTE",
                Quantidade = Math.Abs(produto.QuantidadeEstoque - quantidadeAnterior),
                QuantidadeAnterior = quantidadeAnterior,
                QuantidadePosterior = produto.QuantidadeEstoque,
                Observacao = "Ajuste realizado pela edição do produto.",
                CriadoEmUtc = DateTime.UtcNow
            });
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Conflict(new { mensagem = "Não foi possível salvar o produto. Verifique o código de barras e os vínculos informados." });
        }
        return NoContent();
    }

    /// <summary>Recebe e armazena a foto do produto. Limite de 5 MB; JPEG, PNG ou WEBP.</summary>
    [HttpPost("{id:int}/foto")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> EnviarFoto(int id, IFormFile arquivo, CancellationToken ct)
    {
        var produto = await ProdutosVisiveis().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (produto is null) return NotFound(new { mensagem = "Produto não encontrado." });
        if (arquivo is null || arquivo.Length == 0)
            return BadRequest(new { mensagem = "Selecione uma imagem." });
        if (arquivo.Length > 5 * 1024 * 1024)
            return BadRequest(new { mensagem = "A imagem deve ter no máximo 5 MB." });

        var extensao = Path.GetExtension(arquivo.FileName).ToLowerInvariant();
        var permitido = new[] { ".jpg", ".jpeg", ".png", ".webp" };
        if (!permitido.Contains(extensao))
            return BadRequest(new { mensagem = "Formato inválido. Envie JPG, PNG ou WEBP." });

        await using var input = arquivo.OpenReadStream();
        var assinatura = new byte[12];
        var lidos = await input.ReadAsync(assinatura.AsMemory(0, assinatura.Length), ct);
        var jpeg = lidos >= 3 && assinatura[0] == 0xFF && assinatura[1] == 0xD8 && assinatura[2] == 0xFF;
        var png = lidos >= 8 && assinatura.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var webp = lidos >= 12 && Encoding.ASCII.GetString(assinatura, 0, 4) == "RIFF"
                   && Encoding.ASCII.GetString(assinatura, 8, 4) == "WEBP";
        if (!jpeg && !png && !webp)
            return BadRequest(new { mensagem = "O conteúdo enviado não parece ser uma imagem válida." });

        input.Position = 0;
        var pasta = Path.Combine(environment.ContentRootPath, "uploads", "produtos");
        Directory.CreateDirectory(pasta);
        var nomeArquivo = $"{Guid.NewGuid():N}{(webp ? ".webp" : png ? ".png" : ".jpg")}";
        var destino = Path.Combine(pasta, nomeArquivo);
        await using (var output = System.IO.File.Create(destino))
            await input.CopyToAsync(output, ct);

        var caminhoAntigo = produto.FotoCaminho;
        produto.FotoCaminho = nomeArquivo;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            System.IO.File.Delete(destino);
            throw;
        }

        ExcluirArquivoFoto(caminhoAntigo);
        return Ok(new { fotoUrl = Url.ActionLink(nameof(ObterFoto), values: new { id = produto.Id }) });
    }

    /// <summary>Entrega a imagem do produto ao aplicativo e ao site.</summary>
    [HttpGet("{id:int}/foto")]
    [AllowAnonymous]
    public async Task<IActionResult> ObterFoto(int id, CancellationToken ct)
    {
        var caminho = await db.Produtos.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => p.FotoCaminho)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(caminho))
            return NotFound();
        var nome = Path.GetFileName(caminho);
        var arquivo = Path.Combine(environment.ContentRootPath, "uploads", "produtos", nome);
        if (!System.IO.File.Exists(arquivo)) return NotFound();
        var tipo = Path.GetExtension(nome).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };
        return PhysicalFile(arquivo, tipo, enableRangeProcessing: true);
    }

    /// <summary>Remove a foto atual do produto.</summary>
    [HttpDelete("{id:int}/foto")]
    public async Task<IActionResult> RemoverFoto(int id, CancellationToken ct)
    {
        var produto = await ProdutosVisiveis().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (produto is null) return NotFound(new { mensagem = "Produto não encontrado." });
        var anterior = produto.FotoCaminho;
        if (string.IsNullOrWhiteSpace(anterior)) return NoContent();
        produto.FotoCaminho = null;
        await db.SaveChangesAsync(ct);
        ExcluirArquivoFoto(anterior);
        return NoContent();
    }

    private void ExcluirArquivoFoto(string? nome)
    {
        if (string.IsNullOrWhiteSpace(nome)) return;
        var seguro = Path.GetFileName(nome);
        var caminho = Path.Combine(environment.ContentRootPath, "uploads", "produtos", seguro);
        if (System.IO.File.Exists(caminho)) System.IO.File.Delete(caminho);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Excluir(int id)
    {
        var produto = await ProdutosVisiveis().FirstOrDefaultAsync(p => p.Id == id);
        if (produto is null)
            return NotFound(new { mensagem = "Produto não encontrado no catálogo autorizado." });
        if (await db.MovimentacoesEstoque.AnyAsync(m => m.ProdutoId == id) ||
            await db.ItensVenda.AnyAsync(i => i.ProdutoId == id))
            return Conflict(new { mensagem = "Este produto possui histórico de estoque ou vendas. Desative-o em vez de excluí-lo." });
        if (await db.ProdutosEstabelecimentos.AnyAsync(pe => pe.ProdutoId == id && pe.QuantidadeEstoque != 0))
            return Conflict(new { mensagem = "Este produto possui saldo em alguma loja. Transfira ou ajuste o estoque antes de excluí-lo." });
        var vinculos = await db.ProdutosEstabelecimentos.Where(pe => pe.ProdutoId == id).ToListAsync();
        db.ProdutosEstabelecimentos.RemoveRange(vinculos);
        db.Produtos.Remove(produto);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private IQueryable<Produto> ProdutosVisiveis()
    {
        var consulta = db.Produtos.AsQueryable();
        if (User.IsInRole("ADMIN"))
            return consulta;

        var clienteId = ObterClienteId();
        if (!clienteId.HasValue)
            return consulta.Where(_ => false);

        return consulta.Where(p =>
            p.ClienteId == clienteId.Value ||
            (p.ClienteId == null && p.Estabelecimento != null &&
             p.Estabelecimento.Ativo && p.Estabelecimento.ClienteId == clienteId.Value &&
             p.Estabelecimento.Cliente != null && p.Estabelecimento.Cliente.Ativo));
    }

    private async Task<(int? Id, IActionResult? Erro)> ResolverEstabelecimentoParaGravacao(int? idRecebido)
    {
        if (User.IsInRole("ADMIN"))
        {
            if (!idRecebido.HasValue)
                return (null, null); // Mantém suporte a registros globais antigos do administrador.
            var existe = await db.Estabelecimentos.AnyAsync(e => e.Id == idRecebido.Value);
            return existe ? (idRecebido.Value, null) : (null, BadRequest("Estabelecimento inexistente."));
        }

        var clienteId = ObterClienteId();
        if (!clienteId.HasValue)
            return (null, Unauthorized(new { mensagem = "O token não contém ClienteId válido." }));

        var id = await db.Estabelecimentos
            .Where(e => e.ClienteId == clienteId.Value && e.Ativo && e.EhEstoqueCentral && e.Cliente != null && e.Cliente.Ativo)
            .Select(e => (int?)e.Id)
            .FirstOrDefaultAsync();
        return id.HasValue
            ? (id.Value, null)
            : (null, BadRequest(new { mensagem = "O cliente não possui estabelecimento ativo." }));
    }

    private int? ObterClienteId()
    {
        var raw = User.FindFirstValue("ClienteId");
        return int.TryParse(raw, out var id) && id > 0 ? id : null;
    }

    private static string? NormalizarCodigo(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizarTexto(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

// DTOs impedem o binding direto de Categoria/Estabelecimento/Produtos de navegação do EF.
public class CriarProdutoRequest
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string? Descricao { get; set; }
    public string? CodigoBarras { get; set; }
    public decimal Preco { get; set; }
    public int QuantidadeEstoque { get; set; }
    public int EstoqueMinimo { get; set; }
    public bool Ativo { get; set; } = true;
    public int CategoriaId { get; set; }
    public int? EstabelecimentoId { get; set; }
}

public sealed class AtualizarProdutoRequest : CriarProdutoRequest { }
