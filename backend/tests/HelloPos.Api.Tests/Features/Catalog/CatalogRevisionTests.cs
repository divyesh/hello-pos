using System.Net;
using HelloPos.Api.Tests.Infrastructure.Database;
using HelloPos.Api.Tests.Tenancy;
using Npgsql;
using static HelloPos.Api.Tests.Features.Catalog.CatalogApi;
using static HelloPos.Api.Tests.Features.Catalog.CatalogImportTests;

namespace HelloPos.Api.Tests.Features.Catalog;

/// <summary>
/// Feature #13, research R1: every catalog write stamps exactly the changed roots (tax profile, category,
/// product, item discount) with a newer revision, children included; removed discounts are recorded.
/// </summary>
[Collection(PostgresTests.Name)]
public sealed class CatalogRevisionTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TaxProfileWrites_StampTheProfile_IncludingVersionChanges()
    {
        await using var shop = await ShopAsync(postgres);
        var (id, version) = await TaxProfileAsync(shop.Owner, shop.Id);
        var other = (await TaxProfileAsync(shop.Owner, shop.Id, "Exempt", "exempt")).Id;
        var created = await RevisionAsync(shop.App, "tax_profiles", id);
        var untouched = await RevisionAsync(shop.App, "tax_profiles", other);
        Assert.True(created > 0);

        var added = await JsonAsync(await AddVersionAsync(shop.Owner, shop.Id, id, Today(shop.App).AddDays(3), Components(("CGST", "6"), ("SGST", "6")), version));
        var afterAdd = await RevisionAsync(shop.App, "tax_profiles", id);
        Assert.True(afterAdd > created);

        var futureId = Guid.Parse((string)added["versions"]![1]!["id"]!);
        using (await DeleteVersionAsync(shop.Owner, shop.Id, id, futureId))
        {
        }

        var afterDelete = await RevisionAsync(shop.App, "tax_profiles", id);
        Assert.True(afterDelete > afterAdd);

        using (await RenameTaxProfileAsync(shop.Owner, shop.Id, id, "GST", (int)(await JsonAsync(await GetTaxProfileAsync(shop.Owner, shop.Id, id)))["version"]!))
        {
        }

        Assert.True(await RevisionAsync(shop.App, "tax_profiles", id) > afterDelete);
        Assert.Equal(untouched, await RevisionAsync(shop.App, "tax_profiles", other));
    }

    [Fact]
    public async Task CategoryWrites_StampOnlyThatCategory_AndARefusedSaveChangesNothing()
    {
        await using var shop = await ShopAsync(postgres);
        var (profile, category) = await BasicsAsync(shop);
        var (other, _) = await CategoryAsync(shop.Owner, shop.Id, "Other", profile);
        var before = await RevisionAsync(shop.App, "categories", category);
        var otherBefore = await RevisionAsync(shop.App, "categories", other);
        var profileBefore = await RevisionAsync(shop.App, "tax_profiles", profile);

        using (var updated = await UpdateCategoryAsync(shop.Owner, shop.Id, category, "General goods", 2, profile, 1))
        {
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        }

        var after = await RevisionAsync(shop.App, "categories", category);
        Assert.True(after > before);
        Assert.Equal(otherBefore, await RevisionAsync(shop.App, "categories", other));
        Assert.Equal(profileBefore, await RevisionAsync(shop.App, "tax_profiles", profile));

        using (var stale = await UpdateCategoryAsync(shop.Owner, shop.Id, category, "Stale", 3, profile, 1))
        {
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        }

        Assert.Equal(after, await RevisionAsync(shop.App, "categories", category));

        using (await SetCategoryActiveAsync(shop.Owner, shop.Id, category, false, 2))
        {
        }

        Assert.True(await RevisionAsync(shop.App, "categories", category) > after);
    }

    [Fact]
    public async Task ProductWrites_StampTheProduct_EvenWhenOnlyBarcodesChange()
    {
        await using var shop = await ShopAsync(postgres);
        var (_, category) = await BasicsAsync(shop);
        var body = ProductBody("Cola", category, 199, "067000004568");
        var (id, _) = await ProductAsync(shop.Owner, shop.Id, body);
        var (other, _) = await ProductAsync(shop.Owner, shop.Id, ProductBody("Water", category, 99));
        var created = await RevisionAsync(shop.App, "products", id);
        var otherCreated = await RevisionAsync(shop.App, "products", other);
        var categoryBefore = await RevisionAsync(shop.App, "categories", category);

        body["barcodes"] = new System.Text.Json.Nodes.JsonArray("067000004568", "4011");
        using (var updated = await UpdateProductAsync(shop.Owner, shop.Id, id, body, 1))
        {
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        }

        var afterBarcodes = await RevisionAsync(shop.App, "products", id);
        Assert.True(afterBarcodes > created);
        Assert.Equal(otherCreated, await RevisionAsync(shop.App, "products", other));
        Assert.Equal(categoryBefore, await RevisionAsync(shop.App, "categories", category));

        using (await SetProductActiveAsync(shop.Owner, shop.Id, id, false, 2))
        {
        }

        Assert.True(await RevisionAsync(shop.App, "products", id) > afterBarcodes);
    }

    [Fact]
    public async Task DiscountWrites_StampTheDiscount_AndRemovalsAreRecorded()
    {
        await using var shop = await ShopAsync(postgres);
        var (_, category) = await BasicsAsync(shop);
        var (product, _) = await ProductAsync(shop.Owner, shop.Id, ProductBody("Cola", category));
        var today = Today(shop.App);
        var created = await JsonAsync(await CreateDiscountAsync(shop.Owner, shop.Id, product, DiscountBody("percent", "10", today.AddDays(2), today.AddDays(5))));
        var discount = Guid.Parse((string)created["id"]!);
        var first = await RevisionAsync(shop.App, "item_discounts", discount);
        Assert.True(first > 0);

        using (await UpdateDiscountAsync(shop.Owner, shop.Id, product, discount, DiscountBody("percent", "15", today.AddDays(2), today.AddDays(6))))
        {
        }

        var second = await RevisionAsync(shop.App, "item_discounts", discount);
        Assert.True(second > first);

        using (var deleted = await DeleteDiscountAsync(shop.Owner, shop.Id, product, discount))
        {
            Assert.True(deleted.IsSuccessStatusCode);
        }

        var removal = await ScalarAsync(shop.App,
            "select revision from pos.catalog_removals where entity = 'item_discount' and entity_id = @id", discount);
        Assert.True(removal > second);
    }

    [Fact]
    public async Task AnImport_StampsEveryProductItCreatesOrChanges_AndNoOther()
    {
        await using var shop = await ShopAsync(postgres);
        await TaxProfileAsync(shop.Owner, shop.Id, "GST 5%");
        using (await ImportAsync(shop.Owner, shop.Id, Csv(
            "COLA,Cola,Drinks,GST 5%,,piece,1.99,false,1,1,067000004568,,0,true",
            "TEA,Tea,Drinks,GST 5%,,piece,2.50,false,1,1,,,0,true")))
        {
        }

        var cola = await IdAsync(shop.App, "COLA");
        var tea = await IdAsync(shop.App, "TEA");
        var colaBefore = await RevisionAsync(shop.App, "products", cola);
        var teaBefore = await RevisionAsync(shop.App, "products", tea);

        using (var imported = await ImportAsync(shop.Owner, shop.Id, Csv(
            "COLA,Cola,Drinks,GST 5%,,piece,2.19,false,1,1,067000004568,,0,true",
            "TEA,Tea,Drinks,GST 5%,,piece,2.50,false,1,1,,,0,true")))
        {
            Assert.Equal(HttpStatusCode.OK, imported.StatusCode);
        }

        Assert.True(await RevisionAsync(shop.App, "products", cola) > colaBefore);
        Assert.Equal(teaBefore, await RevisionAsync(shop.App, "products", tea));
    }

    /// <summary>A root's revision, read as the migrator (no business scope needed).</summary>
    internal static Task<long> RevisionAsync(TenancyApp app, string table, Guid id) =>
        ScalarAsync(app, $"select revision from pos.{table} where id = @id", id);

    private static async Task<Guid> IdAsync(TenancyApp app, string sku)
    {
        await using var connection = new NpgsqlConnection(app.Logins.Migrator);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("select id from pos.products where sku = @sku", connection);
        command.Parameters.AddWithValue("sku", sku);
        return (Guid)(await command.ExecuteScalarAsync(Ct))!;
    }

    private static async Task<long> ScalarAsync(TenancyApp app, string sql, Guid id)
    {
        await using var connection = new NpgsqlConnection(app.Logins.Migrator);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }
}
