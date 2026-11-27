using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Merkdito.Web.Services;

public sealed class AuthService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly MerkditoAuthenticationStateProvider _authenticationStateProvider;

    public AuthService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        MerkditoAuthenticationStateProvider authenticationStateProvider)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _authenticationStateProvider = authenticationStateProvider;
    }

    public string? Token { get; private set; }
    public string? Nome { get; private set; }
    public string? Email { get; private set; }
    public string? Perfil { get; private set; }
    public bool EstaAutenticado => !string.IsNullOrWhiteSpace(Token);

    public async Task<(bool Sucesso, string Mensagem)> LoginAsync(string email, string senha)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(senha))
            return (false, "Informe o e-mail e a senha.");

        var apiBaseUrl = _configuration["ApiBaseUrl"];
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
            return (false, "A URL da API não está configurada.");

        try
        {
            var client = _httpClientFactory.CreateClient();
            var url = $"{apiBaseUrl.TrimEnd('/')}/api/Auth/login";
            var resposta = await client.PostAsJsonAsync(url, new { Email = email.Trim(), Senha = senha });

            if (!resposta.IsSuccessStatusCode)
            {
                LimparSessao();
                return (false, "Não foi possível entrar. Verifique o e-mail e a senha.");
            }

            var conteudo = await resposta.Content.ReadAsStringAsync();
            using var documento = JsonDocument.Parse(conteudo);
            var dados = documento.RootElement;

            Token = LerTexto(dados, "token");
            Nome = LerTexto(dados, "nome");
            Email = LerTexto(dados, "email");
            Perfil = LerTexto(dados, "perfil");

            if (string.IsNullOrWhiteSpace(Token) ||
                string.IsNullOrWhiteSpace(Nome) ||
                string.IsNullOrWhiteSpace(Email) ||
                string.IsNullOrWhiteSpace(Perfil))
            {
                LimparSessao();
                return (false, "A resposta da API não contém todos os dados necessários.");
            }

            _authenticationStateProvider.DefinirUsuario(Nome, Email, Perfil);
            return (true, "Login realizado com sucesso.");
        }
        catch (HttpRequestException)
        {
            LimparSessao();
            return (false, "Não foi possível conectar à API.");
        }
        catch (JsonException)
        {
            LimparSessao();
            return (false, "A API retornou uma resposta inválida.");
        }
        catch (TaskCanceledException)
        {
            LimparSessao();
            return (false, "A conexão com a API demorou demais para responder.");
        }
    }

    public HttpClient CriarClienteAutenticado()
    {
        if (string.IsNullOrWhiteSpace(Token))
            throw new InvalidOperationException("É necessário realizar o login antes de acessar a API.");

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        return client;
    }

    public void LimparSessao()
    {
        Token = null;
        Nome = null;
        Email = null;
        Perfil = null;
        _authenticationStateProvider.LimparUsuario();
    }

    private static string? LerTexto(JsonElement dados, string propriedade)
    {
        if (dados.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var item in dados.EnumerateObject())
        {
            if (string.Equals(item.Name, propriedade, StringComparison.OrdinalIgnoreCase))
                return item.Value.ValueKind == JsonValueKind.String ? item.Value.GetString() : null;
        }

        return null;
    }
}
