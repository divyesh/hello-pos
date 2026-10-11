using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using HelloPos.Api.Tests.Features.Pos;
using HelloPos.Api.Tests.Features.Staff;
using HelloPos.Api.Tests.Infrastructure.Database;
using HelloPos.Api.Tests.Tenancy;
using HelloPos.Kernel.Staff;
using Npgsql;
using static HelloPos.Api.Tests.Features.Businesses.OpenAndEditTests;
using static HelloPos.Api.Tests.Features.Owners.OwnerApi;
using static HelloPos.Api.Tests.Features.Pos.PosApi;

namespace HelloPos.Api.Tests.Features.Devices;

/// <summary>US1 register a POS device: FR-001–FR-003, FR-014; research R1.</summary>
[Collection(PostgresTests.Name)]
public sealed class DeviceTests(PostgresFixture postgres)
{
    [Fact]
    public async Task OwnerCreatesACode_TheDevicePairs_AndIsListedActive()
    {
        var (app, owner, shop) = await ShopAsync();
        await using var _ = app;
        using var __ = owner;

        var pairing = JsonNode.Parse(await AssertContractAsync(await CreatePairingAsync(owner, shop), HttpStatusCode.Created, "createDevicePairing"))!;
        var code = (string)pairing["code"]!;
        Assert.Matches("^[2-9A-HJKMNP-Z]{4}-[2-9A-HJKMNP-Z]{4}$", code);
        var expiresIn = DateTimeOffset.Parse((string)pairing["expiresAt"]!, System.Globalization.CultureInfo.InvariantCulture) - app.Clock.GetUtcNow();
        Assert.InRange(expiresIn.TotalSeconds, 298, 300);

        using var key = RSA.Create(3072);
        using var device = app.OwnerClient(withClientHeader: false);
        var paired = JsonNode.Parse(await AssertContractAsync(await PairAsync(device, code.ToLowerInvariant().Replace("-", string.Empty, StringComparison.Ordinal), "Front till", PublicKeyOf(key)), HttpStatusCode.OK, "posPair"))!;
        Assert.Equal("Shop", (string?)paired["businessName"]);
        Assert.StartsWith("dev_", (string)paired["deviceCredential"]!, StringComparison.Ordinal);

        var listed = Assert.Single(JsonNode.Parse(await AssertContractAsync(await ListDevicesAsync(owner, shop), HttpStatusCode.OK, "listDevices"))!.AsArray())!;
        Assert.Equal("Front till", (string?)listed["name"]);
        Assert.True((bool?)listed["active"]);
        Assert.Equal("Owner", (string?)listed["registeredBy"]);
        Assert.Contains("device_registered", await AuditAsync(app));
    }

    [Fact]
    public async Task ACode_WorksOnce_AndExpiresAfterFiveMinutes()
    {
        var (app, owner, shop) = await ShopAsync();
        await using var _ = app;
        using var __ = owner;
        using var key = RSA.Create(3072);
        using var device = app.OwnerClient(withClientHeader: false);
        var used = await CodeAsync(owner, shop);
        var expired = await CodeAsync(owner, shop);

        Assert.Equal(HttpStatusCode.OK, (await PairAsync(device, used, "Till 1", PublicKeyOf(key))).StatusCode);
        await AssertContractAsync(await PairAsync(device, used, "Till 2", PublicKeyOf(key)), HttpStatusCode.NotFound, "posPair");
        app.Clock.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        Assert.Equal(HttpStatusCode.NotFound, (await PairAsync(device, expired, "Till 3", PublicKeyOf(key))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PairAsync(device, "ABCD-EFGH", "Till 4", PublicKeyOf(key))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PairAsync(device, "0O1I-LLLL", "Till 5", PublicKeyOf(key))).StatusCode);
    }

