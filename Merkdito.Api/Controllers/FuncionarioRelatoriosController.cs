using System.Security.Claims;
using Merkdito.Data.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Merkdito.Api.Controllers;

/// <summary>Relatórios de vendas limitados ao cliente e ao estabelecimento autorizados.</summary>
[ApiController]
[Route("api/funcionario/relatorios")]
[Authorize(Roles = "FUNCIONARIO")]
public sealed class FuncionarioRelatoriosController(MerkditoDbContext db) : ControllerBase
{
[HttpGet("estabelecimentos")]
public async Task<IActionResult> Estabelecimentos(CancellationToken ct)
{
    if (!TryGetIdentity(out var clienteId, out var funcionarioId)) return Unauthorized();

    var estabelecimentos = await db.UsuariosFuncionariosEstabelecimentos.AsNoTracking()
        .Where(a => a.UsuarioFuncionarioId == funcionarioId
            && a.PodeVerRelatorios
            && a.UsuarioFuncionario != null
            && a.UsuarioFuncionario.Ativo
            && a.UsuarioFuncionario.ClienteId == clienteId
            && a.Estabelecimento != null
            && a.Estabelecimento.Ativo
            && a.Estabelecimento.ClienteId == clienteId
            && !a.Estabelecimento.EhEstoqueCentral)
        .OrderBy(a => a.Estabelecimento!.NomeFantasia)
        .Select(a => new
        {
            id = a.EstabelecimentoId,
            nome = a.Estabelecimento!.NomeFantasia,
            ehEstoqueCentral = a.Estabelecimento.EhEstoqueCentral
        })
        .ToListAsync(ct);

    return Ok(estabelecimentos);
}

    [HttpGet("vendas")]
    public async Task<IActionResult> ListarVendas(
        [FromQuery] int estabelecimentoId,
        [FromQuery] DateTime? de,
        [FromQuery] DateTime? ate,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamanhoPagina = 50,
        CancellationToken ct = default)
    {
        if (!TryGetIdentity(out var clienteId, out var funcionarioId)) return Unauthorized();
        if (estabelecimentoId <= 0) return BadRequest(new { mensagem = "Informe um estabelecimento válido." });
        if (!await TemPermissaoRelatorios(funcionarioId, clienteId, estabelecimentoId, ct)) return Forbid();

        var inicio = de?.Date;
        var fimExclusivo = ate?.Date.AddDays(1);
        if (inicio.HasValue && fimExclusivo.HasValue && fimExclusivo <= inicio)
            return BadRequest(new { mensagem = "O período final deve ser igual ou posterior ao período inicial." });
        if (inicio.HasValue && fimExclusivo.HasValue && (fimExclusivo.Value - inicio.Value).TotalDays > 366)
            return BadRequest(new { mensagem = "O período máximo permitido é de 366 dias." });

        pagina = Math.Max(1, pagina);
        tamanhoPagina = Math.Clamp(tamanhoPagina, 1, 100);

        var consulta = db.Vendas.AsNoTracking().Where(v =>
            v.EstabelecimentoId == estabelecimentoId &&
            v.Estabelecimento != null && v.Estabelecimento.ClienteId == clienteId &&
            v.Estabelecimento.Ativo);
        if (inicio.HasValue) consulta = consulta.Where(v => v.DataVendaUtc >= inicio.Value);
        if (fimExclusivo.HasValue) consulta = consulta.Where(v => v.DataVendaUtc < fimExclusivo.Value);

        var totalRegistros = await consulta.CountAsync(ct);
        var vendas = await consulta.OrderByDescending(v => v.DataVendaUtc).ThenByDescending(v => v.Id)
            .Skip((pagina - 1) * tamanhoPagina).Take(tamanhoPagina)
            .Select(v => new
            {
                v.Id,
                v.IdVendaExterna,
                v.DataVendaUtc,
                v.Total,
                v.Status,
                v.Origem,
                v.EstabelecimentoId,
                quantidadeItens = v.Itens.Count(),
                totalUnidades = v.Itens.Sum(i => (int?)i.Quantidade) ?? 0
            }).ToListAsync(ct);

        return Ok(new { pagina, tamanhoPagina, totalRegistros, vendas });
    }

