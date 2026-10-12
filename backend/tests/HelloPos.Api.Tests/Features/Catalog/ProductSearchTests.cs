using System.Diagnostics;
using System.Net;
using HelloPos.Api.Tests.Infrastructure.Database;
using Npgsql;
using static HelloPos.Api.Tests.Features.Catalog.CatalogApi;
using static HelloPos.Api.Tests.Features.Owners.OwnerApi;

namespace HelloPos.Api.Tests.Features.Catalog;

/// <summary>US2 search and filters: FR-012, SC-003.</summary>
[Collection(PostgresTests.Name)]
public sealed class ProductSearchTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task FindsByNamePart_Barcode_OrSku_AndFilters()
    {
        await using var shop = await ShopAsync(postgres);
        var (profile, drinks) = await BasicsAsync(shop);
        var (produce, _) = await CategoryAsync(shop.Owner, shop.Id, "Produce", profile);
        var cola = ProductBody("Coca-Cola 355 ml", drinks, 199, "067000004568");
        cola["sku"] = "CC-355";
        await ProductAsync(shop.Owner, shop.Id, cola);
        await ProductAsync(shop.Owner, shop.Id, ProductBody("Diet Cola", drinks, 199));
        await ProductAsync(shop.Owner, shop.Id, ProductBody("Bananas", produce, 69, "4011"));
        var (water, waterVersion) = await ProductAsync(shop.Owner, shop.Id, ProductBody("Water 1 l", drinks, 99));
        await SetProductActiveAsync(shop.Owner, shop.Id, water, false, waterVersion);

        Assert.Equal(["Coca-Cola 355 ml", "Diet Cola"], await NamesAsync(shop, "?q=COLA"));
        Assert.Equal(["Coca-Cola 355 ml"], await NamesAsync(shop, "?q=0067000004568"));
        Assert.Equal(["Coca-Cola 355 ml"], await NamesAsync(shop, "?q=0%2067000%2000456%208"));
        Assert.Equal(["Coca-Cola 355 ml"], await NamesAsync(shop, "?q=cc-355"));
        Assert.Equal(["Bananas"], await NamesAsync(shop, $"?categoryId={produce}"));
        Assert.Equal(["Water 1 l"], await NamesAsync(shop, "?active=false"));
        Assert.Equal(["Bananas", "Coca-Cola 355 ml", "Diet Cola"], await NamesAsync(shop, "?active=true"));
        Assert.Empty(await NamesAsync(shop, "?q=%25"));
    }

    [Fact]
    public async Task Pages_WithATotal()
    {
        await using var shop = await ShopAsync(postgres);
        var (_, category) = await BasicsAsync(shop);
        foreach (var name in new[] { "A", "B", "C", "D", "E" })
        {
            await ProductAsync(shop.Owner, shop.Id, ProductBody(name, category));
        }

        var second = await JsonAsync(await AssertContractAsync(await ListProductsAsync(shop.Owner, shop.Id, "?page=2&pageSize=2"), HttpStatusCode.OK, "listProducts"));

        Assert.Equal(5, (int)second["total"]!);
        Assert.Equal(["C", "D"], second["items"]!.AsArray().Select(p => (string)p!["name"]!));
    }

    [Fact]
    public async Task TenThousandProducts_SearchInUnderASecond()
    {
        await using var shop = await ShopAsync(postgres);
        var (_, category) = await BasicsAsync(shop);
        await using (var connection = new NpgsqlConnection(shop.App.Logins.Migrator))
        {
            await connection.OpenAsync(Ct);
            await using var insert = new NpgsqlCommand("""
                insert into pos.products (business_id, id, name, category_id, unit, unit_price, open_price, min_quantity, step, deposit_per_unit, active)
                select @b, gen_random_uuid(), 'Product ' || i || case when i % 1000 = 0 then ' special' else '' end,
                       @c, 'piece', 1.99, false, 1, 1, 0, true
                from generate_series(1, 10000) as i
                """, connection);
            insert.Parameters.AddWithValue("b", shop.Id);
            insert.Parameters.AddWithValue("c", category);
            await insert.ExecuteNonQueryAsync(Ct);
            await using var analyze = new NpgsqlCommand("analyze pos.products", connection);
            await analyze.ExecuteNonQueryAsync(Ct);
        }

        await ListProductsAsync(shop.Owner, shop.Id, "?q=special"); // warm up
        var watch = Stopwatch.StartNew();
        var found = await JsonAsync(await ListProductsAsync(shop.Owner, shop.Id, "?q=special"));
        watch.Stop();

        Assert.Equal(10, (int)found["total"]!);
        Assert.True(watch.ElapsedMilliseconds < 1000, $"search took {watch.ElapsedMilliseconds} ms");
    }

    private static async Task<List<string>> NamesAsync(CatalogShop shop, string query) =>
        (await JsonAsync(await ListProductsAsync(shop.Manager, shop.Id, query)))["items"]!.AsArray().Select(p => (string)p!["name"]!).ToList();
}
