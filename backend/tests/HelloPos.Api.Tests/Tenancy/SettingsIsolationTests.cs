using System.Net;
using System.Text.Json.Nodes;
using HelloPos.Api.Tests.Infrastructure.Database;
using static HelloPos.Api.Tests.Features.Businesses.OpenAndEditTests;
using static HelloPos.Api.Tests.Features.BusinessSettings.SettingsApi;
using static HelloPos.Api.Tests.Features.Catalog.CatalogApi;
using static HelloPos.Api.Tests.Features.Pos.CatalogSyncTests;
using static HelloPos.Api.Tests.Features.Pos.PosApi;

namespace HelloPos.Api.Tests.Tenancy;

/// <summary>
/// Feature #12 FR-017, SC-005 (Constitution III): no business can see or change another business's payment
/// types, areas, tables, selling settings or settings history, by any operation, with the other business's
/// ids in paths or in bodies; a till receives only its own business's settings.
/// </summary>
[Collection(PostgresTests.Name)]
public sealed class SettingsIsolationTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnotherBusinesssSettings_AreInvisibleAndUnreachable()
    {
        await using var shop = await ShopAsync(postgres);
        var theirCash = Guid.Parse((string)(await JsonAsync(await ListPaymentTypesAsync(shop.Owner, shop.Id)))[0]!["id"]!);
        var (theirUpi, upiVersion) = await PaymentTypeAsync(shop.Owner, shop.Id, "Their UPI");
        var (theirArea, areaVersion) = await AreaAsync(shop.Owner, shop.Id, "Their hall");
        var (theirTable, tableVersion) = await TableAsync(shop.Owner, shop.Id, theirArea, "T1");
        var theirSelling = SellingBody(await JsonAsync(await GetSellingAsync(shop.Owner, shop.Id)));
        theirSelling["discountLimitPercent"] = "25";
        using (await UpdateSellingAsync(shop.Owner, shop.Id, theirSelling))
        {
        }

        // The same owner opens a second business: the first one's settings must vanish from every view.
        var other = await CreateIdAsync(shop.Owner, "Other");
        await OpenAsync(shop.Owner, other);
        var myCash = Guid.Parse((string)(await JsonAsync(await ListPaymentTypesAsync(shop.Owner, other)))[0]!["id"]!);
        var (myArea, _) = await AreaAsync(shop.Owner, other, "My hall");
        var (myTable, myTableVersion) = await TableAsync(shop.Owner, other, myArea, "M1");

        // Reads.
        Assert.Equal(["Cash"], (await JsonAsync(await ListPaymentTypesAsync(shop.Owner, other))).AsArray().Select(t => (string)t!["name"]!));
        var areas = (await JsonAsync(await ListAreasAsync(shop.Owner, other))).AsArray();
        Assert.Equal(["My hall"], areas.Select(a => (string)a!["name"]!));
        Assert.Equal([myTable], areas[0]!["tables"]!.AsArray().Select(t => Guid.Parse((string)t!["id"]!)));
        Assert.Equal("0", (string?)(await JsonAsync(await GetSellingAsync(shop.Owner, other)))["discountLimitPercent"]);
        var history = await ListChangesAsync(shop.Owner, other);
        Assert.DoesNotContain("Their", await history.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        foreach (var id in new[] { theirUpi, theirArea, theirTable, shop.Id })
        {
            Assert.Empty((await JsonAsync(await ListChangesAsync(shop.Owner, other, $"?entityId={id}"))).AsArray());
        }

        // Their ids in paths.
        Assert.Equal(HttpStatusCode.NotFound, (await UpdatePaymentTypeAsync(shop.Owner, other, theirUpi, "Hacked", "other", false, false, upiVersion)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SetPaymentTypeActiveAsync(shop.Owner, other, theirUpi, false, upiVersion)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SetPaymentTypeActiveAsync(shop.Owner, other, theirUpi, true, upiVersion)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await UpdateAreaAsync(shop.Owner, other, theirArea, "Hacked", 0, areaVersion)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SetAreaActiveAsync(shop.Owner, other, theirArea, false, areaVersion)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SetAreaActiveAsync(shop.Owner, other, theirArea, true, areaVersion)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await UpdateTableAsync(shop.Owner, other, theirTable, myArea, "Hacked", 2, 0, tableVersion)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SetTableActiveAsync(shop.Owner, other, theirTable, false, tableVersion)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SetTableActiveAsync(shop.Owner, other, theirTable, true, tableVersion)).StatusCode);

        // Their ids in bodies.
        Assert.Equal(HttpStatusCode.BadRequest, (await ReorderPaymentTypesAsync(shop.Owner, other, myCash, theirUpi)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ReorderPaymentTypesAsync(shop.Owner, other, theirCash)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await CreateTableAsync(shop.Owner, other, theirArea, "X1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await CreateTablesAsync(shop.Owner, other, theirArea, 1, 3)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await UpdateTableAsync(shop.Owner, other, myTable, theirArea, "M1", 4, 0, myTableVersion)).StatusCode);

        // Names are unique per business only: their names are free here.
        await PaymentTypeAsync(shop.Owner, other, "Their UPI");
        await TableAsync(shop.Owner, other, myArea, "T1");

        // Changing this business's selling settings leaves theirs alone.
        var mine = SellingBody(await JsonAsync(await GetSellingAsync(shop.Owner, other)));
        mine["discountLimitPercent"] = "5";
        using (var saved = await UpdateSellingAsync(shop.Owner, other, mine))
        {
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        }

        // And nothing of theirs changed.
        await OpenAsync(shop.Owner, shop.Id);
        var types = (await JsonAsync(await ListPaymentTypesAsync(shop.Owner, shop.Id))).AsArray();
        Assert.Equal([("Cash", true), ("Their UPI", true)], types.Select(t => ((string)t!["name"]!, (bool)t["active"]!)));
        var theirAreas = (await JsonAsync(await ListAreasAsync(shop.Owner, shop.Id))).AsArray();
        Assert.Equal([("Their hall", true)], theirAreas.Select(a => ((string)a!["name"]!, (bool)a["active"]!)));
        Assert.Equal([("T1", true)], theirAreas[0]!["tables"]!.AsArray().Select(t => ((string)t!["name"]!, (bool)t["active"]!)));
        Assert.Equal("25", (string?)(await JsonAsync(await GetSellingAsync(shop.Owner, shop.Id)))["discountLimitPercent"]);
        var theirHistory = await (await ListChangesAsync(shop.Owner, shop.Id)).Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain(other.ToString(), theirHistory, StringComparison.Ordinal);
        Assert.DoesNotContain("My hall", theirHistory, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATill_ReceivesOnlyItsBusinesssSettings()
    {
        await using var shop = await ShopAsync(postgres);
        var (ourUpi, _) = await PaymentTypeAsync(shop.Owner, shop.Id, "Our UPI");
        var (ourArea, _) = await AreaAsync(shop.Owner, shop.Id, "Our hall");
        await TableAsync(shop.Owner, shop.Id, ourArea, "O1");
        using var device = await RegisterDeviceAsync(shop.App, shop.Owner, shop.Id);
        using var till = DeviceClient(shop.App, device.Credential);
        var since = (long)JsonNode.Parse(await (await SyncAsync(till)).Content.ReadAsStringAsync(Ct))!["revision"]!;

        // Another business of the same owner gets settings after the till's read.
        var other = await CreateIdAsync(shop.Owner, "Other");
        await OpenAsync(shop.Owner, other);
        var theirCash = Guid.Parse((string)(await JsonAsync(await ListPaymentTypesAsync(shop.Owner, other)))[0]!["id"]!);
        var (theirUpi, _) = await PaymentTypeAsync(shop.Owner, other, "Their UPI");
        var (theirArea, _) = await AreaAsync(shop.Owner, other, "Their hall");
        var (theirTable, _) = await TableAsync(shop.Owner, other, theirArea, "X1");
        var theirSelling = SellingBody(await JsonAsync(await GetSellingAsync(shop.Owner, other)));
        theirSelling["discountLimitPercent"] = "33";
        theirSelling["tipsEnabled"] = true;
        theirSelling["tipPresets"] = new JsonArray("18");
        using (await UpdateSellingAsync(shop.Owner, other, theirSelling))
        {
        }

        foreach (var request in new long?[] { null, since, 0 })
        {
            using var response = await SyncAsync(till, request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var text = await response.Content.ReadAsStringAsync(Ct);
            var catalog = JsonNode.Parse(text)!;

            foreach (var id in new[] { theirCash, theirUpi, theirArea, theirTable })
            {
                Assert.DoesNotContain(id.ToString(), text, StringComparison.Ordinal);
            }

            Assert.DoesNotContain("Their", text, StringComparison.Ordinal);
            Assert.Equal(("0", false), ((string)catalog["selling"]!["discountLimitPercent"]!, (bool)catalog["selling"]!["tipsEnabled"]!));
        }

        var full = JsonNode.Parse(await (await SyncAsync(till)).Content.ReadAsStringAsync(Ct))!;
        Assert.Contains(ourUpi, full["paymentTypes"]!.AsArray().Select(t => Guid.Parse((string)t!["id"]!)));

        // And the other business's till sees only its own.
        using var theirDevice = await RegisterDeviceAsync(shop.App, shop.Owner, other, "Their till");
        using var theirTill = DeviceClient(shop.App, theirDevice.Credential);
        var theirs = JsonNode.Parse(await (await SyncAsync(theirTill)).Content.ReadAsStringAsync(Ct))!;
        Assert.Equal([theirCash, theirUpi], theirs["paymentTypes"]!.AsArray().Select(t => Guid.Parse((string)t!["id"]!)));
        Assert.Equal([theirArea], theirs["tableAreas"]!.AsArray().Select(a => Guid.Parse((string)a!["id"]!)));
        Assert.Equal([theirTable], theirs["tables"]!.AsArray().Select(t => Guid.Parse((string)t!["id"]!)));
        Assert.Equal(["18"], theirs["selling"]!["tipPresets"]!.AsArray().Select(t => (string)t!));
    }
}