    [HttpGet("vendas/resumo")]
    public async Task<IActionResult> ResumoVendas(
        [FromQuery] int estabelecimentoId,
        [FromQuery] DateTime? de,
        [FromQuery] DateTime? ate,
        CancellationToken ct = default)
    {
        if (!TryGetIdentity(out var clienteId, out var funcionarioId)) return Unauthorized();
        if (estabelecimentoId <= 0) return BadRequest(new { mensagem = "Informe um estabelecimento válido." });
        if (!await TemPermissaoRelatorios(funcionarioId, clienteId, estabelecimentoId, ct)) return Forbid();

        var inicio = de?.Date;
        var fimExclusivo = ate?.Date.AddDays(1);
        if (inicio.HasValue && fimExclusivo.HasValue && fimExclusivo <= inicio)
            return BadRequest(new { mensagem = "O período final deve ser igual ou posterior ao período inicial." });
        if (inicio.HasValue && fimExclusivo.HasValue && (fimExclusivo.Value - inicio.Value).TotalDays > 366)
            return BadRequest(new { mensagem = "O período máximo permitido é de 366 dias." });

        var consulta = db.Vendas.AsNoTracking().Where(v =>
            v.EstabelecimentoId == estabelecimentoId &&
            v.Estabelecimento != null && v.Estabelecimento.ClienteId == clienteId &&
            v.Estabelecimento.Ativo && v.Status == "CONFIRMADA");
        if (inicio.HasValue) consulta = consulta.Where(v => v.DataVendaUtc >= inicio.Value);
        if (fimExclusivo.HasValue) consulta = consulta.Where(v => v.DataVendaUtc < fimExclusivo.Value);

        var resumo = await consulta.GroupBy(_ => 1).Select(g => new
        {
            quantidadeVendas = g.Count(),
            faturamentoTotal = g.Sum(v => v.Total),
            ticketMedio = g.Count() == 0 ? 0m : g.Sum(v => v.Total) / g.Count()
        }).FirstOrDefaultAsync(ct);

        var porDia = await consulta.GroupBy(v => v.DataVendaUtc.Date)
            .Select(g => new { dataUtc = g.Key, quantidadeVendas = g.Count(), faturamento = g.Sum(v => v.Total) })
            .OrderBy(x => x.dataUtc).ToListAsync(ct);

        return Ok(new
        {
            estabelecimentoId,
            periodoDeUtc = inicio,
            periodoAteUtc = ate?.Date,
            quantidadeVendas = resumo?.quantidadeVendas ?? 0,
            faturamentoTotal = resumo?.faturamentoTotal ?? 0m,
            ticketMedio = resumo?.ticketMedio ?? 0m,
            porDia
        });
    }

    [HttpGet("vendas/{id:int}")]
    public async Task<IActionResult> DetalheVenda(int id, [FromQuery] int estabelecimentoId, CancellationToken ct)
    {
        if (!TryGetIdentity(out var clienteId, out var funcionarioId)) return Unauthorized();
        if (id <= 0 || estabelecimentoId <= 0) return BadRequest(new { mensagem = "Venda e estabelecimento são obrigatórios." });
        if (!await TemPermissaoRelatorios(funcionarioId, clienteId, estabelecimentoId, ct)) return Forbid();

        var venda = await db.Vendas.AsNoTracking()
            .Where(v => v.Id == id && v.EstabelecimentoId == estabelecimentoId
                && v.Estabelecimento != null && v.Estabelecimento.ClienteId == clienteId
                && v.Estabelecimento.Ativo)
            .Select(v => new
            {
                v.Id, v.IdVendaExterna, v.DataVendaUtc, v.Total, v.Status, v.Origem, v.EstabelecimentoId,
                itens = v.Itens.Select(i => new
                {
                    i.ProdutoId, i.NomeProduto, i.CodigoBarrasProduto, i.Quantidade,
                    i.PrecoUnitario, i.TotalItem
                }).ToList()
            }).FirstOrDefaultAsync(ct);

        return venda is null ? NotFound(new { mensagem = "Venda não encontrada neste estabelecimento." }) : Ok(venda);
    }

    private Task<bool> TemPermissaoRelatorios(int funcionarioId, int clienteId, int estabelecimentoId, CancellationToken ct) =>
        db.UsuariosFuncionariosEstabelecimentos.AsNoTracking().AnyAsync(a =>
            a.UsuarioFuncionarioId == funcionarioId && a.EstabelecimentoId == estabelecimentoId &&
            a.PodeVerRelatorios && a.UsuarioFuncionario != null && a.UsuarioFuncionario.Ativo &&
            a.UsuarioFuncionario.ClienteId == clienteId && a.Estabelecimento != null &&
            a.Estabelecimento.Ativo && a.Estabelecimento.ClienteId == clienteId, ct);

    private bool TryGetIdentity(out int clienteId, out int funcionarioId)
    {
        clienteId = 0;
        funcionarioId = 0;
        return int.TryParse(User.FindFirstValue("ClienteId"), out clienteId) && clienteId > 0 &&
            int.TryParse(User.FindFirstValue("FuncionarioId"), out funcionarioId) && funcionarioId > 0;
    }
}
