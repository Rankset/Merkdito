using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Merkdito.Api.Security;
using Merkdito.Data.Context;
using Merkdito.Data.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Merkdito.Api.Controllers;

/// <summary>
/// Contrato separado dos perfis CLIENTE/ADMIN/FUNCIONARIO.
/// A API é a única autoridade para preço, estoque, idempotência e escopo de loja.
/// </summary>
[ApiController]
[Route("api/consumidor")]
public sealed class ConsumidorController(
    MerkditoDbContext db,
    JwtTokenService jwt,
    IConfiguration config,
    ILogger<ConsumidorController> logger) : ControllerBase
{
    [HttpGet("lojas")]
    [AllowAnonymous]
    public async Task<IActionResult> Lojas(CancellationToken ct)
    {
        var lojas = await db.Estabelecimentos.AsNoTracking()
            .Where(x => x.Ativo && !x.EhEstoqueCentral && x.Cliente != null && x.Cliente.Ativo)
            .OrderBy(x => x.Cliente!.Nome).ThenBy(x => x.NomeFantasia)
            .Select(x => new
            {
                id = x.Id,
                empresaId = x.ClienteId,
                nome = x.NomeFantasia,
                empresa = x.Cliente!.Nome
            })
            .ToListAsync(ct);

        return Ok(lojas);
    }

    [HttpPost("cadastro")]
    [AllowAnonymous]
    [EnableRateLimiting("consumidor-auth")]
    public async Task<IActionResult> Cadastrar(
        [FromBody] CadastroConsumidorDto input,
        CancellationToken ct)
    {
        var nome = input.Nome?.Trim() ?? "";
        var email = input.Email?.Trim().ToLowerInvariant() ?? "";

        if (nome.Length < 2 || nome.Length > 150 ||
            email.Length > 254 ||
            !new EmailAddressAttribute().IsValid(email) ||
            string.IsNullOrWhiteSpace(input.Senha) ||
            input.Senha.Length < 10 ||
            input.Senha.Length > 128)
        {
            return BadRequest(new
            {
                mensagem = "Informe nome, e-mail válido e senha de 10 a 128 caracteres."
            });
        }

        var ids = input.EstabelecimentoIds?.Distinct().ToList() ?? [];

        if (ids.Count == 0 || ids.Count > 20 || ids.Any(x => x <= 0))
        {
            return BadRequest(new
            {
                mensagem = "Selecione entre 1 e 20 PDVs válidos."
            });
        }

        var ativos = await db.Estabelecimentos.AsNoTracking()
            .CountAsync(x =>
                ids.Contains(x.Id) &&
                x.Ativo &&
                !x.EhEstoqueCentral &&
                x.Cliente != null &&
                x.Cliente.Ativo, ct);

        if (ativos != ids.Count)
        {
            return BadRequest(new
            {
                mensagem = "Uma ou mais lojas selecionadas estão indisponíveis."
            });
        }

        if (await db.Consumidores.AnyAsync(x => x.Email == email, ct))
        {
            return Conflict(new
            {
                mensagem = "Não foi possível concluir o cadastro com este e-mail."
            });
        }

        var consumidor = new Consumidor
        {
            Nome = nome,
            Email = email,
            PasswordHash = PasswordHashing.Hash(input.Senha),
            EstabelecimentoId = ids[0],
            Ativo = true,
            LojasVinculadas = ids.Select(id => new ConsumidorEstabelecimento
            {
                EstabelecimentoId = id
            }).ToList()
        };

        db.Consumidores.Add(consumidor);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return Conflict(new
            {
                mensagem = "Não foi possível concluir o cadastro com este e-mail."
            });
        }

        return StatusCode(201, GerarSessao(consumidor));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("consumidor-auth")]
    public async Task<IActionResult> Login(
        [FromBody] LoginConsumidorDto input,
        CancellationToken ct)
    {
        var email = input.Email?.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(input.Senha))
        {
            logger.LogWarning("Login de consumidor rejeitado: e-mail ou senha vazios.");
            return Unauthorized(new { mensagem = "Credenciais inválidas." });
        }

        var consumidor = await db.Consumidores
            .AsNoTracking()
            .Include(x => x.LojasVinculadas)
            .FirstOrDefaultAsync(x => x.Email == email && x.Ativo, ct);

        if (consumidor is null)
        {
            logger.LogWarning(
                "Login de consumidor rejeitado: consumidor não encontrado ou inativo.");
            return Unauthorized(new { mensagem = "Credenciais inválidas." });
        }

        if (!PasswordHashing.Verify(input.Senha, consumidor.PasswordHash))
        {
            logger.LogWarning(
                "Login de consumidor rejeitado: senha não confere. ConsumidorId={ConsumidorId}",
                consumidor.Id);
            return Unauthorized(new { mensagem = "Credenciais inválidas." });
        }

        if (consumidor.LojasVinculadas.Count == 0)
        {
            logger.LogWarning(
                "Login de consumidor rejeitado: nenhuma loja vinculada. ConsumidorId={ConsumidorId}",
                consumidor.Id);
            return Unauthorized(new { mensagem = "Credenciais inválidas." });
        }

        logger.LogInformation(
            "Login de consumidor validado. ConsumidorId={ConsumidorId}",
            consumidor.Id);

        return Ok(GerarSessao(consumidor));
    }

    /// <summary>
    /// Redefine a senha de um consumidor por meio de uma sessão administrativa.
    /// A senha nunca é registrada em log nem retornada pela API.
    /// </summary>
    [HttpPut("{consumidorId:int}/senha")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> RedefinirSenha(
        int consumidorId,
        [FromBody] RedefinirSenhaConsumidorDto input,
        CancellationToken ct)
    {
        if (consumidorId <= 0)
            return BadRequest(new { mensagem = "Identificador de consumidor inválido." });

        if (string.IsNullOrWhiteSpace(input.NovaSenha) ||
            input.NovaSenha.Length < 10 ||
            input.NovaSenha.Length > 128)
        {
            return BadRequest(new
            {
                mensagem = "A senha deve possuir entre 10 e 128 caracteres."
            });
        }

        var consumidor = await db.Consumidores
            .FirstOrDefaultAsync(x => x.Id == consumidorId && x.Ativo, ct);

        if (consumidor is null)
        {
            return NotFound(new
            {
                mensagem = "Consumidor não encontrado ou inativo."
            });
        }

        consumidor.PasswordHash = PasswordHashing.Hash(input.NovaSenha);

        await db.SaveChangesAsync(ct);

        return Ok(new
        {
            mensagem = "Senha redefinida com sucesso.",
            consumidorId = consumidor.Id
        });
    }

    [HttpGet("perfil")]
    [Authorize(Roles = "CONSUMIDOR")]
    public async Task<IActionResult> Perfil(CancellationToken ct)
    {
        var consumidor = await UsuarioAtual(ct);
        if (consumidor is null) return Unauthorized();

        var ids = await db.ConsumidorEstabelecimentos.AsNoTracking()
            .Where(x => x.ConsumidorId == consumidor.Id &&
                x.Estabelecimento != null &&
                x.Estabelecimento.Ativo &&
                !x.Estabelecimento.EhEstoqueCentral &&
                x.Estabelecimento.Cliente != null &&
                x.Estabelecimento.Cliente.Ativo)
            .Select(x => x.EstabelecimentoId)
            .ToListAsync(ct);

        return Ok(new
        {
            id = consumidor.Id,
            nome = consumidor.Nome,
            email = consumidor.Email,
            estabelecimentoIds = ids
        });
    }

    [HttpPut("vinculos")]
    [Authorize(Roles = "CONSUMIDOR")]
    public async Task<IActionResult> AtualizarVinculos(
        [FromBody] VinculosConsumidorDto input,
        CancellationToken ct)
    {
        var consumidor = await UsuarioAtual(ct);
        if (consumidor is null) return Unauthorized();

        var ids = input.EstabelecimentoIds?.Distinct().ToArray() ?? [];
        if (ids.Length == 0 || ids.Length > 20 || ids.Any(id => id <= 0))
            return BadRequest(new { mensagem = "Selecione entre 1 e 20 PDVs." });

        var ativos = await db.Estabelecimentos.AsNoTracking().CountAsync(x =>
            ids.Contains(x.Id) && x.Ativo && !x.EhEstoqueCentral &&
            x.Cliente != null && x.Cliente.Ativo, ct);

        if (ativos != ids.Length)
            return BadRequest(new { mensagem = "Há PDVs indisponíveis na seleção." });

        await using var tx = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, ct);

        var existentes = await db.ConsumidorEstabelecimentos
            .Where(x => x.ConsumidorId == consumidor.Id)
            .ToListAsync(ct);

        db.ConsumidorEstabelecimentos.RemoveRange(
            existentes.Where(x => !ids.Contains(x.EstabelecimentoId)));

        var atuais = existentes.Select(x => x.EstabelecimentoId).ToHashSet();

        db.ConsumidorEstabelecimentos.AddRange(
            ids.Where(id => !atuais.Contains(id))
                .Select(id => new ConsumidorEstabelecimento
                {
                    ConsumidorId = consumidor.Id,
                    EstabelecimentoId = id
                }));

        // Mantém a associação inicial da versão 1 alinhada a um PDV autorizado.
        await db.SaveChangesAsync(ct);

        await db.Consumidores.Where(c => c.Id == consumidor.Id)
            .ExecuteUpdateAsync(
                q => q.SetProperty(c => c.EstabelecimentoId, ids[0]), ct);

        await tx.CommitAsync(ct);
        return Ok(new { estabelecimentoIds = ids });
    }

    [HttpGet("lojas/{lojaId:int}/produtos")]
    [Authorize(Roles = "CONSUMIDOR")]
    public async Task<IActionResult> Catalogo(int lojaId, CancellationToken ct)
    {
        var consumidor = await UsuarioAtual(ct);
        if (consumidor is null) return Unauthorized();

        if (!await PodeAcessarLoja(consumidor.Id, lojaId, ct))
            return Forbid();

        if (!await LojaAtiva(lojaId, ct))
            return NotFound(new { mensagem = "Loja indisponível." });

        var itens = await db.ProdutosEstabelecimentos.AsNoTracking()
            .Where(x => x.EstabelecimentoId == lojaId && x.Ativo &&
                x.Produto != null && x.Produto.Ativo &&
                x.Produto.ClienteId == x.Estabelecimento!.ClienteId &&
                x.PrecoVenda >= 0)
            .OrderBy(x => x.Produto!.Nome)
            .Select(x => new
            {
                id = x.ProdutoId,
                nome = x.Produto!.Nome,
                descricao = x.Produto.Descricao,
                categoriaId = x.Produto.CategoriaId,
                categoriaNome = x.Produto.Categoria != null
                    ? x.Produto.Categoria.Nome : "Categoria",
                preco = x.PrecoVenda,
                estoque = x.QuantidadeEstoque,
                temFoto = x.Produto.FotoCaminho != null
            })
            .ToListAsync(ct);

        var catalogo = itens.Select(x => new
        {
            x.id, x.nome, x.descricao, x.categoriaId, x.categoriaNome,
            x.preco, x.estoque,
            fotoUrl = x.temFoto ? $"{Request.Scheme}://{Request.Host}/api/Produtos/{x.id}/foto" : null
        });
        return Ok(catalogo);
    }

    [HttpGet("pedidos")]
    [Authorize(Roles = "CONSUMIDOR")]
    public async Task<IActionResult> Historico(CancellationToken ct)
    {
        var consumidor = await UsuarioAtual(ct);
        if (consumidor is null) return Unauthorized();

        var pedidos = await db.PedidosConsumidores.AsNoTracking()
            .Where(x => x.ConsumidorId == consumidor.Id)
            .OrderByDescending(x => x.CriadoEmUtc)
            .Take(50)
            .Select(x => new
            {
                pedidoId = x.VendaId,
                x.CriadoEmUtc,
                estabelecimentoId = x.EstabelecimentoId,
                total = x.Venda!.Total,
                status = x.Venda.Status,
                itens = x.Venda.Itens.Select(i => new
                {
                    produtoId = i.ProdutoId,
                    nome = i.NomeProduto,
                    quantidade = i.Quantidade,
                    precoUnitario = i.PrecoUnitario
                }).ToList()
            })
            .ToListAsync(ct);

        return Ok(pedidos);
    }

    [HttpPost("pedidos")]
    [Authorize(Roles = "CONSUMIDOR")]
    public async Task<IActionResult> FinalizarVenda(
        [FromBody] CriarPedidoConsumidorDto input,
        CancellationToken ct)
    {
        var consumidor = await UsuarioAtual(ct);
        if (consumidor is null) return Unauthorized();

        if (!await PodeAcessarLoja(consumidor.Id, input.EstabelecimentoId, ct))
            return Forbid();

        // Sem provedor de pagamento, essa operação só é liberada em laboratório.
        // REGISTRADA não significa pagamento aprovado nem retirada liberada.
        if (!config.GetValue<bool>("ComprasAutonomas:PermitirVendaSemPagamento"))
            return StatusCode(503, new
            {
                mensagem = "Finalização indisponível até a configuração do meio de pagamento."
            });

        if (!await LojaAtiva(input.EstabelecimentoId, ct))
            return Conflict(new { mensagem = "Loja indisponível." });

        if (string.IsNullOrWhiteSpace(input.IdempotencyKey) ||
            input.IdempotencyKey.Length > 64 ||
            input.Itens is null || input.Itens.Count < 1 || input.Itens.Count > 40 ||
            input.Itens.Any(i => i.ProdutoId <= 0 || i.Quantidade <= 0 || i.Quantidade > 1000) ||
            input.Itens.GroupBy(i => i.ProdutoId).Any(g => g.Count() > 1))
            return BadRequest(new { mensagem = "Revise os itens e quantidades da compra." });

        var fingerprint = ResumoDoPedido(input);

        // SERIALIZABLE mantém leituras, alterações de saldo e gravação atômicas.
        await using var tx = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, ct);

        var anterior = await db.PedidosConsumidores.AsNoTracking()
            .Where(x => x.ConsumidorId == consumidor.Id &&
                x.ChaveIdempotencia == input.IdempotencyKey)
            .Select(x => new
            {
                x.VendaId,
                x.ResumoItensSha256,
                x.Venda!.Total,
                x.Venda.Status
            })
            .FirstOrDefaultAsync(ct);

        if (anterior is not null)
        {
            if (anterior.ResumoItensSha256 != fingerprint)
                return Conflict(new
                {
                    mensagem = "Esta chave de confirmação pertence a outra composição do carrinho."
                });

            await tx.CommitAsync(ct);
            return Ok(new
            {
                pedidoId = anterior.VendaId,
                total = anterior.Total,
                status = anterior.Status,
                repetido = true
            });
        }

        var ids = input.Itens.Select(x => x.ProdutoId).ToArray();

        var registros = await db.ProdutosEstabelecimentos.Include(x => x.Produto)
            .Where(x => x.EstabelecimentoId == input.EstabelecimentoId &&
                ids.Contains(x.ProdutoId) && x.Ativo)
            .ToDictionaryAsync(x => x.ProdutoId, ct);

        var lojaClienteId = await db.Estabelecimentos.AsNoTracking()
            .Where(x => x.Id == input.EstabelecimentoId)
            .Select(x => x.ClienteId)
            .SingleAsync(ct);

        var venda = new Venda
        {
            EstabelecimentoId = input.EstabelecimentoId,
            IdVendaExterna = $"APP:{consumidor.Id}:{input.IdempotencyKey}",
            DataVendaUtc = DateTime.UtcNow,
            Origem = "APP_CONSUMIDOR",
            Status = "REGISTRADA"
        };

        decimal total = 0;

        foreach (var item in input.Itens)
        {
            if (!registros.TryGetValue(item.ProdutoId, out var estoque) ||
                estoque.Produto is null ||
                !estoque.Produto.Ativo ||
                estoque.Produto.ClienteId != lojaClienteId ||
                estoque.QuantidadeEstoque < item.Quantidade ||
                estoque.PrecoVenda < 0)
                return Conflict(new
                {
                    mensagem = "Produto indisponível ou estoque insuficiente. Atualize o catálogo."
                });

            var preco = decimal.Round(
                estoque.PrecoVenda, 2, MidpointRounding.AwayFromZero);
            var valor = preco * item.Quantidade;

            if (valor > 1_000_000_000m || total + valor > 1_000_000_000m)
                return BadRequest(new
                {
                    mensagem = "Valor total excede o limite permitido."
                });

            var saldoAntes = estoque.QuantidadeEstoque;
            estoque.QuantidadeEstoque -= item.Quantidade;
            total += valor;

            venda.Itens.Add(new ItemVenda
            {
                ProdutoId = item.ProdutoId,
                NomeProduto = estoque.Produto.Nome,
                CodigoBarrasProduto = estoque.Produto.CodigoBarras,
                Quantidade = item.Quantidade,
                PrecoUnitario = preco,
                TotalItem = valor
            });

            db.MovimentacoesEstoque.Add(new MovimentacaoEstoque
            {
                ProdutoId = item.ProdutoId,
                EstabelecimentoId = input.EstabelecimentoId,
                Tipo = "SAIDA",
                Quantidade = item.Quantidade,
                QuantidadeAnterior = saldoAntes,
                QuantidadePosterior = estoque.QuantidadeEstoque,
                Observacao = "Venda registrada pelo aplicativo consumidor (sem pagamento online)",
                CriadoEmUtc = DateTime.UtcNow
            });
        }

        venda.Total = total;
        db.Vendas.Add(venda);

        try
        {
            await db.SaveChangesAsync(ct);

            db.PedidosConsumidores.Add(new PedidoConsumidor
            {
                ConsumidorId = consumidor.Id,
                VendaId = venda.Id,
                EstabelecimentoId = input.EstabelecimentoId,
                ChaveIdempotencia = input.IdempotencyKey,
                ResumoItensSha256 = fingerprint
            });

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            await tx.RollbackAsync(CancellationToken.None);
            return Conflict(new
            {
                mensagem = "A venda não foi concluída devido a um conflito. Consulte seu histórico antes de tentar novamente."
            });
        }

        return StatusCode(201, new
        {
            pedidoId = venda.Id,
            total = venda.Total,
            status = venda.Status,
            repetido = false
        });
    }

    private async Task<Consumidor?> UsuarioAtual(CancellationToken ct)
    {
        if (!User.IsInRole("CONSUMIDOR") ||
            !int.TryParse(User.FindFirstValue("ConsumidorId"), out var id))
            return null;

        return await db.Consumidores.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.Ativo, ct);
    }

    private async Task<bool> PodeAcessarLoja(
        int consumidorId, int lojaId, CancellationToken ct) =>
        await db.ConsumidorEstabelecimentos.AsNoTracking()
            .AnyAsync(x => x.ConsumidorId == consumidorId &&
                x.EstabelecimentoId == lojaId, ct);

    private async Task<bool> LojaAtiva(int id, CancellationToken ct) =>
        id > 0 &&
        await db.Estabelecimentos.AsNoTracking()
            .AnyAsync(x => x.Id == id && x.Ativo &&
                !x.EhEstoqueCentral && x.Cliente != null && x.Cliente.Ativo, ct);

    private object GerarSessao(Consumidor consumidor)
    {
        var token = jwt.CreateToken(
            consumidor.Id,
            consumidor.Nome,
            consumidor.Email,
            "CONSUMIDOR",
            consumidorId: consumidor.Id,
            consumidorPasswordHash: consumidor.PasswordHash);

        return new
        {
            token = token.Token,
            expiresAtUtc = token.ExpiresAtUtc,
            nome = consumidor.Nome,
            email = consumidor.Email,
            estabelecimentoIds = consumidor.LojasVinculadas
                .Select(x => x.EstabelecimentoId).ToArray()
        };
    }

    private static string ResumoDoPedido(CriarPedidoConsumidorDto input)
    {
        var canonical = $"{input.EstabelecimentoId}|" +
            string.Join(";", input.Itens!
                .OrderBy(x => x.ProdutoId)
                .Select(x => $"{x.ProdutoId}:{x.Quantidade}"));

        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}

public sealed record CadastroConsumidorDto(
    string? Nome, string? Email, string? Senha, List<int>? EstabelecimentoIds);

public sealed record VinculosConsumidorDto(List<int>? EstabelecimentoIds);

public sealed record LoginConsumidorDto(string? Email, string? Senha);

public sealed record ItemPedidoConsumidorDto(int ProdutoId, int Quantidade);

public sealed record CriarPedidoConsumidorDto(
    int EstabelecimentoId, string? IdempotencyKey,
    List<ItemPedidoConsumidorDto>? Itens);

public sealed record RedefinirSenhaConsumidorDto(string? NovaSenha);
