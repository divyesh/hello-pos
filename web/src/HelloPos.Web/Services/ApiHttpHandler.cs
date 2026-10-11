using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace HelloPos.Web.Services;

/// <summary>
/// Every API request from the browser includes the session cookie (feature 005, research R3) and the
/// CSRF header <c>X-HelloPos-Client: web</c>. The cookie is HttpOnly: this code never sees it.
/// </summary>
public sealed class ApiHttpHandler : DelegatingHandler
{
    public const string ClientHeader = "X-HelloPos-Client";
    public const string WebClient = "web";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        request.Headers.TryAddWithoutValidation(ClientHeader, WebClient);
        return base.SendAsync(request, cancellationToken);
    }
}
