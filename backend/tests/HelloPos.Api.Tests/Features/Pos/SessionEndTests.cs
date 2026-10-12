using System.Net;
using System.Text.Json.Nodes;
using HelloPos.Api.Tests.Features.Staff;
using HelloPos.Api.Tests.Infrastructure.Database;
using static HelloPos.Api.Tests.Features.Owners.OwnerApi;
using static HelloPos.Api.Tests.Features.Pos.PosApi;
using static HelloPos.Api.Tests.Features.Pos.SignInTests;

namespace HelloPos.Api.Tests.Features.Pos;

/// <summary>US3: every FR-008 event ends the session at the next request (SC-004); changing one's own PIN.</summary>
[Collection(PostgresTests.Name)]
public sealed class SessionEndTests(PostgresFixture postgres)
{
    private const string Pin = "4826";
    private const string NewPin = "5938";

    [Fact]
    public async Task DeactivatingThePerson_EndsTheirSession()
    {
        await using var shop = await ShopWithCashierAsync(postgres);
        using var device = DeviceClient(shop.App, shop.Device.Credential);
        var (access, refresh) = await SignedInAsync(device, "ravi", Pin);
        using var session = SessionClient(shop.App, access);
        var ravi = await StaffIdAsync(session);

        using (var deactivated = await StaffApi.DeactivateAsync(shop.Owner, shop.Id, ravi, await StaffApi.VersionAsync(shop.Owner, shop.Id, ravi)))
        {
            Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await PosMeAsync(session)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PosRefreshAsync(device, refresh)).StatusCode);
    }

    [Fact]
    public async Task APinResetByTheOwner_EndsSessionsStartedBeforeIt()
    {
        await using var shop = await ShopWithCashierAsync(postgres);
        using var device = DeviceClient(shop.App, shop.Device.Credential);
        var (access, refresh) = await SignedInAsync(device, "ravi", Pin);
        using var session = SessionClient(shop.App, access);
        var ravi = await StaffIdAsync(session);
        shop.App.Clock.Advance(TimeSpan.FromSeconds(1));

        using (var reset = await StaffApi.SetPinAsync(shop.Owner, shop.Id, ravi, "ravi", NewPin, await StaffApi.VersionAsync(shop.Owner, shop.Id, ravi)))
        {
            Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await PosMeAsync(session)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PosRefreshAsync(device, refresh)).StatusCode);
        var (fresh, _) = await SignedInAsync(device, "ravi", NewPin);
        using var after = SessionClient(shop.App, fresh);
        Assert.Equal(HttpStatusCode.OK, (await PosMeAsync(after)).StatusCode);
    }

    [Fact]
    public async Task RevokingTheDevice_EndsItsSessions()
    {
        await using var shop = await ShopWithCashierAsync(postgres);
        using var device = DeviceClient(shop.App, shop.Device.Credential);
        var (access, _) = await SignedInAsync(device, "ravi", Pin);
        using var session = SessionClient(shop.App, access);

        using (var revoked = await RevokeAsync(shop.Owner, shop.Id, shop.Device.DeviceId, 1))
        {
            Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await PosMeAsync(session)).StatusCode);
    }

    [Fact]
    public async Task SessionsEnd_TwelveHoursAfterSignIn_EvenWhenRefreshed()
    {
        await using var shop = await ShopWithCashierAsync(postgres);
        using var device = DeviceClient(shop.App, shop.Device.Credential);
        var (_, refresh) = await SignedInAsync(device, "ravi", Pin);

        for (var i = 0; i < 4; i++)
        {
            shop.App.Clock.Advance(TimeSpan.FromHours(2.9));
            using var rotated = await PosRefreshAsync(device, refresh);
            Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
            refresh = (string)JsonNode.Parse(await rotated.Content.ReadAsStringAsync(Ct))!["refreshToken"]!;
        }

        shop.App.Clock.Advance(TimeSpan.FromHours(0.5)); // 12.1 hours after sign-in

        Assert.Equal(HttpStatusCode.Unauthorized, (await PosRefreshAsync(device, refresh)).StatusCode);
    }

    [Fact]
    public async Task OwnPin_WrongCurrentOrWeakNew_IsRefused()
    {
        await using var shop = await ShopWithCashierAsync(postgres);
        using var device = DeviceClient(shop.App, shop.Device.Credential);
        var (access, _) = await SignedInAsync(device, "ravi", Pin);
        using var session = SessionClient(shop.App, access);

        var wrong = JsonNode.Parse(await AssertContractAsync(await PosChangePinAsync(session, "9071", NewPin), HttpStatusCode.BadRequest, "posChangeOwnPin"))!;
        Assert.NotNull(wrong["errors"]!["currentPin"]);
        var weak = JsonNode.Parse(await AssertContractAsync(await PosChangePinAsync(session, Pin, "1111"), HttpStatusCode.BadRequest, "posChangeOwnPin"))!;
        Assert.NotNull(weak["errors"]!["newPin"]);

        Assert.Equal(HttpStatusCode.OK, (await PosMeAsync(session)).StatusCode);
        Assert.Contains("sign_in_failed", await AuditAsync(shop.App));
    }

    [Fact]
    public async Task OwnPin_WrongCurrentFiveTimes_LocksThePerson()
    {
        await using var shop = await ShopWithCashierAsync(postgres);
        using var device = DeviceClient(shop.App, shop.Device.Credential);
        var (access, _) = await SignedInAsync(device, "ravi", Pin);
        using var session = SessionClient(shop.App, access);

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await PosChangePinAsync(session, "9071", NewPin)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await PosChangePinAsync(session, Pin, NewPin)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PosSignInAsync(device, "ravi", Pin)).StatusCode);
    }

    [Fact]
    public async Task OwnPin_Change_ReturnsNewTokens_AndEndsTheOtherSessions()
    {
        await using var shop = await ShopWithCashierAsync(postgres);
        using var other = await RegisterDeviceAsync(shop.App, shop.Owner, shop.Id, "Back till");
        using var device = DeviceClient(shop.App, shop.Device.Credential);
        using var otherDevice = DeviceClient(shop.App, other.Credential);
        var (access, refresh) = await SignedInAsync(device, "ravi", Pin);
        var (otherAccess, _) = await SignedInAsync(otherDevice, "ravi", Pin);
        using var session = SessionClient(shop.App, access);
        using var otherSession = SessionClient(shop.App, otherAccess);

        var tokens = JsonNode.Parse(await AssertContractAsync(await PosChangePinAsync(session, Pin, NewPin), HttpStatusCode.OK, "posChangeOwnPin"))!;

        using var renewed = SessionClient(shop.App, (string)tokens["accessToken"]!);
        Assert.Equal(HttpStatusCode.OK, (await PosMeAsync(renewed)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PosRefreshAsync(device, (string)tokens["refreshToken"]!)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PosMeAsync(session)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PosRefreshAsync(device, refresh)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PosMeAsync(otherSession)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PosSignInAsync(otherDevice, "ravi", Pin)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PosSignInAsync(otherDevice, "ravi", NewPin)).StatusCode);
        Assert.Contains("pin_changed", await AuditAsync(shop.App));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<Guid> StaffIdAsync(HttpClient session) =>
        Guid.Parse((string)JsonNode.Parse(await (await PosMeAsync(session)).Content.ReadAsStringAsync(Ct))!["staffId"]!);
}