    [Theory]
    [InlineData("deviceName", "")]
    [InlineData("publicKey", "not-a-key")]
    [InlineData("publicKey", "small")]
    public async Task BadPairRequests_AreRejectedByField_AndKeepTheCode(string field, string value)
    {
        var (app, owner, shop) = await ShopAsync();
        await using var _ = app;
        using var __ = owner;
        using var key = RSA.Create(3072);
        using var small = RSA.Create(1024);
        using var device = app.OwnerClient(withClientHeader: false);
        var code = await CodeAsync(owner, shop);
        var publicKey = field != "publicKey" ? PublicKeyOf(key) : value == "small" ? PublicKeyOf(small) : value;

        var body = await AssertContractAsync(await PairAsync(device, code, field == "deviceName" ? value : "Till", publicKey), HttpStatusCode.BadRequest, "posPair");

        Assert.Contains(field, ErrorFields(body));
        Assert.Equal(HttpStatusCode.OK, (await PairAsync(device, code, "Till", PublicKeyOf(key))).StatusCode);
    }

    [Fact]
    public async Task Manager_RegistersDevices_CashierAccountCannot()
    {
        var (app, owner, shop) = await ShopAsync();
        await using var _ = app;
        using var __ = owner;
        var (manager, _) = await app.SignedInStaffClientAsync("manager@example.com", shop, StaffRole.Manager);
        using var ___ = manager;

        using (var device = await RegisterDeviceAsync(app, manager, shop))
        {
            Assert.Equal("manager", (string?)Assert.Single(JsonNode.Parse(await (await ListDevicesAsync(owner, shop)).Content.ReadAsStringAsync(Ct))!.AsArray())!["registeredBy"]);
        }

        using var cashier = await app.SignUpVerifiedOwnerAsync("cashier@example.com");
        await app.AddStaffAsync(shop, StaffRole.Cashier, await TenancyApp.AccountIdAsync(cashier));
        Assert.Equal(HttpStatusCode.Unauthorized, (await CreatePairingAsync(cashier, shop)).StatusCode);
    }

    [Fact]
    public async Task Rename_IsVersioned_Revoke_StopsTheCredentialAtOnce()
    {
        var (app, owner, shop) = await ShopAsync();
        await using var _ = app;
        using var __ = owner;
        using var device = await RegisterDeviceAsync(app, owner, shop);
        using var asDevice = DeviceClient(app, device.Credential);
        var info = JsonNode.Parse(await AssertContractAsync(await asDevice.GetAsync(new Uri("/pos/device", UriKind.Relative), Ct), HttpStatusCode.OK, "posDevice"))!;
        Assert.Equal("Shop", (string?)info["businessName"]);

        await AssertContractAsync(await RenameAsync(owner, shop, device.DeviceId, "Back till", 1), HttpStatusCode.OK, "renameDevice");
        await AssertContractAsync(await RenameAsync(owner, shop, device.DeviceId, "Stale", 1), HttpStatusCode.Conflict, "renameDevice");
        var revoked = JsonNode.Parse(await AssertContractAsync(await RevokeAsync(owner, shop, device.DeviceId, 2), HttpStatusCode.OK, "revokeDevice"))!;

        Assert.False((bool?)revoked["active"]);
        await AssertContractAsync(await asDevice.GetAsync(new Uri("/pos/device", UriKind.Relative), Ct), HttpStatusCode.Unauthorized, "posDevice");
        Assert.Contains("device_revoked", await AuditAsync(app));
    }

    private async Task<(TenancyApp App, HttpClient Owner, Guid Shop)> ShopAsync()
    {
        var app = await TenancyApp.CreateAsync(postgres);
        var owner = await app.SignUpVerifiedOwnerAsync("owner@example.com");
        var shop = await CreateIdAsync(owner, "Shop");
        await OpenAsync(owner, shop);
        return (app, owner, shop);
    }

    private static async Task<string> CodeAsync(HttpClient owner, Guid shop)
    {
        using var response = await CreatePairingAsync(owner, shop);
        return (string)JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!["code"]!;
    }

    private static async Task<string> AuditAsync(TenancyApp app)
    {
        await using var connection = new NpgsqlConnection(app.Logins.Migrator);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("select coalesce(string_agg(action, ','), '') from pos.staff_changes", connection);
        return (string)(await command.ExecuteScalarAsync(Ct))!;
    }
}
