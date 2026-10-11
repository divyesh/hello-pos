using System.Diagnostics;
using System.Net;
using System.Text.Json;
using HelloPos.Api.Tests.Contract;
using HelloPos.Api.Tests.Infrastructure.Database;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace HelloPos.Api.Tests.Features.Health;

/// <summary>
/// FR-013, FR-014, SC-003: with a database configured, /health reports available or degraded, stays
/// contract-conformant, answers within 3 seconds and never reveals database details.
/// </summary>
[Collection(PostgresTests.Name)]
public sealed class GetHealthDatabaseTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Health_FollowsDatabaseAvailability()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.UseSetting("ConnectionStrings:HelloPos", postgres.ConnectionString));
        using var client = factory.CreateClient();

        Assert.Equal("available", (await GetHealth(client)).Status);

        await postgres.PauseAsync();
        try
        {
            var watch = Stopwatch.StartNew();
            var degraded = await GetHealth(client);
            watch.Stop();

            Assert.Equal("degraded", degraded.Status);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"/health took {watch.Elapsed}");
            AssertConformsAndRevealsNothing(degraded.Body);
        }
        finally
        {
            await postgres.UnpauseAsync();
        }

        Assert.Equal("available", (await GetHealth(client)).Status);
    }

    private void AssertConformsAndRevealsNothing(string body)
    {
        var violations = OpenApiContract.Validate(OpenApiContract.ResponseSchema("getHealth", "200"), body);
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));

        var settings = new NpgsqlConnectionStringBuilder(postgres.ConnectionString);
        foreach (var secret in new[] { settings.Host!, settings.Database!, settings.Username!, "Npgsql", "timeout", "exception" })
        {
            Assert.DoesNotContain(secret, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static async Task<(string Status, string Body)> GetHealth(HttpClient client)
    {
        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(body);
        return (json.RootElement.GetProperty("status").GetString()!, body);
    }
}
