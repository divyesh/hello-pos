using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using HelloPos.Api.Features.Staff;
using HelloPos.Api.Tests.Features.Staff;
using HelloPos.Api.Tests.Infrastructure.Database;
using Microsoft.AspNetCore.Identity;
using static HelloPos.Api.Tests.Features.Businesses.OpenAndEditTests;
using static HelloPos.Api.Tests.Features.Owners.OwnerApi;
using static HelloPos.Api.Tests.Features.Pos.PosApi;
using static HelloPos.Api.Tests.Features.Pos.SignInTests;

namespace HelloPos.Api.Tests.Features.Pos;

/// <summary>US3 offline sign-in (clarification Q1, research R4): the bundle, offline sessions, attempt reports.</summary>
[Collection(PostgresTests.Name)]
public sealed class OfflineAccessTests(PostgresFixture postgres)
{
    private const string Pin = "4826";

    [Fact]
    public async Task TheBundle_HoldsOnlyActiveStaffWithAPin_OfThisBusiness_AsHashes()
    {
        await using var shop = await ShopWithCashierAsync(postgres);
        await AddAsync(shop, "Mina", "manager", "mina", "583920");
        var gone = await AddAsync(shop, "Gone", "cashier", "gone", Pin);
        Assert.Equal(HttpStatusCode.OK, (await StaffApi.DeactivateAsync(shop.Owner, shop.Id, gone, 1)).StatusCode);
        var other = await CreateIdAsync(shop.Owner, "Other shop");
        await OpenAsync(shop.Owner, other);
        await StaffApi.AddAsync(shop.Owner, other, StaffApi.NewStaff("Zed", "cashier", "zed", Pin));
        using var device = DeviceClient(shop.App, shop.Device.Credential);

        await AssertContractAsync(await OfflineAccessAsync(device), HttpStatusCode.OK, "posOfflineAccess");
        var bundle = await OpenOfflineBundleAsync(device, shop.Device.Key);

        Assert.Equal(shop.Id, Guid.Parse((string)bundle["businessId"]!));
        var staff = bundle["staff"]!.AsArray();
        Assert.Equal(["mina", "ravi"], staff.Select(s => (string)s!["username"]!));
        var ravi = staff.Single(s => (string)s!["username"]! == "ravi")!;
        Assert.Equal("cashier", (string?)ravi["role"]);
        Assert.NotNull(ravi["pinChangedAt"]);
        var hash = (string)ravi["pinHash"]!;
        Assert.NotEqual(PasswordVerificationResult.Failed,
            new PasswordHasher<StaffMember>().VerifyHashedPassword(new StaffMember { DisplayName = "x" }, hash, Pin));
        Assert.DoesNotContain(Pin, bundle.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain("583920", bundle.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnlyThisDevicesKey_OpensTheBundle()
    {
        await using var shop = await ShopWithCashierAsync(postgres);
        using var other = await RegisterDeviceAsync(shop.App, shop.Owner, shop.Id, "Back till");
        using var device = DeviceClient(shop.App, shop.Device.Credential);

        await Assert.ThrowsAnyAsync<CryptographicException>(() => OpenOfflineBundleAsync(device, other.Key));
    }

    [Fact]
    public async Task AnOfflineSignIn_BecomesASession_WhileThePersonAndPinAreUnchanged()
    {
        await using var shop = await ShopWithCashierAsync(postgres);
        using var device = DeviceClient(shop.App, shop.Device.Credential);
        var (ravi, pinChangedAt) = await RaviAsync(device, shop.Device.Key);
        var signedInAt = shop.App.Clock.GetUtcNow();
        shop.App.Clock.Advance(TimeSpan.FromMinutes(20));

        var tokens = JsonNode.Parse(await AssertContractAsync(
            await SessionFromOfflineAsync(device, ravi, signedInAt, pinChangedAt), HttpStatusCode.OK, "posSessionFromOffline"))!;

        using var session = SessionClient(shop.App, (string)tokens["accessToken"]!);
        Assert.Equal(HttpStatusCode.OK, (await PosMeAsync(session)).StatusCode);
        Assert.Equal(signedInAt + TimeSpan.FromHours(12), DateTimeOffset.Parse((string)tokens["sessionExpiresAt"]!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Contains("signed_in_offline", await AuditAsync(shop.App));
    }

    [Fact]
    public async Task AnOfflineSignIn_IsRefused_AfterDeactivation_APinChange_OrTwelveHours()
    {
        await using var shop = await ShopWithCashierAsync(postgres);
        using var device = DeviceClient(shop.App, shop.Device.Credential);
        var (ravi, pinChangedAt) = await RaviAsync(device, shop.Device.Key);
        var signedInAt = shop.App.Clock.GetUtcNow();

        shop.App.Clock.Advance(TimeSpan.FromSeconds(1));
        using (var reset = await StaffApi.SetPinAsync(shop.Owner, shop.Id, ravi, "ravi", "5938", 1))
        {
            Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        }

        await AssertContractAsync(await SessionFromOfflineAsync(device, ravi, signedInAt, pinChangedAt), HttpStatusCode.Unauthorized, "posSessionFromOffline");

        var (_, changedAt) = await RaviAsync(device, shop.Device.Key);
        Assert.Equal(HttpStatusCode.OK, (await SessionFromOfflineAsync(device, ravi, signedInAt, changedAt)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await SessionFromOfflineAsync(device, ravi, signedInAt - TimeSpan.FromHours(13), changedAt)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await StaffApi.DeactivateAsync(shop.Owner, shop.Id, ravi, 2)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SessionFromOfflineAsync(device, ravi, signedInAt, changedAt)).StatusCode);
    }

    [Fact]
    public async Task OfflineFailures_CountTowardTheLockout()
    {
        await using var shop = await ShopWithCashierAsync(postgres);
        using var device = DeviceClient(shop.App, shop.Device.Credential);
        var at = shop.App.Clock.GetUtcNow();

        await AssertContractAsync(
            await ReportAttemptsAsync(device, [.. Enumerable.Range(0, 5).Select(i => ("RAVI", false, at.AddSeconds(i)))]),
            HttpStatusCode.NoContent, "posReportAttempts");

        Assert.Equal(HttpStatusCode.Unauthorized, (await PosSignInAsync(device, "ravi", Pin)).StatusCode);
        Assert.Contains("locked_out", await AuditAsync(shop.App));
    }

    [Fact]
    public async Task AnOfflineSuccess_ResetsTheCount()
    {
        await using var shop = await ShopWithCashierAsync(postgres);
        using var device = DeviceClient(shop.App, shop.Device.Credential);
        var at = shop.App.Clock.GetUtcNow();

        using var report = await ReportAttemptsAsync(device,
            ("ravi", false, at), ("ravi", false, at.AddSeconds(1)), ("ravi", false, at.AddSeconds(2)), ("ravi", false, at.AddSeconds(3)),
            ("ravi", true, at.AddSeconds(4)), ("ravi", false, at.AddSeconds(5)), ("nobody", false, at));
        Assert.Equal(HttpStatusCode.NoContent, report.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await PosSignInAsync(device, "ravi", Pin)).StatusCode);
    }

    [Fact]
    public async Task TooManyAttemptsInOneReport_AreRefused()
    {
        await using var shop = await ShopWithCashierAsync(postgres);
        using var device = DeviceClient(shop.App, shop.Device.Credential);
        var at = shop.App.Clock.GetUtcNow();

        using var report = await ReportAttemptsAsync(device, [.. Enumerable.Range(0, 101).Select(i => ("ravi", false, at))]);

        Assert.Equal(HttpStatusCode.BadRequest, report.StatusCode);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(Guid StaffId, DateTimeOffset? PinChangedAt)> RaviAsync(HttpClient device, RSA key)
    {
        var ravi = (await OpenOfflineBundleAsync(device, key))["staff"]!.AsArray().Single(s => (string)s!["username"]! == "ravi")!;
        return (Guid.Parse((string)ravi["staffId"]!), ravi["pinChangedAt"] is { } changed
            ? DateTimeOffset.Parse((string)changed!, System.Globalization.CultureInfo.InvariantCulture)
            : null);
    }

    private static async Task<Guid> AddAsync(Shop shop, string name, string role, string username, string pin)
    {
        using var added = await StaffApi.AddAsync(shop.Owner, shop.Id, StaffApi.NewStaff(name, role, username, pin));
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        return Guid.Parse((string)JsonNode.Parse(await added.Content.ReadAsStringAsync(Ct))!["id"]!);
    }
}
