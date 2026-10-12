using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using HelloPos.Api.Features.Catalog;
using HelloPos.Api.Tests.Contract;
using HelloPos.Api.Tests.Infrastructure.Database;
using HelloPos.Api.Tests.Tenancy;
using HelloPos.Kernel.Pricing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static HelloPos.Api.Tests.Features.Catalog.CatalogApi;
using static HelloPos.Api.Tests.Features.Pos.PosApi;

namespace HelloPos.Api.Tests.Features.Catalog;

/// <summary>
/// Feature #13 FR-017, SC-005, research R9: each catalog of <c>contracts/catalog-pricing-cases.json</c> is
/// stored for a test business; a till's download must equal the file, and every check priced through the
/// server's <see cref="CatalogPricing"/> must give the hand-derived values. The Kotlin suite prices the same
/// file through the till's price feed.
/// </summary>
[Collection(PostgresTests.Name)]
public sealed class CatalogPricingCasesTests(PostgresFixture postgres)
{
    private static readonly JsonArray Catalogs =
        JsonNode.Parse(File.ReadAllText(Path.Combine(OpenApiContract.RepositoryRoot, "contracts", "catalog-pricing-cases.json")))!["catalogs"]!.AsArray();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> CatalogIds()
    {
        var data = new TheoryData<string>();
        foreach (var catalog in Catalogs)
        {
            data.Add((string)catalog!["id"]!);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CatalogIds))]
    public async Task TheTillsDownload_EqualsTheFile_AndTheServerPricesEveryCheckAsExpected(string id)
    {
        var entry = Catalogs.Single(c => (string)c!["id"]! == id)!;
        var catalog = entry["catalog"]!;
        await using var shop = await ShopAsync(postgres);
        await StoreAsync(shop, catalog);

        using var device = await RegisterDeviceAsync(shop.App, shop.Owner, shop.Id);
        using var client = DeviceClient(shop.App, device.Credential);
        using var response = await client.GetAsync(new Uri("/pos/catalog", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var downloaded = JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!;
        var serverToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(
            DateTimeOffset.Parse((string)downloaded["serverTime"]!, CultureInfo.InvariantCulture),
            TimeZoneInfo.FindSystemTimeZoneById((string)catalog["business"]!["timeZone"]!)).DateTime);
        AssertSameCatalog(catalog, downloaded, serverToday.AddDays(-1));

        foreach (var check in entry["checks"]!.AsArray())
        {
            await AssertPricedAsync(shop, check!);
        }
    }

    private static async Task AssertPricedAsync(CatalogShop shop, JsonNode check)
    {
        var product = Guid.Parse((string)check["productId"]!);
        var at = DateTimeOffset.Parse((string)check["at"]!, CultureInfo.InvariantCulture);
        var expected = check["expected"]!;
        await using var scope = shop.App.Services.CreateAsyncScope();
        var db = TenancyApp.ActingAs(scope.ServiceProvider, shop.Id);
        var feed = await CatalogPricing.ForAsync(db, [product], at, Ct);
        var line = feed.Line(product, "a", decimal.Parse((string)check["quantity"]!, CultureInfo.InvariantCulture), (long?)check["enteredPrice"]);

        var sale = Assert.IsType<PricingResult.Priced>(PricingEngine.Price(feed.Sale([line]))).Sale;

        var name = (string)check["id"]!;
        var priced = Assert.Single(sale.Lines);
        Assert.True((string)expected["taxProfile"]! == feed.Products[product].TaxProfile.Name, name);
        Assert.True(
            ((long)expected["lineAmount"]!, (long)expected["itemDiscount"]!, (long)expected["deposit"]!) == (priced.LineAmount, priced.ItemDiscount, priced.Deposit),
            $"{name}: line {priced}");
        Assert.Equal(
            expected["taxes"]!.AsArray().Select(t => ((string)t!["label"]!, decimal.Parse((string)t["ratePercent"]!, CultureInfo.InvariantCulture), (long)t["amount"]!)),
            sale.Taxes.Select(t => (t.Label, t.RatePercent, t.Amount)));
        Assert.True(((long)expected["taxTotal"]!, (long)expected["total"]!) == (sale.TaxTotal, sale.Total), $"{name}: tax {sale.TaxTotal}, total {sale.Total}");
    }

    /// <summary>Compares the download with the file, lists in id order; ended discounts are left out of downloads.</summary>
    private static void AssertSameCatalog(JsonNode expected, JsonNode actual, DateOnly yesterday)
    {
        Assert.True(JsonNode.DeepEquals(expected["business"], actual["business"]), $"business: {actual["business"]}");
        foreach (var list in new[] { "taxProfiles", "categories", "products", "discounts" })
        {
            var want = expected[list]!.AsArray()
                .Where(d => list != "discounts" || d!["toDate"] is null || DateOnly.Parse((string)d["toDate"]!, CultureInfo.InvariantCulture) >= yesterday);
            Assert.True(
                JsonNode.DeepEquals(Sorted(want), Sorted(actual[list]!.AsArray())),
                $"{list}:\nexpected {Sorted(want).ToJsonString()}\nactual   {Sorted(actual[list]!.AsArray()).ToJsonString()}");
        }
    }

    private static JsonArray Sorted(IEnumerable<JsonNode?> items) =>
        new(items.OrderBy(i => (string)i!["id"]!, StringComparer.Ordinal).Select(i => i!.DeepClone()).ToArray());

    /// <summary>Writes the file's catalog, with its ids, for <paramref name="shop"/> (as the migrator).</summary>
    private static async Task StoreAsync(CatalogShop shop, JsonNode catalog)
    {
        var business = catalog["business"]!;
        var currency = (string)business["currency"]!;
        await using var connection = new NpgsqlConnection(shop.App.Logins.Migrator);
        await connection.OpenAsync(Ct);

        async Task Run(string sql, params (string Name, object? Value)[] parameters)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("b", shop.Id);
            foreach (var (parameter, value) in parameters)
            {
                command.Parameters.AddWithValue(parameter, value ?? DBNull.Value);
            }

            await command.ExecuteNonQueryAsync(Ct);
        }

        decimal Major(JsonNode? minor) => (long)minor! / (decimal)Math.Pow(10, Currencies.DigitsOf(currency));
        Guid Id(JsonNode? node) => Guid.Parse((string)node!);
        DateOnly Day(JsonNode? node) => DateOnly.Parse((string)node!, CultureInfo.InvariantCulture);

        await Run(
            "update pos.businesses set country_code = @country, currency_code = @currency, pricing_mode = @mode, time_zone = @zone, cash_rounding_step = @step, deposits_taxable = @deposits where id = @b",
            ("country", currency == "INR" ? "IN" : "CA"), ("currency", currency), ("mode", (string)business["pricingMode"]!),
            ("zone", (string)business["timeZone"]!), ("step", Major(business["cashRoundingStep"])), ("deposits", (bool)business["depositsTaxable"]!));

        foreach (var profile in catalog["taxProfiles"]!.AsArray())
        {
            var kind = (string)profile!["kind"]! == "zeroRated" ? TaxKinds.ZeroRated : (string)profile["kind"]!;
            await Run("insert into pos.tax_profiles (business_id, id, name, normalized_name, kind, active) values (@b, @id, @name, @norm, @kind, @active)",
                ("id", Id(profile["id"])), ("name", (string)profile["name"]!), ("norm", ((string)profile["name"]!).ToLowerInvariant()), ("kind", kind), ("active", (bool)profile["active"]!));
            foreach (var version in profile["versions"]!.AsArray())
            {
                await Run("insert into pos.tax_profile_versions (business_id, id, tax_profile_id, effective_from) values (@b, @id, @profile, @from)",
                    ("id", Id(version!["id"])), ("profile", Id(profile["id"])), ("from", Day(version["effectiveFrom"])));
                var position = 0;
                foreach (var component in version["components"]!.AsArray())
                {
                    await Run("insert into pos.tax_components (business_id, id, tax_profile_version_id, position, label, rate_percent) values (@b, gen_random_uuid(), @version, @position, @label, @rate)",
                        ("version", Id(version["id"])), ("position", (short)position++), ("label", (string)component!["label"]!),
                        ("rate", decimal.Parse((string)component["ratePercent"]!, CultureInfo.InvariantCulture)));
                }
            }
        }

        foreach (var category in catalog["categories"]!.AsArray())
        {
            await Run("insert into pos.categories (business_id, id, name, normalized_name, sort_order, default_tax_profile_id, active) values (@b, @id, @name, @norm, @sort, @profile, @active)",
                ("id", Id(category!["id"])), ("name", (string)category["name"]!), ("norm", ((string)category["name"]!).ToLowerInvariant()), ("sort", (int)category["sortOrder"]!),
                ("profile", Id(category["defaultTaxProfileId"])), ("active", (bool)category["active"]!));
        }

        foreach (var product in catalog["products"]!.AsArray())
        {
            await Run("""
                insert into pos.products (business_id, id, name, sku, normalized_sku, category_id, tax_profile_id, unit, unit_price, open_price, min_quantity, step, min_age, deposit_per_unit, active)
                values (@b, @id, @name, @sku, @norm, @category, @profile, @unit, @price, @open, @min, @step, @age, @deposit, @active)
                """,
                ("id", Id(product!["id"])), ("name", (string)product["name"]!), ("sku", (string?)product["sku"]), ("norm", ((string?)product["sku"])?.ToLowerInvariant()), ("category", Id(product["categoryId"])),
                ("profile", product["taxProfileId"] is null ? null : Id(product["taxProfileId"])), ("unit", (string)product["unit"]!),
                ("price", product["unitPrice"] is null ? null : Major(product["unitPrice"])), ("open", (bool)product["openPrice"]!),
                ("min", decimal.Parse((string)product["minQuantity"]!, CultureInfo.InvariantCulture)), ("step", decimal.Parse((string)product["step"]!, CultureInfo.InvariantCulture)),
                ("age", product["minAge"] is null ? null : (short)(int)product["minAge"]!), ("deposit", Major(product["depositPerUnit"])), ("active", (bool)product["active"]!));
            foreach (var code in product["barcodes"]!.AsArray())
            {
                var value = (string)code!;
                await Run("insert into pos.product_barcodes (business_id, id, product_id, code, kind) values (@b, gen_random_uuid(), @product, @code, @kind)",
                    ("product", Id(product["id"])), ("code", value), ("kind", value.Length switch { 13 => "ean13", 8 => "ean8", _ => "plu" }));
            }
        }

        foreach (var discount in catalog["discounts"]!.AsArray())
        {
            var percent = (string)discount!["kind"]! == "percent";
            await Run("insert into pos.item_discounts (business_id, id, product_id, kind, value, from_date, to_date) values (@b, @id, @product, @kind, @value, @from, @to)",
                ("id", Id(discount["id"])), ("product", Id(discount["productId"])), ("kind", (string)discount["kind"]!),
                ("value", percent ? decimal.Parse((string)discount["percent"]!, CultureInfo.InvariantCulture) : Major(discount["amountOff"])),
                ("from", Day(discount["fromDate"])), ("to", discount["toDate"] is null ? null : Day(discount["toDate"])));
        }
    }
}
