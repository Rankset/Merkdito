using System.Security.Claims;
using System.Text.RegularExpressions;
using Merkdito.Api.Security;
using Merkdito.Data.Context;
using Merkdito.Data.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Merkdito.Api.Controllers;

[ApiController]
[Route("api/usuarios-funcionarios")]
[Authorize(Roles = "CLIENTE")]
public sealed class UsuariosFuncionariosController(MerkditoDbContext db) : ControllerBase
{
    private int? ClienteId => int.TryParse(User.FindFirstValue("ClienteId"), out var id) ? id : null;

    public sealed record AcessoRequest(int EstabelecimentoId, bool PodeVerProdutos, bool PodeEditarProdutos,
        bool PodeGerenciarEstoque, bool PodeRegistrarVendas, bool PodeVerRelatorios,
        bool PodeGerenciarCompras, bool PodeGerenciarFornecedores);
    public sealed record CriarUsuarioRequest(string Nome, string Email, string Senha, bool Ativo,
        List<AcessoRequest> Acessos);
    public sealed record AtualizarUsuarioRequest(string Nome, string Email, bool Ativo,
        string? NovaSenha, List<AcessoRequest> Acessos);

    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken ct)
    {
        if (ClienteId is not int clienteId) return Unauthorized();
        var usuarios = await db.UsuariosFuncionarios.AsNoTracking()
            .Where(u => u.ClienteId == clienteId)
            .OrderBy(u => u.Nome)
            .Select(u => new { u.Id, u.Nome, u.Email, u.Ativo, u.CriadoEmUtc,
                acessos = u.Acessos.Select(a => new { a.EstabelecimentoId, estabelecimento = a.Estabelecimento!.NomeFantasia,
                    a.PodeVerProdutos, a.PodeEditarProdutos, a.PodeGerenciarEstoque, a.PodeRegistrarVendas,
                    a.PodeVerRelatorios, a.PodeGerenciarCompras, a.PodeGerenciarFornecedores }) })
            .ToListAsync(ct);
        return Ok(usuarios);
    }

    [HttpGet("estabelecimentos")]
    public async Task<IActionResult> Estabelecimentos(CancellationToken ct)
    {
        if (ClienteId is not int clienteId) return Unauthorized();
        var lojas = await db.Estabelecimentos.AsNoTracking()
            .Where(e => e.ClienteId == clienteId && e.Ativo)
            .OrderBy(e => e.EhEstoqueCentral ? 0 : 1).ThenBy(e => e.NomeFantasia)
            .Select(e => new { e.Id, e.NomeFantasia, e.EhEstoqueCentral })
            .ToListAsync(ct);
        return Ok(lojas);
    }

    [HttpPost]
    public async Task<IActionResult> Criar([FromBody] CriarUsuarioRequest request, CancellationToken ct)
    {
        if (ClienteId is not int clienteId) return Unauthorized();
        var nome = request.Nome?.Trim(); var email = request.Email?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(nome) || nome.Length > 150 || string.IsNullOrWhiteSpace(email) || email.Length > 254)
            return BadRequest(new { mensagem = "Informe nome e e-mail válidos." });
        if (!Regex.IsMatch(email, @"^[^\s@]+@[^\s@]+\.[^\s@]+$")) return BadRequest(new { mensagem = "O e-mail informado é inválido." });
        if (!SenhaValida(request.Senha)) return BadRequest(new { mensagem = "A senha deve ter entre 12 e 128 caracteres e conter maiúscula, minúscula, número e símbolo." });
        if (request.Acessos is null || request.Acessos.Count == 0) return BadRequest(new { mensagem = "Atribua ao menos um estabelecimento ao funcionário." });
        if (await db.UsuariosFuncionarios.AnyAsync(u => u.Email.ToLower() == email, ct) ||
            await db.Clientes.AnyAsync(c => c.Email.ToLower() == email, ct))
            return Conflict(new { mensagem = "Este e-mail já está associado a uma conta do Merkdito. Como o login usa somente e-mail e senha, cada conta precisa ter um e-mail exclusivo." });
        var acessoIds = request.Acessos.Select(a => a.EstabelecimentoId).Distinct().ToArray();
        var estabelecimentosValidos = await db.Estabelecimentos.CountAsync(e => e.ClienteId == clienteId && e.Ativo && acessoIds.Contains(e.Id), ct);
        if (acessoIds.Length != request.Acessos.Count || estabelecimentosValidos != acessoIds.Length)
            return BadRequest(new { mensagem = "Um ou mais estabelecimentos são inválidos, inativos ou pertencem a outro cliente." });

        var usuario = new UsuarioFuncionario { ClienteId = clienteId, Nome = nome, Email = email,
            PasswordHash = PasswordHashing.Hash(request.Senha), Ativo = request.Ativo, CriadoEmUtc = DateTime.UtcNow };
        foreach (var a in request.Acessos) usuario.Acessos.Add(Map(a));
        db.UsuariosFuncionarios.Add(usuario);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Listar), new { id = usuario.Id }, new { usuario.Id, usuario.Nome, usuario.Email, usuario.Ativo });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Atualizar(int id, [FromBody] AtualizarUsuarioRequest request, CancellationToken ct)
    {
        if (ClienteId is not int clienteId) return Unauthorized();
        var usuario = await db.UsuariosFuncionarios.Include(u => u.Acessos)
            .FirstOrDefaultAsync(u => u.Id == id && u.ClienteId == clienteId, ct);
        if (usuario is null) return NotFound(new { mensagem = "Funcionário não encontrado." });
        var nome = request.Nome?.Trim(); var email = request.Email?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(nome) || nome.Length > 150 || string.IsNullOrWhiteSpace(email) || email.Length > 254 ||
            !Regex.IsMatch(email, @"^[^\s@]+@[^\s@]+\.[^\s@]+$")) return BadRequest(new { mensagem = "Informe nome e e-mail válidos." });
        if (await db.UsuariosFuncionarios.AnyAsync(u => u.Id != id && u.Email.ToLower() == email, ct) ||
            await db.Clientes.AnyAsync(c => c.Email.ToLower() == email, ct))
            return Conflict(new { mensagem = "Este e-mail já está associado a outra conta do Merkdito. Cada conta precisa ter um e-mail exclusivo." });
        if (request.Acessos is null || request.Acessos.Count == 0) return BadRequest(new { mensagem = "Atribua ao menos um estabelecimento ao funcionário." });
        if (!string.IsNullOrWhiteSpace(request.NovaSenha) && !SenhaValida(request.NovaSenha))
            return BadRequest(new { mensagem = "A nova senha deve ter entre 12 e 128 caracteres e conter maiúscula, minúscula, número e símbolo." });
        var acessoIds = request.Acessos.Select(a => a.EstabelecimentoId).Distinct().ToArray();
        var validos = await db.Estabelecimentos.CountAsync(e => e.ClienteId == clienteId && e.Ativo && acessoIds.Contains(e.Id), ct);
        if (acessoIds.Length != request.Acessos.Count || validos != acessoIds.Length)
            return BadRequest(new { mensagem = "Um ou mais estabelecimentos são inválidos, inativos ou pertencem a outro cliente." });
        usuario.Nome = nome; usuario.Email = email; usuario.Ativo = request.Ativo; usuario.AtualizadoEmUtc = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(request.NovaSenha)) usuario.PasswordHash = PasswordHashing.Hash(request.NovaSenha);
        db.UsuariosFuncionariosEstabelecimentos.RemoveRange(usuario.Acessos);
        usuario.Acessos.Clear();
        foreach (var a in request.Acessos) usuario.Acessos.Add(Map(a));
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Desativar(int id, CancellationToken ct)
    {
        if (ClienteId is not int clienteId) return Unauthorized();
        var usuario = await db.UsuariosFuncionarios.FirstOrDefaultAsync(u => u.Id == id && u.ClienteId == clienteId, ct);
        if (usuario is null) return NotFound(new { mensagem = "Funcionário não encontrado." });
        usuario.Ativo = false; usuario.AtualizadoEmUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static UsuarioFuncionarioEstabelecimento Map(AcessoRequest a) => new()
    {
        EstabelecimentoId = a.EstabelecimentoId, PodeVerProdutos = a.PodeVerProdutos,
        PodeEditarProdutos = a.PodeEditarProdutos, PodeGerenciarEstoque = a.PodeGerenciarEstoque,
        PodeRegistrarVendas = a.PodeRegistrarVendas, PodeVerRelatorios = a.PodeVerRelatorios,
        PodeGerenciarCompras = a.PodeGerenciarCompras, PodeGerenciarFornecedores = a.PodeGerenciarFornecedores
    };

    private static bool SenhaValida(string? senha) => senha is { Length: >= 12 and <= 128 } &&
        senha.Any(char.IsUpper) && senha.Any(char.IsLower) && senha.Any(char.IsDigit) && senha.Any(c => !char.IsLetterOrDigit(c));
}
