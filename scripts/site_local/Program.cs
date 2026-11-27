using Microsoft.Extensions.FileProviders;

// Exclusivamente laboratório: serve o Frontend original localmente.
// Nenhum segredo é hospedado aqui; toda autenticação acontece na API.
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
var pasta = Environment.GetEnvironmentVariable("MERKDITO_FRONTEND_PATH")
    ?? Path.Combine(Directory.GetCurrentDirectory(), "Frontend");
pasta = Path.GetFullPath(pasta);
if (!Directory.Exists(pasta) || !File.Exists(Path.Combine(pasta, "index.html")))
    throw new InvalidOperationException($"Frontend original não encontrado: {pasta}");
var arquivos = new PhysicalFileProvider(pasta);
app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = arquivos });
app.UseStaticFiles(new StaticFileOptions { FileProvider = arquivos });
app.Run("http://localhost:5500");
