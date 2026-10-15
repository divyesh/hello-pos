using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using HelloPos.Api.Tests.Infrastructure.Database;
using HelloPos.Kernel.Staff;
using static HelloPos.Api.Tests.Features.Catalog.CatalogApi;
using static HelloPos.Api.Tests.Features.Owners.OwnerApi;
using static HelloPos.Api.Tests.Features.BusinessSettings.SettingsApi;

namespace HelloPos.Api.Tests.Features.BusinessSettings;

/// <summary>Feature #12 US3: areas and tables (FR-010–FR-013; owners and managers).</summary>
[Collection(PostgresTests.Name)]
public sealed class TableTests(PostgresFixture postgres)
{
    [Fact]
    public async Task OwnersAndManagers_TurnUsesTablesOnAndOff()
    {
        await using var shop = await ShopAsync(postgres);

        var on = await JsonAsync(await AssertContractAsync(await UpdateSellingAsync(shop.Manager, shop.Id,
            Uses(await JsonAsync(await GetSellingAsync(shop.Manager, shop.Id)), true)), HttpStatusCode.OK, "updateSellingSettings"));
        var off = await JsonAsync(await UpdateSellingAsync(shop.Owner, shop.Id, Uses(on, false)));

        Assert.True((bool)on["usesTables"]!);
        Assert.False((bool)off["usesTables"]!);
        Assert.Equal(["selling:updated", "selling:updated"], await SettingsHistoryAsync(shop.App));
    }

    [Fact]
    public async Task Areas_AreCreatedRenamedAndReordered()
    {
        await using var shop = await ShopAsync(postgres);

        var hall = await JsonAsync(await AssertContractAsync(await CreateAreaAsync(shop.Manager, shop.Id, "Main hall"), HttpStatusCode.Created, "createTableArea"));
        var (patio, _) = await AreaAsync(shop.Owner, shop.Id, "Patio");
        var (bar, barVersion) = await AreaAsync(shop.Owner, shop.Id, "Bar");
        var renamed = await JsonAsync(await AssertContractAsync(
            await UpdateAreaAsync(shop.Manager, shop.Id, bar, "Bar counter", 0, barVersion), HttpStatusCode.OK, "updateTableArea"));

        Assert.Equal((0, true, 1), ((int)hall["position"]!, (bool)hall["active"]!, (int)hall["version"]!));
        Assert.Empty(hall["tables"]!.AsArray());
        Assert.Equal(("Bar counter", 0, 2), ((string)renamed["name"]!, (int)renamed["position"]!, (int)renamed["version"]!));
        var areas = (await JsonAsync(await AssertContractAsync(await ListAreasAsync(shop.Manager, shop.Id), HttpStatusCode.OK, "listTableAreas"))).AsArray();
        Assert.Equal(["Bar counter", "Main hall", "Patio"], areas.Select(a => (string)a!["name"]!));
        Assert.Equal([0, 1, 2], areas.Select(a => (int)a!["position"]!));
        Assert.Equal(patio, Guid.Parse((string)areas[2]!["id"]!));
        Assert.Equal(["tableArea:created", "tableArea:created", "tableArea:created", "tableArea:updated"], await SettingsHistoryAsync(shop.App));
    }

