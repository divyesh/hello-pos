using System.Net;
using System.Text.Json.Nodes;
using HelloPos.Api.Infrastructure.Identity.Email;
using HelloPos.Api.Tests.Features.Businesses;
using HelloPos.Api.Tests.Infrastructure.Database;
using HelloPos.Api.Tests.Tenancy;
using HelloPos.Kernel.Staff;
using static HelloPos.Api.Tests.Features.Businesses.CreateBusinessTests;
using static HelloPos.Api.Tests.Features.Businesses.OpenAndEditTests;
using static HelloPos.Api.Tests.Features.Owners.OwnerApi;
using static HelloPos.Api.Tests.Features.Staff.StaffApi;

namespace HelloPos.Api.Tests.Features.Staff;

/// <summary>US3: managers, co-owners and the last owner; FR-002–FR-006, SC-003; research R4, R6.</summary>
[Collection(PostgresTests.Name)]
public sealed class RoleRulesTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Manager_IsRefusedEverythingBeyondCashiers_AndNothingChanges()
    {
        var shop = await ShopAsync();
        await using var app = shop.App;
        using var owner = shop.Owner;
        var ravi = await AddPersonAsync(owner, shop.Id, "Ravi", "cashier", "ravi");
        var boss = await AddPersonAsync(owner, shop.Id, "Boss", "manager", "boss");
        var ownerStaff = await IdOfAsync(owner, shop.Id, "Owner");
        var (manager, _) = await app.SignedInStaffClientAsync("manager@example.com", shop.Id, StaffRole.Manager);
        using var __ = manager;
        var before = await (await StaffApi.ListAsync(owner, shop.Id, "all")).Content.ReadAsStringAsync(Ct);

        var refused = new (string What, HttpResponseMessage Response)[]
        {
            ("promote a cashier", await UpdateAsync(manager, shop.Id, ravi, "Ravi", "manager", 1)),
            ("rename the owner", await UpdateAsync(manager, shop.Id, ownerStaff, "Taken", "owner", 1)),
            ("deactivate the owner", await DeactivateAsync(manager, shop.Id, ownerStaff, 1)),
            ("rename another manager", await UpdateAsync(manager, shop.Id, boss, "Taken", "manager", 1)),
            ("deactivate another manager", await DeactivateAsync(manager, shop.Id, boss, 1)),
            ("set another manager's PIN", await SetPinAsync(manager, shop.Id, boss, "boss", "9071", 1)),
            ("add a manager", await AddAsync(manager, shop.Id, NewStaff("New boss", "manager", "newboss"))),
            ("invite", await InviteAsync(manager, shop.Id, "someone@example.com")),
            ("list invitations", await ListInvitationsAsync(manager, shop.Id)),
            ("change business settings", await OpenAndEditTests.UpdateAsync(manager, Settings("Taken", "IN", "INR", "tax_inclusive", 4, "Asia/Kolkata", 100), shop.Id)),
        };

        foreach (var (what, response) in refused)
        {
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"a manager could {what}: {(int)response.StatusCode}");
        }

        Assert.Equal(before, await (await StaffApi.ListAsync(owner, shop.Id, "all")).Content.ReadAsStringAsync(Ct));
        Assert.Empty(app.Emails.To("someone@example.com"));
    }

    [Fact]
    public async Task Manager_ManagesCashiers()
    {
        var shop = await ShopAsync();
        await using var app = shop.App;
        using var owner = shop.Owner;
        var ravi = await AddPersonAsync(owner, shop.Id, "Ravi", "cashier", "ravi");
        var (manager, _) = await app.SignedInStaffClientAsync("manager@example.com", shop.Id, StaffRole.Manager);
        using var __ = manager;

        Assert.Equal(HttpStatusCode.OK, (await UpdateAsync(manager, shop.Id, ravi, "Ravi K", "cashier", 1)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SetPinAsync(manager, shop.Id, ravi, "ravi", "9071", 2)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await DeactivateAsync(manager, shop.Id, ravi, 3)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ReactivateAsync(manager, shop.Id, ravi, 4)).StatusCode);
        Assert.True(await PinVerifiesAsync(app, shop.Id, "ravi", "9071"));
    }

    [Fact]
    public async Task CashierAccount_CannotReachTheBackOffice()
    {
        var shop = await ShopAsync();
        await using var app = shop.App;
        using var owner = shop.Owner;
        using var cashier = await app.SignUpVerifiedOwnerAsync("cashier@example.com");
        await app.AddStaffAsync(shop.Id, StaffRole.Cashier, await TenancyApp.AccountIdAsync(cashier));

        Assert.Equal(HttpStatusCode.NotFound, (await OpenAsync(cashier, shop.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await StaffApi.ListAsync(cashier, shop.Id)).StatusCode);
    }

    [Fact]
    public async Task CoOwner_ByInvitation_HasAnOwnersRights()
    {
        var shop = await ShopAsync();
        await using var app = shop.App;
        using var owner = shop.Owner;
        await InviteAsync(owner, shop.Id, "partner@example.com", "owner", "Partner");
        using var partner = await app.SignUpVerifiedOwnerAsync("partner@example.com");
        Assert.Equal(HttpStatusCode.NoContent, (await AcceptAsync(partner, InvitationTokenFor(app, "partner@example.com"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await OpenAsync(partner, shop.Id)).StatusCode);

        var ravi = await AddPersonAsync(partner, shop.Id, "Ravi", "cashier", "ravi");
        Assert.Equal(HttpStatusCode.OK, (await UpdateAsync(partner, shop.Id, ravi, "Ravi", "manager", 1)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await InviteAsync(partner, shop.Id, "mgr@example.com")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await OpenAndEditTests.UpdateAsync(partner, Settings("Renamed", "IN", "INR", "tax_inclusive", 4, "Asia/Kolkata", 100), shop.Id)).StatusCode);
        Assert.Equal("owner", (string?)JsonNode.Parse(await (await MeAsync(partner)).Content.ReadAsStringAsync(Ct))!["currentRole"]);
    }

    [Fact]
    public async Task TheLastActiveOwner_CannotBeDeactivatedOrDemoted()
    {
        var shop = await ShopAsync();
        await using var app = shop.App;
        using var owner = shop.Owner;
        var me = await IdOfAsync(owner, shop.Id, "Owner");

        foreach (var response in new[]
                 {
                     await DeactivateAsync(owner, shop.Id, me, 1),
                     await UpdateAsync(owner, shop.Id, me, "Owner", "manager", 1),
                 })
        {
            var body = await AssertContractAsync(response, HttpStatusCode.Conflict, response.RequestMessage!.Method == HttpMethod.Put ? "updateStaff" : "deactivateStaff");
            Assert.Equal("urn:hellopos:problem:last-owner", (string?)JsonNode.Parse(body)!["type"]);
        }

        Assert.Equal(HttpStatusCode.OK, (await StaffApi.ListAsync(owner, shop.Id)).StatusCode);
    }

    [Fact]
    public async Task AnOwner_CanStepDown_WhenAnotherOwnerRemains_AndLosesAccessAtOnce()
    {
        var shop = await ShopAsync();
        await using var app = shop.App;
        using var owner = shop.Owner;
        var (partner, _) = await app.SignedInStaffClientAsync("partner@example.com", shop.Id, StaffRole.Owner);
        using var __ = partner;
        var me = await IdOfAsync(owner, shop.Id, "Owner");

        Assert.Equal(HttpStatusCode.OK, (await DeactivateAsync(owner, shop.Id, me, 1)).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await StaffApi.ListAsync(owner, shop.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await StaffApi.ListAsync(partner, shop.Id)).StatusCode);
    }

    [Fact]
    public async Task TwoOwnersDeactivatingEachOther_AtOnce_LeaveExactlyOneOwner()
    {
        var shop = await ShopAsync();
        await using var app = shop.App;
        using var owner = shop.Owner;
        var (partner, partnerId) = await app.SignedInStaffClientAsync("partner@example.com", shop.Id, StaffRole.Owner);
        using var __ = partner;
        var ownerId = await IdOfAsync(owner, shop.Id, "Owner");

        var results = await Task.WhenAll(
            DeactivateAsync(owner, shop.Id, partnerId, 1),
            DeactivateAsync(partner, shop.Id, ownerId, 1));

        // One wins. The other is refused: 409 when both checks overlap (last-owner lock), or 403 when the
        // winner finished first and the loser is already inactive. Either way exactly one owner remains.
        var statuses = results.Select(r => r.StatusCode).ToList();
        Assert.True(
            statuses.Count(s => s == HttpStatusCode.OK) == 1
                && statuses.Count(s => s is HttpStatusCode.Conflict or HttpStatusCode.Forbidden) == 1,
            $"Expected one OK and one 409/403, got {string.Join(", ", statuses)}");
        var survivor = results[0].StatusCode == HttpStatusCode.OK ? owner : partner;
        var people = JsonNode.Parse(await (await StaffApi.ListAsync(survivor, shop.Id)).Content.ReadAsStringAsync(Ct))!.AsArray();
        Assert.Single(people, p => (string?)p!["role"] == "owner" && (bool?)p["active"] == true);
    }

    [Fact]
    public async Task Revoke_KillsTheLink_Resend_ReplacesIt()
    {
        var shop = await ShopAsync();
        await using var app = shop.App;
        using var owner = shop.Owner;
        var revokedId = await InvitationIdAsync(owner, shop.Id, "gone@example.com");
        var revokedToken = InvitationTokenFor(app, "gone@example.com");
        var resentId = await InvitationIdAsync(owner, shop.Id, "again@example.com");
        var oldToken = InvitationTokenFor(app, "again@example.com");

        await AssertContractAsync(await RevokeAsync(owner, shop.Id, revokedId), HttpStatusCode.NoContent, "revokeInvitation");
        await AssertContractAsync(await ResendAsync(owner, shop.Id, resentId), HttpStatusCode.NoContent, "resendInvitation");

        using var anyone = app.OwnerClient();
        Assert.Equal(HttpStatusCode.NotFound, (await PreviewAsync(anyone, revokedToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PreviewAsync(anyone, oldToken)).StatusCode);
        Assert.Equal(2, app.Emails.To("again@example.com").Count(m => m.Kind == OwnerEmails.InvitationKind));
        Assert.Equal(HttpStatusCode.OK, (await PreviewAsync(anyone, InvitationTokenFor(app, "again@example.com"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await RevokeAsync(owner, shop.Id, revokedId)).StatusCode);
        Assert.Contains("revoked", await (await ListInvitationsAsync(owner, shop.Id)).Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resend_RenewsAnExpiredInvitation()
    {
        var shop = await ShopAsync();
        await using var app = shop.App;
        using var owner = shop.Owner;
        var id = await InvitationIdAsync(owner, shop.Id, "late@example.com");
        app.Clock.Advance(TimeSpan.FromDays(8));
        await SignInAsync(owner, "owner@example.com");
        await OpenAsync(owner, shop.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await ResendAsync(owner, shop.Id, id)).StatusCode);

        using var anyone = app.OwnerClient();
        Assert.Equal(HttpStatusCode.OK, (await PreviewAsync(anyone, InvitationTokenFor(app, "late@example.com"))).StatusCode);
    }

    private async Task<(TenancyApp App, HttpClient Owner, Guid Id)> ShopAsync()
    {
        var app = await TenancyApp.CreateAsync(postgres);
        var owner = await app.SignUpVerifiedOwnerAsync("owner@example.com");
        var shop = await CreateIdAsync(owner, "Shop");
        await OpenAsync(owner, shop);
        return (app, owner, shop);
    }

    private static async Task<Guid> AddPersonAsync(HttpClient client, Guid shop, string name, string role, string username)
    {
        using var response = await AddAsync(client, shop, NewStaff(name, role, username));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return Guid.Parse((string)JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!["id"]!);
    }

    private static async Task<Guid> IdOfAsync(HttpClient client, Guid shop, string name)
    {
        var people = JsonNode.Parse(await (await StaffApi.ListAsync(client, shop, "all")).Content.ReadAsStringAsync(Ct))!.AsArray();
        return Guid.Parse((string)people.Single(p => (string?)p!["displayName"] == name)!["id"]!);
    }

    private static async Task<Guid> InvitationIdAsync(HttpClient owner, Guid shop, string email)
    {
        using var response = await InviteAsync(owner, shop, email);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return Guid.Parse((string)JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!["id"]!);
    }
}
