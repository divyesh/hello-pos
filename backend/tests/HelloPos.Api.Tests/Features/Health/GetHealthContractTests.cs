using System.Net;
using HelloPos.Api.Tests.Contract;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HelloPos.Api.Tests.Features.Health;

/// <summary>SC-004: any change to the health response that the contract does not describe fails here.</summary>
public sealed class GetHealthContractTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task GetHealth_ResponseConformsToContract()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var violations = OpenApiContract.Validate(OpenApiContract.ResponseSchema("getHealth", "200"), body);
        Assert.True(violations.Count == 0,
            $"GET /health does not match contracts/openapi.yaml:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}{Environment.NewLine}Body: {body}");
    }

    [Fact]
    public void ContractSchema_RejectsUndeclaredFields()
    {
        var schema = OpenApiContract.ResponseSchema("getHealth", "200");
        const string withExtraField =
            """{"service":"hello-pos-api","version":"1","status":"available","serverTimeUtc":"2026-10-10T14:03:22Z","secret":"x"}""";

        Assert.NotEmpty(OpenApiContract.Validate(schema, withExtraField));
    }
}
