using HelloPos.Web.ApiClient;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;

namespace HelloPos.Web.Services;

/// <summary>The generated API client over the app's <see cref="HttpClient"/> (cookie and CSRF header included).</summary>
public static class ApiClients
{
    public static HelloPosApiClient Create(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        return new HelloPosApiClient(new HttpClientRequestAdapter(new AnonymousAuthenticationProvider(), httpClient: http)
        {
            BaseUrl = http.BaseAddress?.ToString().TrimEnd('/'),
        });
    }
}
