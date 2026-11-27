using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Merkdito.Api.Security;
using Merkdito.Data.Context;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);
var connection = builder.Configuration.GetConnectionString("MerkditoConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:MerkditoConnection não configurada.");
var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey) || Encoding.UTF8.GetByteCount(jwtKey) < 32)
    throw new InvalidOperationException("Configure Jwt:Key (mínimo de 32 bytes) em User Secrets ou variável de ambiente.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "Merkdito.Api";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "Merkdito.Web";

builder.Services.AddDbContext<MerkditoDbContext>(opt => opt.UseSqlServer(connection));
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.CustomSchemaIds(type => type.FullName?.Replace("+", "."));

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Cole aqui somente o token JWT. O Swagger adicionará o prefixo Bearer automaticamente."
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
{
    [new OpenApiSecuritySchemeReference("Bearer", document)] = new List<string>()
});
});
builder.Services.AddAuthorization();
builder.Services.AddCors(opt => opt.AddPolicy("WebConhecida", p => {
    var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    if (origins.Length > 0) p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
}));
builder.Services.AddRateLimiter(options => {
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("consumidor-auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "desconhecido",
            _ => new FixedWindowRateLimiterOptions {
                PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
                AutoReplenishment = true
            }));
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer = true, ValidIssuer = jwtIssuer,
            ValidateAudience = true, ValidAudience = jwtAudience,
            ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role
        };
        options.Events = new JwtBearerEvents {
            OnTokenValidated = async context => {
                var principal = context.Principal;
                var role = principal?.FindFirstValue(ClaimTypes.Role);
                var subject = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                if (role is null || !int.TryParse(subject, out var id)) {
                    context.Fail("Identidade inválida."); return;
                }
                var db = context.HttpContext.RequestServices.GetRequiredService<MerkditoDbContext>();
                var tokenService = context.HttpContext.RequestServices.GetRequiredService<JwtTokenService>();
                var stamp = principal!.FindFirstValue(JwtTokenService.PasswordStampClaim);
                bool valid = false;
                switch (role) {
                    case "ADMIN":
                        valid = id == 0 && string.Equals(principal.FindFirstValue(ClaimTypes.Email),
                            builder.Configuration["AdminAuth:Email"], StringComparison.OrdinalIgnoreCase);
                        break;
                    case "CLIENTE": {
                        var account = await db.Clientes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
                        valid = account != null && account.Ativo &&
                            principal.FindFirstValue("ClienteId") == id.ToString() &&
                            tokenService.IsPasswordStampValid(account.PasswordHash, stamp);
                        break;
                    }
                    case "FUNCIONARIO": {
                        var funcionario = await db.UsuariosFuncionarios.AsNoTracking()
                            .Where(x => x.Id == id)
                            .Select(x => new { x.Id, x.ClienteId, x.Ativo, x.PasswordHash, ClienteAtivo = x.Cliente != null && x.Cliente.Ativo })
                            .FirstOrDefaultAsync();
                        valid = funcionario != null && funcionario.Ativo && funcionario.ClienteAtivo &&
                            principal.FindFirstValue("FuncionarioId") == id.ToString() &&
                            principal.FindFirstValue("ClienteId") == funcionario.ClienteId.ToString() &&
                            tokenService.IsPasswordStampValid(funcionario.PasswordHash, stamp);
                        break;
                    }
                    case "CONSUMIDOR": {
                        var consumidor = await db.Consumidores.AsNoTracking()
                            .Where(x => x.Id == id)
                            .Select(x => new { x.Id, x.Ativo, x.PasswordHash })
                            .FirstOrDefaultAsync();
                        valid = consumidor != null && consumidor.Ativo &&
                            principal.FindFirstValue("ConsumidorId") == id.ToString() &&
                            tokenService.IsPasswordStampValid(consumidor.PasswordHash, stamp);
                        break;
                    }
                }
                if (!valid) context.Fail("Sessão revogada ou conta sem acesso.");
            }
        };
    });

var app = builder.Build();
if (app.Environment.IsDevelopment()) {
    app.UseSwagger(); app.UseSwaggerUI();
} else { app.UseHsts(); app.UseHttpsRedirection(); }
app.UseRouting();
app.UseCors("WebConhecida");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.Run();
