using System.Net;
using System.Text.Json.Nodes;
using HelloPos.Api.Tests.Features.Staff;
using HelloPos.Api.Tests.Infrastructure.Database;
using static HelloPos.Api.Tests.Features.Businesses.OpenAndEditTests;

namespace HelloPos.Api.Tests.Tenancy;

/// <summary>
/// Feature 006 FR-012, SC-002 on the shared harness: owner X has business A open, owner Y business B;
/// acting as A never reaches B's staff or invitations.
/// </summary>
public sealed class StaffIsolationTests(PostgresFixture postgres) : IsolationTests(postgres)
{
    private readonly Dictionary<Guid, string> sessions = [];

    protected override async Task<(Guid A, Guid B)> CreateBusinessesAsync() =>
        (await OwnerWithOpenBusinessAsync("x@example.com", "Business A"),
         await OwnerWithOpenBusinessAsync("y@example.com", "Business B"));

    protected override HttpClient ClientFor(Guid business) => App.OwnerClient(withClientHeader: true, sessions[business]);

    protected override async Task<IReadOnlyList<Guid>> SeedAsync(HttpClient asBusiness, string label)
    {
        var business = label == "A" ? BusinessA : BusinessB;
        var ids = new List<Guid>();
        foreach (var name in new[] { "ravi", "mina" })
        {
            using var response = await StaffApi.AddAsync(asBusiness, business, StaffApi.NewStaff($"{label} {name}", "cashier", $"{label.ToLowerInvariant()}{name}"));
            response.EnsureSuccessStatusCode();
            ids.Add(Guid.Parse((string)JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!["id"]!));
        }

        using var invited = await StaffApi.InviteAsync(asBusiness, business, $"{label}-invitee@example.com");
        invited.EnsureSuccessStatusCode();
        ids.Add(Guid.Parse((string)JsonNode.Parse(await invited.Content.ReadAsStringAsync(Ct))!["id"]!));
        return ids;
    }

    [Fact]
    public Task ListStaff() => AssertCannotList("GET /staff", c => StaffApi.ListAsync(c, BusinessA, "all"));

    [Fact]
    public Task ListInvitations() => AssertCannotList("GET /staff/invitations", c => StaffApi.ListInvitationsAsync(c, BusinessA));

    [Fact]
    public Task GetStaff() => AssertCannotRead("GET /staff/{id}", id => $"/staff/{id}");

    [Fact]
    public async Task NamingTheOtherBusinessInTheHeader_IsRefused()
    {
        using var asA = AsA();

        using var response = await StaffApi.ListAsync(asA, BusinessB);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
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
