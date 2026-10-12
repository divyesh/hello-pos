using System.Net;
using System.Text.Json.Nodes;
using HelloPos.Api.Tests.Infrastructure.Database;
using static HelloPos.Api.Tests.Features.Businesses.CreateBusinessTests;
using static HelloPos.Api.Tests.Features.Businesses.OpenAndEditTests;
using static HelloPos.Api.Tests.Features.Catalog.CatalogApi;
using static HelloPos.Api.Tests.Features.Owners.OwnerApi;

namespace HelloPos.Api.Tests.Features.Catalog;

/// <summary>US2 products: FR-004, FR-005, FR-006, FR-009, FR-010, FR-015; SC-004.</summary>
[Collection(PostgresTests.Name)]
public sealed class ProductTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Manager_CreatesACountedProduct_WithBarcodes_FollowingTheCategoryTax()
    {
        await using var shop = await ShopAsync(postgres);
        var (profile, category) = await BasicsAsync(shop);
        var body = ProductBody("Cola 355 ml", category, 199, "0 67000 00456 8", "5901234123457");
        body["sku"] = "COLA-355";
        body["depositPerUnit"] = 10;

        var product = await JsonAsync(await AssertContractAsync(await CreateProductAsync(shop.Manager, shop.Id, body), HttpStatusCode.Created, "createProduct"));

        Assert.Equal(199, (long)product["unitPrice"]!);
        Assert.Equal("INR", (string?)product["currency"]);
        Assert.Null(product["taxProfileId"]);
        Assert.Equal(profile.ToString(), (string?)product["effectiveTaxProfileId"]);
        Assert.Equal(["0067000004568", "5901234123457"], product["barcodes"]!.AsArray().Select(b => (string)b!["code"]!));
        Assert.Equal(10, (long)product["depositPerUnit"]!);
        Assert.Equal("1", (string?)product["minQuantity"]);
        Assert.Contains("product:created", await HistoryAsync(shop.App));
    }

    [Fact]
    public async Task WeighedOpenPriceAgeRestricted_AndATaxOverride()
    {
        await using var shop = await ShopAsync(postgres);
        var (_, category) = await BasicsAsync(shop);
        var (exempt, _) = await TaxProfileAsync(shop.Owner, shop.Id, "Exempt", "exempt");

        var bananas = ProductBody("Bananas", category, 69, "4011");
        (bananas["unit"], bananas["minQuantity"], bananas["step"], bananas["taxProfileId"]) = ("kg", "0.005", "0.005", exempt.ToString());
        var weighed = await JsonAsync(await AssertContractAsync(await CreateProductAsync(shop.Owner, shop.Id, bananas), HttpStatusCode.Created, "createProduct"));
        Assert.Equal("0.005", (string?)weighed["step"]);
        Assert.Equal(exempt.ToString(), (string?)weighed["effectiveTaxProfileId"]);

        var cake = await JsonAsync(await AssertContractAsync(await CreateProductAsync(shop.Owner, shop.Id, ProductBody("Custom cake", category, null)), HttpStatusCode.Created, "createProduct"));
        Assert.True((bool?)cake["openPrice"]);
        Assert.Null(cake["unitPrice"]);

        var beer = ProductBody("Beer 6-pack", category, 1599);
        beer["minAge"] = 19;
        Assert.Equal(19, (int)(await JsonAsync(await CreateProductAsync(shop.Owner, shop.Id, beer)))["minAge"]!);
    }

    [Theory]
    [InlineData("unitPrice", null, "openPrice", false)]
    [InlineData("unitPrice", -1, null, null)]
    [InlineData("minQuantity", "0", null, null)]
    [InlineData("minQuantity", "0.3", "step", "0.25")]
    [InlineData("step", "0.0005", null, null)]
    [InlineData("minAge", 0, null, null)]
    [InlineData("depositPerUnit", -5, null, null)]
    [InlineData("unit", "lb", null, null)]
    [InlineData("name", " ", null, null)]
    public async Task InvalidFields_AreRefused(string field, object? value, string? otherField, object? otherValue)
    {
        await using var shop = await ShopAsync(postgres);
        var (_, category) = await BasicsAsync(shop);
        var body = ProductBody("P", category);
        body[field] = JsonValue.Create(value);
        if (otherField is not null)
        {
            body[otherField] = JsonValue.Create(otherValue);
        }

        if (value is null)
        {
            body.Remove(field);
        }

        var problem = await JsonAsync(await AssertContractAsync(await CreateProductAsync(shop.Owner, shop.Id, body), HttpStatusCode.BadRequest, "createProduct"));

        Assert.NotNull(problem["errors"]![field]);
    }

    [Theory]
    [InlineData("5901234123458", "check digit")]
    [InlineData("2012345678903", "in-store")]
    [InlineData("12345", "PLU")]
    [InlineData("abc", "only digits")]
    public async Task InvalidBarcodes_AreRefusedWithTheReason(string barcode, string reason)
    {
        await using var shop = await ShopAsync(postgres);
        var (_, category) = await BasicsAsync(shop);

        var problem = await JsonAsync(await AssertContractAsync(
            await CreateProductAsync(shop.Owner, shop.Id, ProductBody("P", category, 100, barcode)), HttpStatusCode.BadRequest, "createProduct"));

        Assert.Contains(reason, (string)problem["errors"]!["barcodes"]![0]!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ABarcode_BelongsToOneProductPerBusiness_InEitherForm()
    {
        await using var shop = await ShopAsync(postgres);
        var (_, category) = await BasicsAsync(shop);
        await ProductAsync(shop.Owner, shop.Id, ProductBody("Cola", category, 199, "067000004568"));

        var duplicate = await JsonAsync(await CreateProductAsync(shop.Owner, shop.Id, ProductBody("Fake cola", category, 99, "0067000004568")));
        var twice = await JsonAsync(await CreateProductAsync(shop.Owner, shop.Id, ProductBody("Twice", category, 99, "4011", "4011")));

        Assert.Contains("Cola", (string)duplicate["errors"]!["barcodes"]![0]!, StringComparison.Ordinal);
        Assert.NotNull(twice["errors"]?["barcodes"]);
    }

    [Fact]
    public async Task Skus_AreUniqueIgnoringCase()
    {
        await using var shop = await ShopAsync(postgres);
        var (_, category) = await BasicsAsync(shop);
        var first = ProductBody("A", category);
        first["sku"] = "abc-1";
        await ProductAsync(shop.Owner, shop.Id, first);
        var second = ProductBody("B", category);
        second["sku"] = " ABC-1 ";

        var problem = await JsonAsync(await CreateProductAsync(shop.Owner, shop.Id, second));

        Assert.NotNull(problem["errors"]?["sku"]);
    }

    [Fact]
    public async Task TheCategoryAndTaxOverride_MustBeActiveRecordsOfThisBusiness()
    {
        await using var shop = await ShopAsync(postgres);
        var (profile, category) = await BasicsAsync(shop);
        var (old, oldVersion) = await CategoryAsync(shop.Owner, shop.Id, "Old", profile);
        await SetCategoryActiveAsync(shop.Owner, shop.Id, old, false, oldVersion);

        var inactiveCategory = await JsonAsync(await CreateProductAsync(shop.Owner, shop.Id, ProductBody("P", old)));
        var unknownCategory = await JsonAsync(await CreateProductAsync(shop.Owner, shop.Id, ProductBody("P", Guid.CreateVersion7())));
        var overridden = ProductBody("P", category);
        overridden["taxProfileId"] = Guid.CreateVersion7().ToString();
        var unknownProfile = await JsonAsync(await CreateProductAsync(shop.Owner, shop.Id, overridden));

        Assert.NotNull(inactiveCategory["errors"]?["categoryId"]);
        Assert.NotNull(unknownCategory["errors"]?["categoryId"]);
        Assert.NotNull(unknownProfile["errors"]?["taxProfileId"]);
    }

    [Fact]
    public async Task Update_ReplacesBarcodes_RecordsOldAndNew_AndRefusesStaleVersions()
    {
        await using var shop = await ShopAsync(postgres);
        var (_, category) = await BasicsAsync(shop);
        var body = ProductBody("Cola", category, 199, "067000004568");
        var (id, version) = await ProductAsync(shop.Owner, shop.Id, body);

        body["unitPrice"] = 219;
        body["barcodes"] = new JsonArray("5901234123457");
        var updated = await JsonAsync(await AssertContractAsync(await UpdateProductAsync(shop.Manager, shop.Id, id, body, version), HttpStatusCode.OK, "updateProduct"));

        Assert.Equal(219, (long)updated["unitPrice"]!);
        Assert.Equal(["5901234123457"], updated["barcodes"]!.AsArray().Select(b => (string)b!["code"]!));
        await AssertContractAsync(await UpdateProductAsync(shop.Manager, shop.Id, id, body, version), HttpStatusCode.Conflict, "updateProduct");

        // The removed barcode is free again.
        await ProductAsync(shop.Owner, shop.Id, ProductBody("Other", category, 100, "067000004568"));
        var change = await ChangesJsonAsync(shop, "product", "updated");
        Assert.Contains("199", change, StringComparison.Ordinal);
        Assert.Contains("219", change, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deactivate_HidesFromActiveLists_ReactivateRestores()
    {
        await using var shop = await ShopAsync(postgres);
        var (_, category) = await BasicsAsync(shop);
        var (id, version) = await ProductAsync(shop.Owner, shop.Id, ProductBody("Cola", category));

        var off = await JsonAsync(await AssertContractAsync(await SetProductActiveAsync(shop.Manager, shop.Id, id, false, version), HttpStatusCode.OK, "deactivateProduct"));
        Assert.Equal(0, (int)(await JsonAsync(await ListProductsAsync(shop.Manager, shop.Id, "?active=true")))["total"]!);
        await AssertContractAsync(await SetProductActiveAsync(shop.Manager, shop.Id, id, true, (int)off["version"]!), HttpStatusCode.OK, "reactivateProduct");
        Assert.Equal(1, (int)(await JsonAsync(await ListProductsAsync(shop.Manager, shop.Id, "?active=true")))["total"]!);
    }

    [Theory]
    [InlineData("JPY", 149L)]
    [InlineData("KWD", 1250L)]
    [InlineData("USD", 349L)]
    public async Task Money_RoundTripsInMinorUnits_ForEachCurrencysDigits(string currency, long price)
    {
        await using var app = await HelloPos.Api.Tests.Tenancy.TenancyApp.CreateAsync(postgres);
        using var owner = await app.SignUpVerifiedOwnerAsync("owner@example.com");
        using var created = await CreateAsync(owner, Settings("Shop", "US", currency, "tax_exclusive", 1, "America/New_York", 0));
        var id = Guid.Parse((string)(await JsonAsync(created))["id"]!);
        await OpenAsync(owner, id);
        var (profile, _) = await TaxProfileAsync(owner, id, "Tax", components: Components(("Tax", "5")));
        var (category, _) = await CategoryAsync(owner, id, "General", profile);
        var body = ProductBody("Thing", category, price);
        body["depositPerUnit"] = price / 10;

        var product = await JsonAsync(await CreateProductAsync(owner, id, body));

        Assert.Equal(price, (long)product["unitPrice"]!);
        Assert.Equal(price / 10, (long)product["depositPerUnit"]!);
        Assert.Equal(currency, (string?)product["currency"]);
    }

    [Fact]
    public async Task Unknown_Is404()
    {
        await using var shop = await ShopAsync(postgres);

        await AssertContractAsync(await GetProductAsync(shop.Owner, shop.Id, Guid.CreateVersion7()), HttpStatusCode.NotFound, "getProduct");
    }

    private static async Task<string> ChangesJsonAsync(CatalogShop shop, string entity, string action)
    {
        await using var connection = new Npgsql.NpgsqlConnection(shop.App.Logins.Migrator);
        await connection.OpenAsync(Ct);
        await using var command = new Npgsql.NpgsqlCommand(
            "select changes::text from pos.catalog_changes where entity = @e and action = @a order by at desc limit 1", connection);
        command.Parameters.AddWithValue("e", entity);
        command.Parameters.AddWithValue("a", action);
        return (string)(await command.ExecuteScalarAsync(Ct))!;
    }
}
