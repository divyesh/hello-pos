using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HelloPos.Api.Tests.Features.Health;

public sealed class GetHealthTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task GetHealth_WithoutCredentials_ReturnsServiceStatus()
    {
        using var client = factory.CreateClient();
        var before = DateTimeOffset.UtcNow;

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var root = body.RootElement;

        Assert.Equal("hello-pos-api", root.GetProperty("service").GetString());
        Assert.Equal("available", root.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("version").GetString()));

        var serverTime = root.GetProperty("serverTimeUtc").GetString()!;
        Assert.EndsWith("Z", serverTime, StringComparison.Ordinal);
        var parsed = DateTimeOffset.Parse(serverTime, CultureInfo.InvariantCulture);
        Assert.InRange(parsed, before.AddSeconds(-5), DateTimeOffset.UtcNow.AddSeconds(5));
    }
}
