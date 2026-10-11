using System.Net;
using System.Text.Json.Nodes;
using HelloPos.Api.Infrastructure.Identity.Email;
using HelloPos.Api.Tests.Features.Staff;
using HelloPos.Api.Tests.Infrastructure.Database;
using HelloPos.Api.Tests.Tenancy;
using static HelloPos.Api.Tests.Features.Businesses.CreateBusinessTests;
using static HelloPos.Api.Tests.Features.Businesses.OpenAndEditTests;
using static HelloPos.Api.Tests.Features.Owners.OwnerApi;
using static HelloPos.Api.Tests.Features.Staff.StaffApi;

namespace HelloPos.Api.Tests.Features.Invitations;

/// <summary>US1 invitations: FR-011, FR-011a; research R7.</summary>
[Collection(PostgresTests.Name)]
public sealed class InvitationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Invite_PreviewAndAccept_GivesTheInvitedRole()
    {
        var (app, owner, shop) = await OwnerWithShopAsync();
        await using var _ = app;
        using var __ = owner;

        var invited = JsonNode.Parse(await AssertContractAsync(
            await InviteAsync(owner, shop, "Mina@Example.com", "manager", "Mina"), HttpStatusCode.Created, "inviteStaff"))!;
        Assert.Equal("pending", (string?)invited["status"]);
        var listed = JsonNode.Parse(await AssertContractAsync(await ListInvitationsAsync(owner, shop), HttpStatusCode.OK, "listInvitations"))!.AsArray();
        Assert.Equal((string?)invited["id"], (string?)Assert.Single(listed)!["id"]);

        var token = InvitationTokenFor(app, "mina@example.com");
        using var mina = app.OwnerClient();
        var preview = JsonNode.Parse(await AssertContractAsync(await PreviewAsync(mina, token), HttpStatusCode.OK, "previewInvitation"))!;
        Assert.Equal("Shop", (string?)preview["businessName"]);
        Assert.Equal("manager", (string?)preview["role"]);

        using var signedIn = await app.SignUpVerifiedOwnerAsync("mina@example.com", displayName: "Mina P");
        await AssertContractAsync(await AcceptAsync(signedIn, token), HttpStatusCode.NoContent, "acceptInvitation");

        var businesses = JsonNode.Parse(await (await ListAsync(signedIn)).Content.ReadAsStringAsync(Ct))!.AsArray();
        Assert.Equal("manager", (string?)Assert.Single(businesses)!["role"]);
        Assert.Equal(HttpStatusCode.NoContent, (await OpenAsync(signedIn, shop)).StatusCode);
        var staff = JsonNode.Parse(await (await StaffApi.ListAsync(owner, shop)).Content.ReadAsStringAsync(Ct))!.AsArray();
        var mine = Assert.Single(staff, s => (string?)s!["displayName"] == "Mina");
        Assert.True((bool?)mine!["hasBackOfficeAccess"]);
        Assert.Equal("accepted", (string?)Assert.Single(JsonNode.Parse(await (await ListInvitationsAsync(owner, shop)).Content.ReadAsStringAsync(Ct))!.AsArray())!["status"]);
    }

    [Fact]
    public async Task WrongAccount_UnknownToken_UsedToken_AndExpired_AllLookTheSame()
    {
        var (app, owner, shop) = await OwnerWithShopAsync();
        await using var _ = app;
        using var __ = owner;
        await InviteAsync(owner, shop, "mina@example.com");
        var token = InvitationTokenFor(app, "mina@example.com");
        using var stranger = await app.SignUpVerifiedOwnerAsync("stranger@example.com");

        var wrongAccount = await AssertContractAsync(await AcceptAsync(stranger, token), HttpStatusCode.NotFound, "acceptInvitation");
        var unknown = await AssertContractAsync(await AcceptAsync(stranger, "not-a-token"), HttpStatusCode.NotFound, "acceptInvitation");
        Assert.Equal(WithoutTraceId(wrongAccount), WithoutTraceId(unknown));
        Assert.Equal(HttpStatusCode.NotFound, (await PreviewAsync(stranger, "not-a-token")).StatusCode);

        using var mina = await app.SignUpVerifiedOwnerAsync("mina@example.com");
        Assert.Equal(HttpStatusCode.NoContent, (await AcceptAsync(mina, token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AcceptAsync(mina, token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PreviewAsync(mina, token)).StatusCode);
    }

    [Fact]
    public async Task Invitation_ExpiresAfterSevenDays()
    {
        var (app, owner, shop) = await OwnerWithShopAsync();
        await using var _ = app;
        using var __ = owner;
        await InviteAsync(owner, shop, "mina@example.com");
        var token = InvitationTokenFor(app, "mina@example.com");

        app.Clock.Advance(TimeSpan.FromDays(7) + TimeSpan.FromMinutes(1));
        using var mina = await app.SignUpVerifiedOwnerAsync("mina@example.com");
        await SignInAsync(owner, "owner@example.com"); // the owner's session went idle too
        await OpenAsync(owner, shop);

        Assert.Equal(HttpStatusCode.NotFound, (await PreviewAsync(mina, token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AcceptAsync(mina, token)).StatusCode);
        Assert.Equal("expired", (string?)Assert.Single(JsonNode.Parse(await (await ListInvitationsAsync(owner, shop)).Content.ReadAsStringAsync(Ct))!.AsArray())!["status"]);
    }

    [Fact]
    public async Task DuplicatePendingInvite_AndExistingMember_AreConflicts()
    {
        var (app, owner, shop) = await OwnerWithShopAsync();
        await using var _ = app;
        using var __ = owner;
        Assert.Equal(HttpStatusCode.Created, (await InviteAsync(owner, shop, "mina@example.com")).StatusCode);

        await AssertContractAsync(await InviteAsync(owner, shop, "MINA@example.com"), HttpStatusCode.Conflict, "inviteStaff");
        var member = await AssertContractAsync(await InviteAsync(owner, shop, "owner@example.com"), HttpStatusCode.Conflict, "inviteStaff");
        Assert.Equal("urn:hellopos:problem:already-member", (string?)JsonNode.Parse(member)!["type"]);
    }

    [Fact]
    public async Task AcceptingIntoABusinessYouAlreadyWorkAt_IsAConflict()
    {
        var (app, owner, shop) = await OwnerWithShopAsync();
        await using var _ = app;
        using var __ = owner;
        Assert.Equal(HttpStatusCode.Created, (await InviteAsync(owner, shop, "mina@example.com")).StatusCode);
        using var mina = await app.SignUpVerifiedOwnerAsync("mina@example.com");
        await app.AddStaffAsync(shop, Kernel.Staff.StaffRole.Cashier, await TenancyApp.AccountIdAsync(mina));

        var body = await AssertContractAsync(await AcceptAsync(mina, InvitationTokenFor(app, "mina@example.com")), HttpStatusCode.Conflict, "acceptInvitation");

        Assert.Equal("urn:hellopos:problem:already-member", (string?)JsonNode.Parse(body)!["type"]);
    }

    [Fact]
    public async Task Invite_LooksTheSame_WhetherOrNotTheAddressHasAnAccount()
    {
        var (app, owner, shop) = await OwnerWithShopAsync();
        await using var _ = app;
        using var __ = owner;
        (await app.SignUpVerifiedOwnerAsync("existing@example.com")).Dispose();

        using var toExisting = await InviteAsync(owner, shop, "existing@example.com");
        using var toNew = await InviteAsync(owner, shop, "new@example.com");

        Assert.Equal(HttpStatusCode.Created, toExisting.StatusCode);
        Assert.Equal(HttpStatusCode.Created, toNew.StatusCode);
        Assert.Single(app.Emails.To("existing@example.com"), m => m.Kind == OwnerEmails.InvitationKind);
        Assert.Single(app.Emails.To("new@example.com"), m => m.Kind == OwnerEmails.InvitationKind);
    }

    [Fact]
    public async Task InvalidInvite_IsRejectedByField()
    {
        var (app, owner, shop) = await OwnerWithShopAsync();
        await using var _ = app;
        using var __ = owner;

        var body = await AssertContractAsync(await InviteAsync(owner, shop, "not-an-email", "manager", ""), HttpStatusCode.BadRequest, "inviteStaff");

        Assert.Contains("email", ErrorFields(body));
        Assert.Contains("displayName", ErrorFields(body));
        Assert.Empty(app.Emails.To("not-an-email"));
    }

    [Fact]
    public async Task PreviewAndAccept_AreRateLimited()
    {
        await using var app = await TenancyApp.CreateAsync(postgres,
            settings: new Dictionary<string, string?> { ["RateLimits:OwnerAuthPerMinute"] = "10" });
        using var client = app.OwnerClient();

        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(HttpStatusCode.NotFound, (await PreviewAsync(client, $"guess-{i}")).StatusCode);
        }

        await AssertContractAsync(await PreviewAsync(client, "one-more"), HttpStatusCode.TooManyRequests, "previewInvitation");
    }

    private async Task<(TenancyApp App, HttpClient Owner, Guid Shop)> OwnerWithShopAsync()
    {
        var app = await TenancyApp.CreateAsync(postgres);
        var owner = await app.SignUpVerifiedOwnerAsync("owner@example.com");
        var shop = await CreateIdAsync(owner, "Shop");
        await OpenAsync(owner, shop);
        return (app, owner, shop);
    }
}
