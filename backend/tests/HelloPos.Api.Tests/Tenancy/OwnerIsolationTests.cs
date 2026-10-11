using System.Net;
using System.Text.Json.Nodes;
using HelloPos.Api.Tests.Infrastructure.Database;
using static HelloPos.Api.Tests.Features.Businesses.CreateBusinessTests;
using static HelloPos.Api.Tests.Features.Businesses.OpenAndEditTests;

namespace HelloPos.Api.Tests.Tenancy;

/// <summary>
/// Feature 005 (FR-016, SC-002) on the shared harness, with real owners instead of the test header:
/// owner X has business A open, owner Y business B. X can never list, open, read or change B.
/// </summary>
public sealed class OwnerIsolationTests(PostgresFixture postgres) : IsolationTests(postgres)
{
    private readonly Dictionary<Guid, string> sessions = [];

    protected override async Task<(Guid A, Guid B)> CreateBusinessesAsync() =>
        (await OwnerWithOpenBusinessAsync("x@example.com", "Business A"),
         await OwnerWithOpenBusinessAsync("y@example.com", "Business B"));

    protected override HttpClient ClientFor(Guid business) => App.OwnerClient(withClientHeader: true, sessions[business]);

    /// <summary>The business itself is the record: its id must never reach the other owner.</summary>
    protected override async Task<IReadOnlyList<Guid>> SeedAsync(HttpClient asBusiness, string label)
    {
        var current = JsonNode.Parse(await (await GetCurrentAsync(asBusiness)).Content.ReadAsStringAsync(Ct))!;
        return [Guid.Parse((string)current["id"]!)];
    }

    [Fact]
    public Task ListBusinesses() => AssertCannotList("GET /owner/businesses", c => ListAsync(c));

    [Fact]
    public Task CurrentBusiness() => AssertCannotList("GET /business", c => GetCurrentAsync(c));

    [Fact]
    public Task OpenAnotherOwnersBusiness() => AssertCannotReference("POST /owner/businesses/{id}/open",
        id => new HttpRequestMessage(HttpMethod.Post, $"/owner/businesses/{id}/open"),
        c => GetCurrentAsync(c));

    [Fact]
    public async Task UpdateCannotReachTheOtherBusiness()
    {
        using var asA = AsA();
        using var asB = AsB();
        var before = await (await GetCurrentAsync(asB)).Content.ReadAsStringAsync(Ct);

        // A names B's business as its page: refused, not applied anywhere.
        using var stale = await UpdateAsync(asA, Settings("Taken", "IN", "INR", "tax_inclusive", 4, "Asia/Kolkata", 100), BusinessB);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        // A updates its own business: B is unchanged.
        using var own = await UpdateAsync(asA, Settings("Mine", "IN", "INR", "tax_inclusive", 4, "Asia/Kolkata", 100), BusinessA);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        if (await (await GetCurrentAsync(asB)).Content.ReadAsStringAsync(Ct) != before)
        {
            throw new IsolationViolationException("PUT /business: acting as business A changed business B.");
        }
    }

    private async Task<Guid> OwnerWithOpenBusinessAsync(string email, string name)
    {
        using var owner = await App.SignUpVerifiedOwnerAsync(email);
        var id = await CreateIdAsync(owner, name);
        using var opened = await OpenAsync(owner, id);
        Assert.Equal(HttpStatusCode.NoContent, opened.StatusCode);
        sessions[id] = TenancyApp.SessionCookieOf(opened) ?? throw new InvalidOperationException("Opening did not renew the session cookie.");
        return id;
    }
}
