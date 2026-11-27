using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Merkdito.Api.Security;

public sealed class JwtTokenService(IConfiguration configuration)
{
    public const string PasswordStampClaim = "pwdv";

    public (string Token, DateTime ExpiresAtUtc) CreateToken(
        int subjectId,
        string nome,
        string email,
        string perfil,
        int? clienteId = null,
        string? passwordHash = null,
        int? funcionarioId = null,
        int? consumidorId = null, string? consumidorPasswordHash = null, int? lojaId = null)
    {
        var section = configuration.GetSection("Jwt");
        var key = section["Key"];
        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < 32)
            throw new InvalidOperationException("Jwt:Key precisa ter pelo menos 32 bytes.");

        var issuer = section["Issuer"] ?? "Merkdito.Api";
        var audience = section["Audience"] ?? "Merkdito.Web";
        var expires = DateTime.UtcNow.AddHours(4);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subjectId.ToString()),
            new(ClaimTypes.NameIdentifier, subjectId.ToString()),
            new(ClaimTypes.Name, nome),
            new(ClaimTypes.Email, email),
            new(ClaimTypes.Role, perfil)
        };

        if (clienteId.HasValue)
        {
            if (string.IsNullOrWhiteSpace(passwordHash))
                throw new InvalidOperationException(
                    "Não é possível emitir token de cliente sem hash de senha persistido.");
            claims.Add(new Claim("ClienteId", clienteId.Value.ToString()));
            claims.Add(new Claim(PasswordStampClaim, ComputePasswordStamp(passwordHash, key)));
        }

        if (funcionarioId.HasValue)
        {
            if (!clienteId.HasValue || string.IsNullOrWhiteSpace(passwordHash) || funcionarioId.Value <= 0)
                throw new InvalidOperationException("Token de funcionário requer ClienteId, FuncionarioId e hash de senha.");
            claims.Add(new Claim("FuncionarioId", funcionarioId.Value.ToString()));
            claims.Add(new Claim("TipoUsuario", "FUNCIONARIO"));
        }

        if (consumidorId.HasValue)
        {
            if (perfil != "CONSUMIDOR" || consumidorId <= 0 ||
                consumidorId != subjectId ||
                string.IsNullOrWhiteSpace(consumidorPasswordHash))
                throw new InvalidOperationException("Token de consumidor exige ID e hash persistido.");
            claims.Add(new Claim("ConsumidorId", consumidorId.Value.ToString()));
            claims.Add(new Claim(PasswordStampClaim, ComputePasswordStamp(consumidorPasswordHash, key)));
        }
        if (perfil == "CONSUMIDOR" && !consumidorId.HasValue)
            throw new InvalidOperationException("Consumidor sem carimbo de credenciais não pode gerar token.");

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(issuer, audience, claims,
            notBefore: DateTime.UtcNow, expires: expires, signingCredentials: credentials);
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    /// <summary>Compara a marca do token com a derivada do PasswordHash persistido.</summary>
    public bool IsPasswordStampValid(string? passwordHash, string? tokenStamp)
    {
        if (string.IsNullOrWhiteSpace(passwordHash) || string.IsNullOrWhiteSpace(tokenStamp))
            return false;

        var key = configuration.GetSection("Jwt")["Key"];
        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < 32)
            return false;

        var expected = ComputePasswordStamp(passwordHash, key);
        var expectedBytes = Encoding.ASCII.GetBytes(expected);
        var suppliedBytes = Encoding.ASCII.GetBytes(tokenStamp);
        return expectedBytes.Length == suppliedBytes.Length &&
            CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }

    private static string ComputePasswordStamp(string passwordHash, string signingKey)
    {
        var masterKeyBytes = Encoding.UTF8.GetBytes(signingKey);
        // Deriva uma chave por finalidade para não reutilizar diretamente a chave JWT.
        var purposeKey = HMACSHA256.HashData(masterKeyBytes,
            Encoding.UTF8.GetBytes("Merkdito.PasswordStamp.v1"));
        var stampBytes = HMACSHA256.HashData(purposeKey, Encoding.UTF8.GetBytes(passwordHash));
        CryptographicOperations.ZeroMemory(purposeKey);
        return Convert.ToHexString(stampBytes);
    }
}
