using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using HelloPos.Api.Tests.Features.Catalog;
using HelloPos.Api.Tests.Features.Staff;
using HelloPos.Api.Tests.Infrastructure.Database;
using Npgsql;
using static HelloPos.Api.Tests.Features.Catalog.CatalogApi;
using static HelloPos.Api.Tests.Features.Owners.OwnerApi;
using static HelloPos.Api.Tests.Features.Pos.PosApi;

namespace HelloPos.Api.Tests.Features.Pos;

/// <summary>Feature #13 US1: a paired till downloads its business's whole catalog (FR-001, FR-002, research R2–R5).</summary>
[Collection(PostgresTests.Name)]
public sealed class CatalogSyncTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AFullDownload_HoldsTheWholeCatalog_InTheContractShape()
    {
        await using var shop = await ShopAsync(postgres);
        using var device = await RegisterDeviceAsync(shop.App, shop.Owner, shop.Id);
        var today = Today(shop.App);
        var (gst, gstVersion) = await TaxProfileAsync(shop.Owner, shop.Id, "GST 5%");
        using (await AddVersionAsync(shop.Owner, shop.Id, gst, today.AddDays(10), Components(("CGST", "6"), ("SGST", "6")), gstVersion))
        {
        }

        var (exempt, _) = await TaxProfileAsync(shop.Owner, shop.Id, "Exempt", "exempt");
        var (drinks, _) = await CategoryAsync(shop.Owner, shop.Id, "Drinks", gst, sortOrder: 2);
        var (services, _) = await CategoryAsync(shop.Owner, shop.Id, "Services", exempt, sortOrder: 1);
        var colaBody = ProductBody("Cola", drinks, 199, "067000004568", "4011");
        colaBody["sku"] = "COLA";
        colaBody["depositPerUnit"] = 10;
        colaBody["minAge"] = 19;
        var (cola, _) = await ProductAsync(shop.Owner, shop.Id, colaBody);
        var cakeBody = ProductBody("Cake", services, null);
        cakeBody["taxProfileId"] = gst;
        var (cake, _) = await ProductAsync(shop.Owner, shop.Id, cakeBody);
        var (old, oldVersion) = await ProductAsync(shop.Owner, shop.Id, ProductBody("Old soda", drinks, 99));
        using (await SetProductActiveAsync(shop.Owner, shop.Id, old, false, oldVersion))
        {
        }

        var ending = await DiscountIdAsync(shop, cola, DiscountBody("percent", "10", today, today.AddDays(1)));
        var running = await DiscountIdAsync(shop, cola, DiscountBody("fixed", "25", today.AddDays(2), today.AddDays(20)));
        var future = await DiscountIdAsync(shop, cake, DiscountBody("percent", "5", today.AddDays(15)));
        shop.App.Clock.Advance(TimeSpan.FromDays(3));

        using var client = DeviceClient(shop.App, device.Credential);
        var catalog = JsonNode.Parse(await AssertContractAsync(await SyncAsync(client), HttpStatusCode.OK, "posCatalog"))!;

        Assert.Equal("full", (string?)catalog["mode"]);
        Assert.True((long)catalog["revision"]! > 0);
        Assert.Equal(shop.App.Clock.GetUtcNow().UtcDateTime, DateTimeOffset.Parse((string)catalog["serverTime"]!, System.Globalization.CultureInfo.InvariantCulture).UtcDateTime, TimeSpan.FromMinutes(1));
        var business = catalog["business"]!;
        Assert.Equal(("INR", "tax_inclusive", "Asia/Kolkata", false),
            ((string)business["currency"]!, (string)business["pricingMode"]!, (string)business["timeZone"]!, (bool)business["depositsTaxable"]!));

        var profiles = catalog["taxProfiles"]!.AsArray().ToDictionary(p => (string)p!["name"]!);
        var versions = profiles["GST 5%"]!["versions"]!.AsArray();
        Assert.Equal(2, versions.Count);
        Assert.Equal(["2.5", "2.5"], versions[0]!["components"]!.AsArray().Select(c => (string)c!["ratePercent"]!));
        Assert.Equal(["6", "6"], versions[1]!["components"]!.AsArray().Select(c => (string)c!["ratePercent"]!));
        Assert.Equal("exempt", (string?)profiles["Exempt"]!["kind"]);
        Assert.Empty(profiles["Exempt"]!["versions"]![0]!["components"]!.AsArray());

        Assert.Equal(["Services", "Drinks"], catalog["categories"]!.AsArray().OrderBy(c => (int)c!["sortOrder"]!).Select(c => (string)c!["name"]!));

        var products = catalog["products"]!.AsArray().ToDictionary(p => Guid.Parse((string)p!["id"]!));
        var colaRow = products[cola]!;
        Assert.Equal((199L, 10L, 19, "COLA"), ((long)colaRow["unitPrice"]!, (long)colaRow["depositPerUnit"]!, (int)colaRow["minAge"]!, (string)colaRow["sku"]!));
        Assert.Equal(["0067000004568", "4011"], colaRow["barcodes"]!.AsArray().Select(b => (string)b!).Order());
        Assert.Null(colaRow["taxProfileId"]);
        Assert.True((bool)products[cake]!["openPrice"]!);
        Assert.Null(products[cake]!["unitPrice"]);
        Assert.Equal(gst, Guid.Parse((string)products[cake]!["taxProfileId"]!));
        Assert.False((bool)products[old]!["active"]!);

        var discounts = catalog["discounts"]!.AsArray().ToDictionary(d => Guid.Parse((string)d!["id"]!));
        Assert.DoesNotContain(ending, discounts.Keys);
        Assert.Equal(("fixed", 25L), ((string)discounts[running]!["kind"]!, (long)discounts[running]!["amountOff"]!));
        Assert.Equal(("percent", "5"), ((string)discounts[future]!["kind"]!, (string)discounts[future]!["percent"]!));
        Assert.Null(discounts[future]!["toDate"]);
        Assert.Empty(catalog["removedDiscountIds"]!.AsArray());
    }

    [Fact]
    public async Task AnEmptyCatalog_IsAFullDownloadWithEmptyLists()
    {
        await using var shop = await ShopAsync(postgres);
        using var device = await RegisterDeviceAsync(shop.App, shop.Owner, shop.Id);
        using var client = DeviceClient(shop.App, device.Credential);

        var catalog = JsonNode.Parse(await AssertContractAsync(await SyncAsync(client), HttpStatusCode.OK, "posCatalog"))!;

        Assert.Equal("full", (string?)catalog["mode"]);
        foreach (var list in new[] { "taxProfiles", "categories", "products", "discounts" })
        {
            Assert.Empty(catalog[list]!.AsArray());
        }
    }

    [Fact]
    public async Task OnlyAPairedTill_MayDownload()
    {
        await using var shop = await ShopAsync(postgres);
        using var device = await RegisterDeviceAsync(shop.App, shop.Owner, shop.Id);

        using (var unknown = DeviceClient(shop.App, "dev_" + shop.Id.ToString("N") + ".nope.nope"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await SyncAsync(unknown)).StatusCode);
        }

        using (var response = await StaffApi.SendAsync(shop.Owner, HttpMethod.Get, "/pos/catalog", shop.Id))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using (var revoked = await RevokeAsync(shop.Owner, shop.Id, device.DeviceId, 1))
        {
            Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        }

        using var client = DeviceClient(shop.App, device.Credential);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SyncAsync(client)).StatusCode);
    }

    [Fact]
    public async Task TheDownload_IsCompressed_WhenTheTillAcceptsIt()
    {
        await using var shop = await ShopAsync(postgres);
        await BasicsAsync(shop);
        using var device = await RegisterDeviceAsync(shop.App, shop.Owner, shop.Id);
        using var client = DeviceClient(shop.App, device.Credential);
        client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));

        using var response = await SyncAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("gzip", response.Content.Headers.ContentEncoding);
    }

    // ---- US2: only what changed (FR-006, research R1, R2) ---------------------------------------------

    [Fact]
    public async Task NothingChanged_IsAnEmptyChangeSet()
    {
        await using var shop = await ShopAsync(postgres);
        await BasicsAsync(shop);
        using var device = await RegisterDeviceAsync(shop.App, shop.Owner, shop.Id);
        using var client = DeviceClient(shop.App, device.Credential);
        var since = (long)(await CatalogAsync(client))["revision"]!;

        var changes = JsonNode.Parse(await AssertContractAsync(await SyncAsync(client, since), HttpStatusCode.OK, "posCatalog"))!;

        Assert.Equal("changes", (string?)changes["mode"]);
        foreach (var list in new[] { "taxProfiles", "categories", "products", "discounts", "removedDiscountIds" })
        {
            Assert.Empty(changes[list]!.AsArray());
        }

        Assert.Equal("INR", (string?)changes["business"]!["currency"]);
    }

    [Fact]
    public async Task Changes_HoldExactlyWhatChanged_EachWhole()
    {
        await using var shop = await ShopAsync(postgres);
        var (profile, category) = await BasicsAsync(shop);
        var colaBody = ProductBody("Cola", category, 199, "067000004568");
        var (cola, colaVersion) = await ProductAsync(shop.Owner, shop.Id, colaBody);
        await ProductAsync(shop.Owner, shop.Id, ProductBody("Water", category, 99));
        var discount = await DiscountIdAsync(shop, cola, DiscountBody("percent", "10", Today(shop.App).AddDays(2)));
        using var device = await RegisterDeviceAsync(shop.App, shop.Owner, shop.Id);
        using var client = DeviceClient(shop.App, device.Credential);
        var since = (long)(await CatalogAsync(client))["revision"]!;

        colaBody["unitPrice"] = 219;
        colaBody["barcodes"] = new JsonArray("067000004568", "4011");
        using (await UpdateProductAsync(shop.Owner, shop.Id, cola, colaBody, colaVersion))
        {
        }

        var profileVersion = (int)(await JsonAsync(await GetTaxProfileAsync(shop.Owner, shop.Id, profile)))["version"]!;
        using (await AddVersionAsync(shop.Owner, shop.Id, profile, Today(shop.App).AddDays(5), Components(("CGST", "6"), ("SGST", "6")), profileVersion))
        {
        }

        using (await DeleteDiscountAsync(shop.Owner, shop.Id, cola, discount))
        {
        }

        var changes = JsonNode.Parse(await AssertContractAsync(await SyncAsync(client, since), HttpStatusCode.OK, "posCatalog"))!;

        Assert.Equal("changes", (string?)changes["mode"]);
        var product = Assert.Single(changes["products"]!.AsArray())!;
        Assert.Equal((cola, 219L), (Guid.Parse((string)product["id"]!), (long)product["unitPrice"]!));
        Assert.Equal(["0067000004568", "4011"], product["barcodes"]!.AsArray().Select(b => (string)b!).Order());
        var changedProfile = Assert.Single(changes["taxProfiles"]!.AsArray())!;
        Assert.Equal(2, changedProfile["versions"]!.AsArray().Count);
        Assert.Empty(changes["categories"]!.AsArray());
        Assert.Equal([discount], changes["removedDiscountIds"]!.AsArray().Select(d => Guid.Parse((string)d!)));
        Assert.True((long)changes["revision"]! >= since);
    }

    [Fact]
    public async Task ASaveCommittingAfterTheRead_IsInTheNextChanges()
    {
        await using var shop = await ShopAsync(postgres);
        var (_, category) = await BasicsAsync(shop);
        var (cola, _) = await ProductAsync(shop.Owner, shop.Id, ProductBody("Cola", category, 199));
        using var device = await RegisterDeviceAsync(shop.App, shop.Owner, shop.Id);
        using var client = DeviceClient(shop.App, device.Credential);

        // A price change written before the till reads, committed after it.
        await using var connection = new NpgsqlConnection(shop.App.Logins.Migrator);
        await connection.OpenAsync(Ct);
        await using var late = await connection.BeginTransactionAsync(Ct);
        await using (var update = new NpgsqlCommand("update pos.products set unit_price = 2.49 where id = @id", connection, late))
        {
            update.Parameters.AddWithValue("id", cola);
            await update.ExecuteNonQueryAsync(Ct);
        }

        var read = await CatalogAsync(client);
        Assert.Equal(199L, (long)read["products"]![0]!["unitPrice"]!);
        await late.CommitAsync(Ct);

        var changes = await CatalogAsync(client, (long)read["revision"]!);

        var product = Assert.Single(changes["products"]!.AsArray())!;
        Assert.Equal(249L, (long)product["unitPrice"]!);
    }

    [Fact]
    public async Task ASinceFromElsewhere_GetsAFullDownload()
    {
        await using var shop = await ShopAsync(postgres);
        await BasicsAsync(shop);
        using var device = await RegisterDeviceAsync(shop.App, shop.Owner, shop.Id);
        using var client = DeviceClient(shop.App, device.Credential);

        var catalog = await CatalogAsync(client, long.MaxValue / 2);

        Assert.Equal("full", (string?)catalog["mode"]);
        Assert.Single(catalog["categories"]!.AsArray());
    }

    [Fact]
    public async Task TenThousandProducts_DownloadQuickly_AndTenChangesAreUnderOnePercentOfIt()
    {
        await using var shop = await ShopAsync(postgres);
        var (_, category) = await BasicsAsync(shop);
        await ExecuteAsync(shop, """
            insert into pos.products (business_id, id, name, category_id, unit, unit_price, open_price, min_quantity, step, deposit_per_unit, active)
            select @b, gen_random_uuid(), 'Product ' || i, @c, 'piece', 1.99, false, 1, 1, 0, true
            from generate_series(1, 10000) as i
            """, category);
        using var device = await RegisterDeviceAsync(shop.App, shop.Owner, shop.Id);
        using var client = DeviceClient(shop.App, device.Credential);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        using var fullResponse = await SyncAsync(client);
        var full = await fullResponse.Content.ReadAsByteArrayAsync(Ct);
        watch.Stop();
        Assert.True(watch.ElapsedMilliseconds < 10_000, $"a 10,000-product download took {watch.ElapsedMilliseconds} ms on the server");
        var since = (long)JsonNode.Parse(full)!["revision"]!;
        await ExecuteAsync(shop,
            "update pos.products set unit_price = 2.09 where id in (select id from pos.products where business_id = @b order by name limit 10)", category);

        using var changesResponse = await SyncAsync(client, since);
        var changes = await changesResponse.Content.ReadAsByteArrayAsync(Ct);

        Assert.Equal(10, JsonNode.Parse(changes)!["products"]!.AsArray().Count);
        Assert.True(changes.Length * 100 < full.Length, $"changes {changes.Length} bytes, full {full.Length} bytes");
    }

    private static async Task<JsonNode> CatalogAsync(HttpClient client, long? since = null)
    {
        using var response = await SyncAsync(client, since);
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.StatusCode == HttpStatusCode.OK, text);
        return JsonNode.Parse(text)!;
    }

    /// <summary>Runs SQL as the migrator with <c>@b</c> = the shop and <c>@c</c> = a category.</summary>
    private static async Task ExecuteAsync(CatalogShop shop, string sql, Guid category)
    {
        await using var connection = new NpgsqlConnection(shop.App.Logins.Migrator);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("b", shop.Id);
        command.Parameters.AddWithValue("c", category);
        await command.ExecuteNonQueryAsync(Ct);
    }

    internal static Task<HttpResponseMessage> SyncAsync(HttpClient device, long? since = null) =>
        device.GetAsync(new Uri("/pos/catalog" + (since is { } s ? $"?since={s}" : string.Empty), UriKind.Relative), Ct);

    private static async Task<Guid> DiscountIdAsync(CatalogShop shop, Guid product, JsonObject body)
    {
        using var created = await CreateDiscountAsync(shop.Owner, shop.Id, product, body);
        var text = await created.Content.ReadAsStringAsync(Ct);
        Assert.True(created.StatusCode == HttpStatusCode.Created, text);
        return Guid.Parse((string)JsonNode.Parse(text)!["id"]!);
    }
}