    [Fact]
    public async Task AreaNames_AreUniqueIgnoringCase_AndChecked()
    {
        await using var shop = await ShopAsync(postgres);
        await AreaAsync(shop.Owner, shop.Id, "Patio");

        var duplicate = await AssertContractAsync(await CreateAreaAsync(shop.Owner, shop.Id, " PATIO "), HttpStatusCode.BadRequest, "createTableArea");
        var empty = await CreateAreaAsync(shop.Owner, shop.Id, " ");
        var tooLong = await CreateAreaAsync(shop.Owner, shop.Id, new string('a', 41));

        Assert.Contains("name", ErrorFields(duplicate));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [Fact]
    public async Task Tables_AreCreatedWithDefaultSeats_AndInOrder()
    {
        await using var shop = await ShopAsync(postgres);
        var (hall, _) = await AreaAsync(shop.Owner, shop.Id, "Main hall");

        var first = await JsonAsync(await AssertContractAsync(await CreateTableAsync(shop.Manager, shop.Id, hall, "07"), HttpStatusCode.Created, "createTable"));
        var second = await JsonAsync(await CreateTableAsync(shop.Owner, shop.Id, hall, "7", seats: 2));

        Assert.Equal(("07", 4, 0, true), ((string)first["name"]!, (int)first["seats"]!, (int)first["position"]!, (bool)first["active"]!));
        Assert.Equal(("7", 2, 1), ((string)second["name"]!, (int)second["seats"]!, (int)second["position"]!));
        var area = (await JsonAsync(await ListAreasAsync(shop.Owner, shop.Id)))[0]!;
        Assert.Equal(["07", "7"], area["tables"]!.AsArray().Select(t => (string)t!["name"]!));
        Assert.Equal(["tableArea:created", "table:created", "table:created"], await SettingsHistoryAsync(shop.App));
    }

    [Fact]
    public async Task TableNames_AreUniqueInTheBusinessIgnoringCase_AndFieldsAreChecked()
    {
        await using var shop = await ShopAsync(postgres);
        var (hall, _) = await AreaAsync(shop.Owner, shop.Id, "Main hall");
        var (patio, _) = await AreaAsync(shop.Owner, shop.Id, "Patio");
        await TableAsync(shop.Owner, shop.Id, hall, "T1");

        var duplicate = await AssertContractAsync(await CreateTableAsync(shop.Owner, shop.Id, patio, "t1"), HttpStatusCode.BadRequest, "createTable");
        var seats = await CreateTableAsync(shop.Owner, shop.Id, patio, "P1", seats: 51);
        var tooLong = await CreateTableAsync(shop.Owner, shop.Id, patio, new string('x', 21));
        var noArea = await CreateTableAsync(shop.Owner, shop.Id, Guid.CreateVersion7(), "P2");

        Assert.Contains("name", ErrorFields(duplicate));
        Assert.Contains("seats", ErrorFields(await seats.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, noArea.StatusCode);
    }

    [Fact]
    public async Task ATable_IsRenamedMovedResizedAndReordered()
    {
        await using var shop = await ShopAsync(postgres);
        var (hall, _) = await AreaAsync(shop.Owner, shop.Id, "Main hall");
        var (patio, _) = await AreaAsync(shop.Owner, shop.Id, "Patio");
        await CreateTablesAsync(shop.Owner, shop.Id, hall, 1, 3);
        var (p1, _) = await TableAsync(shop.Owner, shop.Id, patio, "P1");
        var hallTables = (await JsonAsync(await ListAreasAsync(shop.Owner, shop.Id)))[0]!["tables"]!.AsArray();
        var two = Guid.Parse((string)hallTables[1]!["id"]!);
        var twoVersion = (int)hallTables[1]!["version"]!;

        var moved = await JsonAsync(await AssertContractAsync(
            await UpdateTableAsync(shop.Manager, shop.Id, two, patio, "Patio 2", 6, 0, twoVersion), HttpStatusCode.OK, "updateTable"));
        var stale = await UpdateTableAsync(shop.Owner, shop.Id, two, patio, "Patio 2b", 6, 0, twoVersion);

        Assert.Equal((patio, "Patio 2", 6, 0), (Guid.Parse((string)moved["areaId"]!), (string)moved["name"]!, (int)moved["seats"]!, (int)moved["position"]!));
        await AssertContractAsync(stale, HttpStatusCode.Conflict, "updateTable");
        var areas = (await JsonAsync(await ListAreasAsync(shop.Owner, shop.Id))).AsArray();
        Assert.Equal(["1", "3"], areas[0]!["tables"]!.AsArray().Select(t => (string)t!["name"]!));
        Assert.Equal([0, 1], areas[0]!["tables"]!.AsArray().Select(t => (int)t!["position"]!));
        Assert.Equal(["Patio 2", "P1"], areas[1]!["tables"]!.AsArray().Select(t => (string)t!["name"]!));
        Assert.Equal(p1, Guid.Parse((string)areas[1]!["tables"]![1]!["id"]!));
        Assert.Equal("table:updated", (await SettingsHistoryAsync(shop.App))[^1]);
        Assert.Equal(HttpStatusCode.NotFound, (await UpdateTableAsync(shop.Owner, shop.Id, Guid.CreateVersion7(), patio, "X", 4, 0, 1)).StatusCode);
    }

    [Fact]
    public async Task BulkAdd_CreatesARangeWithPrefixAndSuffix_ContinuingThePositions()
    {
        await using var shop = await ShopAsync(postgres);
        var (hall, _) = await AreaAsync(shop.Owner, shop.Id, "Main hall");
        await TableAsync(shop.Owner, shop.Id, hall, "Bar stool");

        var created = (await JsonAsync(await AssertContractAsync(
            await CreateTablesAsync(shop.Manager, shop.Id, hall, 1, 20, prefix: "T", suffix: "A", seats: 6), HttpStatusCode.Created, "createTables"))).AsArray();
        var plain = (await JsonAsync(await CreateTablesAsync(shop.Owner, shop.Id, hall, 1, 3))).AsArray();

        Assert.Equal(Enumerable.Range(1, 20).Select(n => $"T{n}A"), created.Select(t => (string)t!["name"]!));
        Assert.Equal(Enumerable.Range(1, 20), created.Select(t => (int)t!["position"]!));
        Assert.All(created, t => Assert.Equal(6, (int)t!["seats"]!));
        Assert.Equal(["1", "2", "3"], plain.Select(t => (string)t!["name"]!));
        Assert.All(plain, t => Assert.Equal(4, (int)t!["seats"]!));
        Assert.Equal(24, (await JsonAsync(await ListAreasAsync(shop.Owner, shop.Id)))[0]!["tables"]!.AsArray().Count);
        Assert.Equal(["tableArea:created", "table:created", "table:bulkCreated", "table:bulkCreated"], await SettingsHistoryAsync(shop.App));
    }

    [Fact]
    public async Task BulkAdd_WithTakenNames_CreatesNothing_AndListsTheClashes()
    {
        await using var shop = await ShopAsync(postgres);
        var (hall, _) = await AreaAsync(shop.Owner, shop.Id, "Main hall");
        var (patio, _) = await AreaAsync(shop.Owner, shop.Id, "Patio");
        await TableAsync(shop.Owner, shop.Id, patio, "4");
        await TableAsync(shop.Owner, shop.Id, patio, "17");

        var clash = await AssertContractAsync(await CreateTablesAsync(shop.Owner, shop.Id, hall, 1, 20), HttpStatusCode.BadRequest, "createTables");

        var message = string.Join(" ", JsonNode.Parse(clash)!["errors"]!.AsObject().SelectMany(e => e.Value!.AsArray().Select(m => (string)m!)));
        Assert.Contains("4", message, StringComparison.Ordinal);
        Assert.Contains("17", message, StringComparison.Ordinal);
        Assert.Empty((await JsonAsync(await ListAreasAsync(shop.Owner, shop.Id)))[0]!["tables"]!.AsArray());
        Assert.DoesNotContain("table:bulkCreated", await SettingsHistoryAsync(shop.App));
    }

    [Fact]
    public async Task BulkAdd_OfMoreThan200_OrABadRange_IsRefused()
    {
        await using var shop = await ShopAsync(postgres);
        var (hall, _) = await AreaAsync(shop.Owner, shop.Id, "Main hall");

        var tooMany = await AssertContractAsync(await CreateTablesAsync(shop.Owner, shop.Id, hall, 1, 201), HttpStatusCode.BadRequest, "createTables");
        var backwards = await CreateTablesAsync(shop.Owner, shop.Id, hall, 5, 4);
        var zero = await CreateTablesAsync(shop.Owner, shop.Id, hall, 0, 4);
        var unknownArea = await CreateTablesAsync(shop.Owner, shop.Id, Guid.CreateVersion7(), 1, 4);

        Assert.NotEmpty(ErrorFields(tooMany));
        Assert.Equal(HttpStatusCode.BadRequest, backwards.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownArea.StatusCode);
    }

    [Fact]
    public async Task BulkAdd_Of200Tables_TakesUnderTwoSeconds()
    {
        await using var shop = await ShopAsync(postgres);
        var (hall, _) = await AreaAsync(shop.Owner, shop.Id, "Main hall");

        var watch = Stopwatch.StartNew();
        var created = await CreateTablesAsync(shop.Owner, shop.Id, hall, 1, 200);
        watch.Stop();

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(200, (await JsonAsync(created)).AsArray().Count);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"took {watch.Elapsed}");
    }

    [Fact]
    public async Task Tables_AreDeactivatedAndReactivated_AndAnAreaWithActiveTablesStays()
    {
        await using var shop = await ShopAsync(postgres);
        var (hall, hallVersion) = await AreaAsync(shop.Owner, shop.Id, "Main hall");
        var (table, version) = await TableAsync(shop.Owner, shop.Id, hall, "1");

        await AssertContractAsync(await SetAreaActiveAsync(shop.Owner, shop.Id, hall, false, hallVersion), HttpStatusCode.Conflict, "deactivateTableArea");
        var off = await JsonAsync(await AssertContractAsync(await SetTableActiveAsync(shop.Manager, shop.Id, table, false, version), HttpStatusCode.OK, "deactivateTable"));
        var areaOff = await JsonAsync(await AssertContractAsync(await SetAreaActiveAsync(shop.Owner, shop.Id, hall, false, hallVersion), HttpStatusCode.OK, "deactivateTableArea"));
        var tableInInactiveArea = await SetTableActiveAsync(shop.Owner, shop.Id, table, true, version + 1);
        var newTableInInactiveArea = await CreateTableAsync(shop.Owner, shop.Id, hall, "2");
        var areaOn = await JsonAsync(await AssertContractAsync(await SetAreaActiveAsync(shop.Owner, shop.Id, hall, true, hallVersion + 1), HttpStatusCode.OK, "reactivateTableArea"));
        var on = await JsonAsync(await AssertContractAsync(await SetTableActiveAsync(shop.Owner, shop.Id, table, true, version + 1), HttpStatusCode.OK, "reactivateTable"));

        Assert.False((bool)off["active"]!);
        Assert.False((bool)areaOff["active"]!);
        Assert.Equal(HttpStatusCode.Conflict, tableInInactiveArea.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, newTableInInactiveArea.StatusCode);
        Assert.True((bool)areaOn["active"]!);
        Assert.True((bool)on["active"]!);
        Assert.Equal(["tableArea:created", "table:created", "table:deactivated", "tableArea:deactivated", "tableArea:reactivated", "table:reactivated"],
            await SettingsHistoryAsync(shop.App));
        await AssertContractAsync(await SetTableActiveAsync(shop.Owner, shop.Id, table, false, 1), HttpStatusCode.Conflict, "deactivateTable");
    }

    [Fact]
    public async Task StaleAreaVersions_Conflict_AndUnknownAreasAreNotFound()
    {
        await using var shop = await ShopAsync(postgres);
        var (hall, version) = await AreaAsync(shop.Owner, shop.Id, "Main hall");
        await UpdateAreaAsync(shop.Owner, shop.Id, hall, "Hall", 0, version);

        await AssertContractAsync(await UpdateAreaAsync(shop.Owner, shop.Id, hall, "Main", 0, version), HttpStatusCode.Conflict, "updateTableArea");
        await AssertContractAsync(await UpdateAreaAsync(shop.Owner, shop.Id, Guid.CreateVersion7(), "Main", 0, 1), HttpStatusCode.NotFound, "updateTableArea");
    }

    [Fact]
    public async Task Cashiers_CannotReachTables()
    {
        await using var shop = await ShopAsync(postgres);

        using var cashier = await shop.App.SignUpVerifiedOwnerAsync("cashier@example.com");
        await shop.App.AddStaffAsync(shop.Id, StaffRole.Cashier, await HelloPos.Api.Tests.Tenancy.TenancyApp.AccountIdAsync(cashier), "Cash");

        Assert.Equal(HttpStatusCode.Unauthorized, (await ListAreasAsync(cashier, shop.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await CreateAreaAsync(cashier, shop.Id, "Patio")).StatusCode);
    }

    private static JsonObject Uses(JsonNode selling, bool usesTables)
    {
        var body = SellingBody(selling);
        body["usesTables"] = usesTables;
        return body;
    }
}
