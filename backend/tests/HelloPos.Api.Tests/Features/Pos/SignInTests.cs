using System.Net;
using System.Text.Json.Nodes;
using HelloPos.Api.Infrastructure.Pos;
using HelloPos.Api.Tests.Features.Staff;
using HelloPos.Api.Tests.Infrastructure.Database;
using HelloPos.Api.Tests.Tenancy;
using Npgsql;
using static HelloPos.Api.Tests.Features.Businesses.OpenAndEditTests;
using static HelloPos.Api.Tests.Features.Owners.OwnerApi;
using static HelloPos.Api.Tests.Features.Pos.PosApi;

namespace HelloPos.Api.Tests.Features.Pos;

/// <summary>US2 sign in, sign out, guessing: FR-004–FR-010, FR-014; SC-003; research R2, R3.</summary>
[Collection(PostgresTests.Name)]
public sealed class SignInTests(PostgresFixture postgres)
{
    private const string Pin = "4826";
    private const string WrongPin = "9071";

    [Fact]
    public async Task RightPin_SignsIn_AndMeDescribesThePerson()
    {
        await using var shop = await ShopAsync();
        using var device = DeviceClient(shop.App, shop.Device.Credential);

        var body = JsonNode.Parse(await AssertContractAsync(await PosSignInAsync(device, "RAVI", Pin), HttpStatusCode.OK, "posSignIn"))!;

        Assert.StartsWith("pat_", (string)body["tokens"]!["accessToken"]!, StringComparison.Ordinal);
        Assert.StartsWith("prt_", (string)body["tokens"]!["refreshToken"]!, StringComparison.Ordinal);
        Assert.Equal("cashier", (string?)body["staff"]!["role"]);
        using var session = SessionClient(shop.App, (string)body["tokens"]!["accessToken"]!);
        var me = JsonNode.Parse(await AssertContractAsync(await PosMeAsync(session), HttpStatusCode.OK, "posMe"))!;
        Assert.Equal("Ravi", (string?)me["displayName"]);
        Assert.Equal("Shop", (string?)me["businessName"]);
    }

    [Fact]
    public async Task EveryFailure_LooksTheSame()
    {
        await using var shop = await ShopAsync();
        using var device = DeviceClient(shop.App, shop.Device.Credential);
        await AddCashierAsync(shop, "gone", Pin, deactivate: true);
        var otherShop = await CreateIdAsync(shop.Owner, "Other shop");
        await OpenAsync(shop.Owner, otherShop);
        await StaffApi.AddAsync(shop.Owner, otherShop, StaffApi.NewStaff("Mina", "cashier", "mina", Pin));

        var bodies = new List<string>();
        foreach (var (username, pin) in new[] { ("ravi", WrongPin), ("nobody", Pin), ("gone", Pin), ("mina", Pin) })
        {
            shop.App.Clock.Advance(TimeSpan.FromMinutes(6)); // past any device back-off
            bodies.Add(WithoutTraceId(await AssertContractAsync(await PosSignInAsync(device, username, pin), HttpStatusCode.Unauthorized, "posSignIn")));
        }

        Assert.Single(bodies.Distinct());
    }

