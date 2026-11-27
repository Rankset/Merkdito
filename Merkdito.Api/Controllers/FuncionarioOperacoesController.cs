using System.Data;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Merkdito.Data.Context;
using Merkdito.Data.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Merkdito.Api.Controllers;

/// <summary>
/// Operações de fornecedores e compras para funcionários. A permissão é validada no banco
/// em cada requisição, por estabelecimento, e os registros continuam isolados por cliente.
/// Compras sempre são recebidas no estoque central da empresa.
/// </summary>
[ApiController]
[Route("api/funcionario/operacoes")]
[Authorize(Roles = "FUNCIONARIO")]
public sealed class FuncionarioOperacoesController(MerkditoDbContext db) : ControllerBase
{
    public sealed record FornecedorRequest(string Nome, string? Cnpj, string? Email, string? Telefone, string? Endereco, bool Ativo = true);
    public sealed record ItemCompraRequest(int ProdutoId, int Quantidade, decimal CustoUnitario);
    public sealed record CriarCompraRequest(int EstabelecimentoId, int FornecedorId, string? NumeroDocumento,
        DateTime? DataCompraUtc, string? Observacao, List<ItemCompraRequest> Itens);

    [HttpGet("estabelecimentos-fornecedores")]
    public async Task<IActionResult> EstabelecimentosFornecedores(CancellationToken ct)
    {
        if (!TryIdentity(out var clienteId, out var funcionarioId)) return Unauthorized();
        var lojas = await db.UsuariosFuncionariosEstabelecimentos.AsNoTracking()
            .Where(a => a.UsuarioFuncionarioId == funcionarioId && a.PodeGerenciarFornecedores
                && a.UsuarioFuncionario != null && a.UsuarioFuncionario.Ativo && a.UsuarioFuncionario.ClienteId == clienteId
                && a.Estabelecimento != null && a.Estabelecimento.Ativo && a.Estabelecimento.ClienteId == clienteId)
            .OrderBy(a => a.Estabelecimento!.NomeFantasia)
            .Select(a => new { id = a.EstabelecimentoId, nome = a.Estabelecimento!.NomeFantasia })
            .ToListAsync(ct);
        return Ok(lojas);
    }

    [HttpGet("estabelecimentos-compras")]
    public async Task<IActionResult> EstabelecimentosCompras(CancellationToken ct)
    {
        if (!TryIdentity(out var clienteId, out var funcionarioId)) return Unauthorized();
        var lojas = await db.UsuariosFuncionariosEstabelecimentos.AsNoTracking()
            .Where(a => a.UsuarioFuncionarioId == funcionarioId && a.PodeGerenciarCompras
                && a.UsuarioFuncionario != null && a.UsuarioFuncionario.Ativo && a.UsuarioFuncionario.ClienteId == clienteId
                && a.Estabelecimento != null && a.Estabelecimento.Ativo && a.Estabelecimento.ClienteId == clienteId
                && a.Estabelecimento.EhEstoqueCentral)
            .OrderBy(a => a.Estabelecimento!.NomeFantasia)
            .Select(a => new { id = a.EstabelecimentoId, nome = a.Estabelecimento!.NomeFantasia })
            .ToListAsync(ct);
        return Ok(lojas);
    }

    [HttpGet("produtos-compra")]
    public async Task<IActionResult> ProdutosCompra([FromQuery] int estabelecimentoId, CancellationToken ct)
    {
        if (!TryIdentity(out var clienteId, out var funcionarioId)) return Unauthorized();
        if (!await HasPermission(funcionarioId, clienteId, estabelecimentoId, "compra", ct)) return Forbid();
        var produtos = await db.Produtos.AsNoTracking()
            .Where(p => p.Ativo && (p.ClienteId == clienteId ||
                (p.ClienteId == null && p.Estabelecimento != null && p.Estabelecimento.ClienteId == clienteId)))
            .OrderBy(p => p.Nome)
            .Select(p => new { id = p.Id, nome = p.Nome, codigoBarras = p.CodigoBarras })
            .ToListAsync(ct);
        return Ok(produtos);
    }

    [HttpGet("fornecedores")]
    public async Task<IActionResult> ListarFornecedores([FromQuery] int estabelecimentoId, [FromQuery] bool incluirInativos = false, CancellationToken ct = default)
    {
        if (!TryIdentity(out var clienteId, out var funcionarioId)) return Unauthorized();
        if (!await HasPermission(funcionarioId, clienteId, estabelecimentoId, "fornecedor", ct) &&
            !await HasPermission(funcionarioId, clienteId, estabelecimentoId, "compra", ct)) return Forbid();
        var q = db.Fornecedores.AsNoTracking().Where(f => f.ClienteId == clienteId);
        if (!incluirInativos) q = q.Where(f => f.Ativo);
        return Ok(await q.OrderBy(f => f.Nome)
            .Select(f => new { f.Id, f.Nome, f.Cnpj, f.Email, f.Telefone, f.Endereco, f.Ativo, f.CriadoEmUtc })
            .ToListAsync(ct));
    }

