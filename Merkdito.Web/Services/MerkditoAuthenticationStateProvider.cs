using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Merkdito.Web.Services;

public sealed class MerkditoAuthenticationStateProvider : AuthenticationStateProvider
{
    private ClaimsPrincipal _usuario = new(new ClaimsIdentity());

    public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
        Task.FromResult(new AuthenticationState(_usuario));

    public void DefinirUsuario(string nome, string email, string perfil)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, nome),
            new(ClaimTypes.Email, email),
            new(ClaimTypes.Role, perfil)
        };

        var identidade = new ClaimsIdentity(claims, authenticationType: "Merkdito");
        _usuario = new ClaimsPrincipal(identidade);
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public void LimparUsuario()
    {
        _usuario = new ClaimsPrincipal(new ClaimsIdentity());
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }
}
