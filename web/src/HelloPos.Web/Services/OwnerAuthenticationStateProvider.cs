using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace HelloPos.Web.Services;

/// <summary>
/// Who is signed in, for <c>AuthorizeView</c> and the layout: asks the API (<c>/owner/me</c>), since
/// the session cookie is HttpOnly and invisible to this app. Pages call <see cref="Refresh"/> after
/// signing in or out.
/// </summary>
public sealed class OwnerAuthenticationStateProvider(IOwnerAccountService accounts) : AuthenticationStateProvider
{
    public const string OwnerIdClaim = "owner_id";
    private static readonly AuthenticationState Anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));

    private Task<AuthenticationState>? current;

    public override Task<AuthenticationState> GetAuthenticationStateAsync() => current ??= LoadAsync();

    public void Refresh()
    {
        current = LoadAsync();
        NotifyAuthenticationStateChanged(current);
    }

    private async Task<AuthenticationState> LoadAsync()
    {
        var session = await accounts.GetSessionAsync().ConfigureAwait(false);
        if (session is null)
        {
            return Anonymous;
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, session.DisplayName),
                new Claim(ClaimTypes.Email, session.Email),
                new Claim(OwnerIdClaim, session.OwnerId.ToString()),
            ],
            authenticationType: "hellopos-session");
        return new AuthenticationState(new ClaimsPrincipal(identity));
    }
}