    [HttpPost("fornecedores")]
    public async Task<IActionResult> CriarFornecedor([FromQuery] int estabelecimentoId, [FromBody] FornecedorRequest request, CancellationToken ct)
    {
        if (!TryIdentity(out var clienteId, out var funcionarioId)) return Unauthorized();
        if (!await HasPermission(funcionarioId, clienteId, estabelecimentoId, "fornecedor", ct)) return Forbid();
        var erro = ValidateSupplier(request);
        if (erro is not null) return BadRequest(new { mensagem = erro });
        var cnpj = Clean(request.Cnpj);
        if (cnpj is not null && await db.Fornecedores.AnyAsync(f => f.ClienteId == clienteId && f.Cnpj == cnpj, ct))
            return Conflict(new { mensagem = "Já existe um fornecedor com esse CNPJ nesta empresa." });
        var entity = new Fornecedor
        {
            ClienteId = clienteId, Nome = request.Nome.Trim(), Cnpj = cnpj,
            Email = Clean(request.Email), Telefone = Clean(request.Telefone), Endereco = Clean(request.Endereco),
            Ativo = request.Ativo, CriadoEmUtc = DateTime.UtcNow
        };
        db.Fornecedores.Add(entity);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Conflict(new { mensagem = "Não foi possível cadastrar o fornecedor." }); }
        return CreatedAtAction(nameof(ListarFornecedores), new { estabelecimentoId }, new { entity.Id, entity.Nome });
    }

    [HttpPut("fornecedores/{id:int}")]
    public async Task<IActionResult> AtualizarFornecedor(int id, [FromQuery] int estabelecimentoId, [FromBody] FornecedorRequest request, CancellationToken ct)
    {
        if (!TryIdentity(out var clienteId, out var funcionarioId)) return Unauthorized();
        if (!await HasPermission(funcionarioId, clienteId, estabelecimentoId, "fornecedor", ct)) return Forbid();
        var erro = ValidateSupplier(request);
        if (erro is not null) return BadRequest(new { mensagem = erro });
        var entity = await db.Fornecedores.FirstOrDefaultAsync(f => f.Id == id && f.ClienteId == clienteId, ct);
        if (entity is null) return NotFound(new { mensagem = "Fornecedor não encontrado nesta empresa." });
        var cnpj = Clean(request.Cnpj);
        if (cnpj is not null && await db.Fornecedores.AnyAsync(f => f.ClienteId == clienteId && f.Id != id && f.Cnpj == cnpj, ct))
            return Conflict(new { mensagem = "Já existe outro fornecedor com esse CNPJ nesta empresa." });
        entity.Nome = request.Nome.Trim(); entity.Cnpj = cnpj; entity.Email = Clean(request.Email);
        entity.Telefone = Clean(request.Telefone); entity.Endereco = Clean(request.Endereco); entity.Ativo = request.Ativo;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Conflict(new { mensagem = "Não foi possível atualizar o fornecedor." }); }
        return NoContent();
    }

    [HttpDelete("fornecedores/{id:int}")]
    public async Task<IActionResult> InativarFornecedor(int id, [FromQuery] int estabelecimentoId, CancellationToken ct)
    {
        if (!TryIdentity(out var clienteId, out var funcionarioId)) return Unauthorized();
        if (!await HasPermission(funcionarioId, clienteId, estabelecimentoId, "fornecedor", ct)) return Forbid();
        var entity = await db.Fornecedores.FirstOrDefaultAsync(f => f.Id == id && f.ClienteId == clienteId, ct);
        if (entity is null) return NotFound(new { mensagem = "Fornecedor não encontrado nesta empresa." });
        entity.Ativo = false;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("compras")]
    public async Task<IActionResult> ListarCompras([FromQuery] int estabelecimentoId, [FromQuery] string? status = null, [FromQuery] int limite = 200, CancellationToken ct = default)
    {
        if (!TryIdentity(out var clienteId, out var funcionarioId)) return Unauthorized();
        if (!await HasPermission(funcionarioId, clienteId, estabelecimentoId, "compra", ct)) return Forbid();
        var estado = NormalizeStatus(status);
        if (!string.IsNullOrWhiteSpace(status) && estado is null)
            return BadRequest(new { mensagem = "Status inválido. Use PENDENTE, RECEBIDA ou CANCELADA." });
        limite = Math.Clamp(limite, 1, 500);
        var q = db.Compras.AsNoTracking().Where(c => c.ClienteId == clienteId && c.EstabelecimentoId == estabelecimentoId);
        if (estado is not null) q = q.Where(c => c.Status == estado);
        var compras = await q.OrderByDescending(c => c.CriadoEmUtc).Take(limite)
            .Select(c => new { c.Id, c.FornecedorId, fornecedor = c.Fornecedor != null ? c.Fornecedor.Nome : null,
                c.EstabelecimentoId, c.NumeroDocumento, c.DataCompraUtc, c.Status, c.Observacao, c.Total,
                c.CriadoEmUtc, c.RecebidaEmUtc, quantidadeItens = c.Itens.Count })
            .ToListAsync(ct);
        return Ok(compras);
    }

    [HttpGet("compras/{id:int}")]
    public async Task<IActionResult> BuscarCompra(int id, [FromQuery] int estabelecimentoId, CancellationToken ct)
    {
        if (!TryIdentity(out var clienteId, out var funcionarioId)) return Unauthorized();
        if (!await HasPermission(funcionarioId, clienteId, estabelecimentoId, "compra", ct)) return Forbid();
        var compra = await db.Compras.AsNoTracking()
            .Where(c => c.Id == id && c.ClienteId == clienteId && c.EstabelecimentoId == estabelecimentoId)
            .Select(c => new { c.Id, c.FornecedorId, fornecedor = c.Fornecedor != null ? c.Fornecedor.Nome : null,
                c.EstabelecimentoId, c.NumeroDocumento, c.DataCompraUtc, c.Status, c.Observacao, c.Total,
                c.CriadoEmUtc, c.RecebidaEmUtc,
                itens = c.Itens.OrderBy(i => i.Id).Select(i => new { i.ProdutoId, i.NomeProduto, i.CodigoBarrasProduto, i.Quantidade, i.CustoUnitario, i.TotalItem }).ToList() })
            .FirstOrDefaultAsync(ct);
        return compra is null ? NotFound(new { mensagem = "Compra não encontrada neste estoque central." }) : Ok(compra);
    }

    [HttpPost("compras")]
    public async Task<IActionResult> CriarCompra([FromBody] CriarCompraRequest request, CancellationToken ct)
    {
        if (!TryIdentity(out var clienteId, out var funcionarioId)) return Unauthorized();
        if (!await HasPermission(funcionarioId, clienteId, request.EstabelecimentoId, "compra", ct)) return Forbid();
        if (request.FornecedorId <= 0) return BadRequest(new { mensagem = "Selecione um fornecedor válido." });
        if (request.Itens is null || request.Itens.Count is < 1 or > 200)
            return BadRequest(new { mensagem = "A compra deve conter entre 1 e 200 itens." });
        if ((request.NumeroDocumento?.Trim().Length ?? 0) > 80 || (request.Observacao?.Trim().Length ?? 0) > 500)
            return BadRequest(new { mensagem = "Documento (até 80 caracteres) ou observação (até 500 caracteres) excede o limite." });
        if (request.Itens.Any(i => i is null || i.ProdutoId <= 0 || i.Quantidade <= 0 || i.Quantidade > 1_000_000 ||
                                  i.CustoUnitario < 0 || i.CustoUnitario > 999_999_999m))
            return BadRequest(new { mensagem = "Há item com produto, quantidade ou custo inválido." });
        if (request.Itens.GroupBy(i => i.ProdutoId).Any(g => g.Count() > 1))
            return BadRequest(new { mensagem = "Não repita o mesmo produto; some as quantidades em uma única linha." });

        var fornecedor = await db.Fornecedores.FirstOrDefaultAsync(f => f.Id == request.FornecedorId && f.ClienteId == clienteId && f.Ativo, ct);
        if (fornecedor is null) return NotFound(new { mensagem = "Fornecedor ativo não encontrado nesta empresa." });
        var ids = request.Itens.Select(i => i.ProdutoId).ToArray();
        var produtos = await db.Produtos.Where(p => ids.Contains(p.Id) && p.Ativo &&
            (p.ClienteId == clienteId || (p.ClienteId == null && p.Estabelecimento != null && p.Estabelecimento.ClienteId == clienteId)))
            .ToDictionaryAsync(p => p.Id, ct);
        if (produtos.Count != ids.Length) return BadRequest(new { mensagem = "Um ou mais produtos não pertencem ao catálogo ativo desta empresa." });

        decimal total = 0m;
        var itens = new List<ItemCompra>();
        foreach (var item in request.Itens)
        {
            var subtotal = decimal.Round(item.Quantidade * item.CustoUnitario, 2, MidpointRounding.AwayFromZero);
            total += subtotal;
            if (subtotal > 9_999_999_999.99m || total > 9_999_999_999.99m)
                return BadRequest(new { mensagem = "O total da compra excede o limite suportado." });
            var produto = produtos[item.ProdutoId];
            itens.Add(new ItemCompra { ProdutoId = produto.Id, NomeProduto = produto.Nome, CodigoBarrasProduto = produto.CodigoBarras,
                Quantidade = item.Quantidade, CustoUnitario = item.CustoUnitario, TotalItem = subtotal });
        }

        var compra = new Compra
        {
            ClienteId = clienteId, FornecedorId = fornecedor.Id, EstabelecimentoId = request.EstabelecimentoId,
            NumeroDocumento = Clean(request.NumeroDocumento),
            DataCompraUtc = request.DataCompraUtc.HasValue
                ? (request.DataCompraUtc.Value.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(request.DataCompraUtc.Value, DateTimeKind.Utc)
                    : request.DataCompraUtc.Value.ToUniversalTime())
                : DateTime.UtcNow,
            Status = "PENDENTE", Observacao = Clean(request.Observacao), Total = total,
            CriadoEmUtc = DateTime.UtcNow, Itens = itens
        };
        db.Compras.Add(compra);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(BuscarCompra), new { id = compra.Id, estabelecimentoId = compra.EstabelecimentoId },
            new { compra.Id, compra.Status, compra.Total, mensagem = "Compra criada como pendente; o estoque ainda não foi alterado." });
    }

    [HttpPost("compras/{id:int}/receber")]
    public async Task<IActionResult> ReceberCompra(int id, [FromQuery] int estabelecimentoId, CancellationToken ct)
    {
        if (!TryIdentity(out var clienteId, out var funcionarioId)) return Unauthorized();
        if (!await HasPermission(funcionarioId, clienteId, estabelecimentoId, "compra", ct)) return Forbid();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var compra = await db.Compras.Include(c => c.Itens)
            .FirstOrDefaultAsync(c => c.Id == id && c.ClienteId == clienteId && c.EstabelecimentoId == estabelecimentoId, ct);
        if (compra is null) return NotFound(new { mensagem = "Compra não encontrada neste estoque central." });
        if (compra.Status != "PENDENTE") return Conflict(new { mensagem = "Somente compras pendentes podem ser recebidas." });
        var ids = compra.Itens.Select(i => i.ProdutoId).Distinct().ToArray();
        var produtos = await db.Produtos.Where(p => ids.Contains(p.Id) && p.Ativo &&
            (p.ClienteId == clienteId || (p.ClienteId == null && p.Estabelecimento != null && p.Estabelecimento.ClienteId == clienteId)))
            .ToDictionaryAsync(p => p.Id, ct);
        if (produtos.Count != ids.Length) return Conflict(new { mensagem = "Um produto não está mais disponível; nenhum estoque foi alterado." });
        var saldos = await db.ProdutosEstabelecimentos.Where(pe => pe.EstabelecimentoId == estabelecimentoId && ids.Contains(pe.ProdutoId))
            .ToDictionaryAsync(pe => pe.ProdutoId, ct);
        foreach (var item in compra.Itens)
        {
            var produto = produtos[item.ProdutoId];
            if (!saldos.TryGetValue(item.ProdutoId, out var saldo))
            {
                saldo = new ProdutoEstabelecimento { ProdutoId = produto.Id, EstabelecimentoId = estabelecimentoId,
                    PrecoVenda = produto.Preco, QuantidadeEstoque = 0, EstoqueMinimo = Math.Max(0, produto.EstoqueMinimo),
                    Ativo = true, CriadoEmUtc = DateTime.UtcNow };
                db.ProdutosEstabelecimentos.Add(saldo);
                saldos[item.ProdutoId] = saldo;
            }
            if (item.Quantidade > int.MaxValue - saldo.QuantidadeEstoque)
                return BadRequest(new { mensagem = $"A quantidade recebida de '{produto.Nome}' excede o limite; nenhum estoque foi alterado." });
            var anterior = saldo.QuantidadeEstoque;
            saldo.QuantidadeEstoque += item.Quantidade;
            produto.EstabelecimentoId = estabelecimentoId;
            produto.QuantidadeEstoque = saldo.QuantidadeEstoque;
            produto.EstoqueMinimo = saldo.EstoqueMinimo;
            produto.Preco = saldo.PrecoVenda;
            db.MovimentacoesEstoque.Add(new MovimentacaoEstoque { ProdutoId = produto.Id, EstabelecimentoId = estabelecimentoId,
                Tipo = "ENTRADA", Quantidade = item.Quantidade, QuantidadeAnterior = anterior,
                QuantidadePosterior = saldo.QuantidadeEstoque,
                Observacao = $"Recebimento da compra #{compra.Id}", CriadoEmUtc = DateTime.UtcNow });
        }
        compra.Status = "RECEBIDA"; compra.RecebidaEmUtc = DateTime.UtcNow;
        try { await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); }
        catch (DbUpdateException)
        {
            await tx.RollbackAsync(ct);
            return Conflict(new { mensagem = "Não foi possível receber a compra. Atualize e tente novamente." });
        }
        return Ok(new { compra.Id, compra.Status, compra.Total, compra.RecebidaEmUtc, mensagem = "Compra recebida e estoque atualizado." });
    }

    [HttpPost("compras/{id:int}/cancelar")]
    public async Task<IActionResult> CancelarCompra(int id, [FromQuery] int estabelecimentoId, CancellationToken ct)
    {
        if (!TryIdentity(out var clienteId, out var funcionarioId)) return Unauthorized();
        if (!await HasPermission(funcionarioId, clienteId, estabelecimentoId, "compra", ct)) return Forbid();
        var compra = await db.Compras.FirstOrDefaultAsync(c => c.Id == id && c.ClienteId == clienteId && c.EstabelecimentoId == estabelecimentoId, ct);
        if (compra is null) return NotFound(new { mensagem = "Compra não encontrada neste estoque central." });
        if (compra.Status != "PENDENTE") return Conflict(new { mensagem = "Somente compras pendentes podem ser canceladas." });
        compra.Status = "CANCELADA";
        await db.SaveChangesAsync(ct);
        return Ok(new { compra.Id, compra.Status });
    }

    private Task<bool> HasPermission(int funcionarioId, int clienteId, int estabelecimentoId, string permission, CancellationToken ct)
    {
        if (estabelecimentoId <= 0) return Task.FromResult(false);
        var q = db.UsuariosFuncionariosEstabelecimentos.AsNoTracking().Where(a =>
            a.UsuarioFuncionarioId == funcionarioId && a.EstabelecimentoId == estabelecimentoId
            && a.UsuarioFuncionario != null && a.UsuarioFuncionario.Ativo && a.UsuarioFuncionario.ClienteId == clienteId
            && a.Estabelecimento != null && a.Estabelecimento.Ativo && a.Estabelecimento.ClienteId == clienteId);
        return permission switch
        {
            "fornecedor" => q.AnyAsync(a => a.PodeGerenciarFornecedores, ct),
            "compra" => q.AnyAsync(a => a.PodeGerenciarCompras && a.Estabelecimento!.EhEstoqueCentral, ct),
            _ => Task.FromResult(false)
        };
    }

    private bool TryIdentity(out int clienteId, out int funcionarioId)
    {
        clienteId = 0; funcionarioId = 0;
        return int.TryParse(User.FindFirstValue("ClienteId"), out clienteId) && clienteId > 0
            && int.TryParse(User.FindFirstValue("FuncionarioId"), out funcionarioId) && funcionarioId > 0;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? NormalizeStatus(string? value)
    {
        var status = value?.Trim().ToUpperInvariant();
        return status is "PENDENTE" or "RECEBIDA" or "CANCELADA" ? status : null;
    }
    private static string? ValidateSupplier(FornecedorRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Nome) || r.Nome.Trim().Length > 150) return "Informe o nome do fornecedor (até 150 caracteres).";
        if ((r.Cnpj?.Trim().Length ?? 0) > 18) return "O CNPJ deve ter no máximo 18 caracteres.";
        if ((r.Email?.Trim().Length ?? 0) > 254) return "O e-mail deve ter no máximo 254 caracteres.";
        if ((r.Telefone?.Trim().Length ?? 0) > 30) return "O telefone deve ter no máximo 30 caracteres.";
        if ((r.Endereco?.Trim().Length ?? 0) > 250) return "O endereço deve ter no máximo 250 caracteres.";
        if (!string.IsNullOrWhiteSpace(r.Email) && !Regex.IsMatch(r.Email.Trim(), @"^[^\s@]+@[^\s@]+\.[^\s@]+$")) return "O e-mail do fornecedor é inválido.";
        return null;
    }
}