    [Fact]
    public async Task FiveFailures_LockThePerson_OnEveryDevice_ForFifteenMinutes()
    {
        await using var shop = await ShopAsync();
        using var d2 = await RegisterDeviceAsync(shop.App, shop.Owner, shop.Id, "Till 2");
        using var d3 = await RegisterDeviceAsync(shop.App, shop.Owner, shop.Id, "Till 3");
        using var d4 = await RegisterDeviceAsync(shop.App, shop.Owner, shop.Id, "Till 4");
        using var one = DeviceClient(shop.App, shop.Device.Credential);
        using var two = DeviceClient(shop.App, d2.Credential);
        using var three = DeviceClient(shop.App, d3.Credential);
        using var four = DeviceClient(shop.App, d4.Credential);

        // Two failures on each of two devices and one on a third: no device backs off, the person locks.
        foreach (var device in new[] { one, one, two, two, three })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await PosSignInAsync(device, "ravi", WrongPin)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await PosSignInAsync(three, "ravi", Pin)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PosSignInAsync(four, "ravi", Pin)).StatusCode);
        shop.App.Clock.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(1));
        Assert.Equal(HttpStatusCode.OK, (await PosSignInAsync(four, "ravi", Pin)).StatusCode);
        Assert.Contains("locked_out", await AuditAsync(shop.App));
    }

    [Fact]
    public async Task ADevice_BacksOff_AfterThreeFailures_WhateverTheUsername()
    {
        await using var shop = await ShopAsync();
        using var device = DeviceClient(shop.App, shop.Device.Credential);
        foreach (var username in new[] { "a1", "b2", "c3" })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await PosSignInAsync(device, username, Pin)).StatusCode);
        }

        using var waiting = await PosSignInAsync(device, "ravi", Pin);
        await AssertContractAsync(waiting, HttpStatusCode.TooManyRequests, "posSignIn");
        Assert.Equal("1", waiting.Headers.RetryAfter?.ToString());

        shop.App.Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(HttpStatusCode.OK, (await PosSignInAsync(device, "ravi", Pin)).StatusCode);
    }

    [Fact]
    public async Task Refresh_RotatesTokens_AndAReusedRefreshTokenEndsTheSession()
    {
        await using var shop = await ShopAsync();
        using var device = DeviceClient(shop.App, shop.Device.Credential);
        var (access, refresh) = await SignedInAsync(device, "ravi", Pin);

        var rotated = JsonNode.Parse(await AssertContractAsync(await PosRefreshAsync(device, refresh), HttpStatusCode.OK, "posRefresh"))!;
        var newAccess = (string)rotated["accessToken"]!;
        Assert.NotEqual(refresh, (string)rotated["refreshToken"]!);
        using (var oldSession = SessionClient(shop.App, access))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await PosMeAsync(oldSession)).StatusCode);
        }

        using var session = SessionClient(shop.App, newAccess);
        Assert.Equal(HttpStatusCode.OK, (await PosMeAsync(session)).StatusCode);

        await AssertContractAsync(await PosRefreshAsync(device, refresh), HttpStatusCode.Unauthorized, "posRefresh");
        Assert.Equal(HttpStatusCode.Unauthorized, (await PosMeAsync(session)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PosRefreshAsync(device, (string)rotated["refreshToken"]!)).StatusCode);
    }

    [Fact]
    public async Task AccessTokens_ExpireAfterFifteenMinutes_RefreshKeepsWorking()
    {
        await using var shop = await ShopAsync();
        using var device = DeviceClient(shop.App, shop.Device.Credential);
        var (access, refresh) = await SignedInAsync(device, "ravi", Pin);
        using var session = SessionClient(shop.App, access);

        shop.App.Clock.Advance(TimeSpan.FromMinutes(16));

        Assert.Equal(HttpStatusCode.Unauthorized, (await PosMeAsync(session)).StatusCode);
        var rotated = JsonNode.Parse(await (await PosRefreshAsync(device, refresh)).Content.ReadAsStringAsync(Ct))!;
        using var renewed = SessionClient(shop.App, (string)rotated["accessToken"]!);
        Assert.Equal(HttpStatusCode.OK, (await PosMeAsync(renewed)).StatusCode);
    }

    [Fact]
    public async Task SignOut_EndsTheSessionOnTheServer()
    {
        await using var shop = await ShopAsync();
        using var device = DeviceClient(shop.App, shop.Device.Credential);
        var (access, refresh) = await SignedInAsync(device, "ravi", Pin);
        using var session = SessionClient(shop.App, access);

        await AssertContractAsync(await PosSignOutAsync(session), HttpStatusCode.NoContent, "posSignOut");

        Assert.Equal(HttpStatusCode.Unauthorized, (await PosMeAsync(session)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PosRefreshAsync(device, refresh)).StatusCode);
        var audit = await AuditAsync(shop.App);
        Assert.Contains("signed_in", audit);
        Assert.Contains("signed_out", audit);
    }

    [Fact]
    public async Task RolesApply_ToPosSessions()
    {
        await using var shop = await ShopAsync();
        await StaffApi.AddAsync(shop.Owner, shop.Id, StaffApi.NewStaff("Mina", "manager", "mina", Pin));
        using var device = DeviceClient(shop.App, shop.Device.Credential);

        var (cashierAccess, _) = await SignedInAsync(device, "ravi", Pin);
        using (var cashier = SessionClient(shop.App, cashierAccess))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync(new Uri("/staff", UriKind.Relative), Ct)).StatusCode);
        }

        var (managerAccess, _) = await SignedInAsync(device, "mina", Pin);
        using var manager = SessionClient(shop.App, managerAccess);
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync(new Uri("/staff", UriKind.Relative), Ct)).StatusCode);
    }

    [Fact]
    public async Task AToken_CannotBeBentToAnotherBusiness()
    {
        await using var shop = await ShopAsync();
        using var device = DeviceClient(shop.App, shop.Device.Credential);
        var (access, _) = await SignedInAsync(device, "ravi", Pin);
        var parsed = PosSecrets.Parse(PosSecretKind.AccessToken, access)!.Value;
        var otherShop = await CreateIdAsync(shop.Owner, "Other shop");

        using var forged = SessionClient(shop.App, access.Replace($"{parsed.BusinessId:N}", $"{otherShop:N}", StringComparison.Ordinal));

        Assert.Equal(HttpStatusCode.Unauthorized, (await PosMeAsync(forged)).StatusCode);
    }

    internal sealed record Shop(TenancyApp App, HttpClient Owner, Guid Id, TestDevice Device) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Device.Dispose();
            Owner.Dispose();
            await App.DisposeAsync();
        }
    }

    /// <summary>An owner with a shop open, cashier Ravi (ravi / 4826) and one registered device.</summary>
    internal static async Task<Shop> ShopWithCashierAsync(PostgresFixture postgres)
    {
        var app = await TenancyApp.CreateAsync(postgres);
        var owner = await app.SignUpVerifiedOwnerAsync("owner@example.com");
        var id = await CreateIdAsync(owner, "Shop");
        await OpenAsync(owner, id);
        using (var added = await StaffApi.AddAsync(owner, id, StaffApi.NewStaff("Ravi", "cashier", "ravi", Pin)))
        {
            Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        }

        return new Shop(app, owner, id, await RegisterDeviceAsync(app, owner, id));
    }

    private Task<Shop> ShopAsync() => ShopWithCashierAsync(postgres);

    private static async Task AddCashierAsync(Shop shop, string username, string pin, bool deactivate)
    {
        using var added = await StaffApi.AddAsync(shop.Owner, shop.Id, StaffApi.NewStaff(username, "cashier", username, pin));
        var id = Guid.Parse((string)JsonNode.Parse(await added.Content.ReadAsStringAsync(Ct))!["id"]!);
        if (deactivate)
        {
            Assert.Equal(HttpStatusCode.OK, (await StaffApi.DeactivateAsync(shop.Owner, shop.Id, id, 1)).StatusCode);
        }
    }

    internal static async Task<string> AuditAsync(TenancyApp app)
    {
        await using var connection = new NpgsqlConnection(app.Logins.Migrator);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("select coalesce(string_agg(action, ','), '') from pos.staff_changes", connection);
        return (string)(await command.ExecuteScalarAsync(Ct))!;
    }
}
