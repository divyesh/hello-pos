using System.Net;
using HelloPos.Api.Features.Catalog.Import;
using HelloPos.Api.Tests.Features.Staff;
using HelloPos.Api.Tests.Infrastructure.Database;
using static HelloPos.Api.Tests.Features.Businesses.OpenAndEditTests;
using static HelloPos.Api.Tests.Features.Catalog.CatalogApi;
using static HelloPos.Api.Tests.Features.Owners.OwnerApi;

namespace HelloPos.Api.Tests.Features.Catalog;

/// <summary>US5 price list export (FR-013) and catalog history (FR-015).</summary>
[Collection(PostgresTests.Name)]
public sealed class ExportAndHistoryTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Export_IsTheImportFormat_ActiveProductsOnly_FormulaSafe()
    {
        await using var shop = await ShopAsync(postgres);
        var (_, category) = await BasicsAsync(shop);
        var (exempt, _) = await TaxProfileAsync(shop.Owner, shop.Id, "Exempt", "exempt");
        var cola = ProductBody("Cola, 355 ml", category, 199, "067000004568");
        (cola["sku"], cola["depositPerUnit"]) = ("COLA", 10);
        await ProductAsync(shop.Owner, shop.Id, cola);
        var sale = ProductBody("=HYPERLINK(\"x\")", category, null);
        sale["taxProfileId"] = exempt.ToString();
        await ProductAsync(shop.Owner, shop.Id, sale);
        var (old, version) = await ProductAsync(shop.Owner, shop.Id, ProductBody("Old", category, 100));
        await SetProductActiveAsync(shop.Owner, shop.Id, old, false, version);

        using var response = await ExportAsync(shop.Manager, shop.Id);
        var csv = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/csv", response.Content.Headers.ContentType?.ToString(), StringComparison.Ordinal);
        var (rows, _) = CsvReader.Parse(csv);
        Assert.Equal(CatalogCsv.Columns, rows[0].Fields);
        Assert.Equal(3, rows.Count);
        var colaRow = rows.Single(r => r.Fields[0] == "COLA").Fields;
        Assert.Equal(["COLA", "Cola, 355 ml", "General", "GST 5%", "", "piece", "1.99", "false", "1", "1", "0067000004568", "", "0.10", "true"], colaRow);
        var formula = rows.Single(r => r.Fields[0] == "").Fields;
        Assert.Equal("'=HYPERLINK(\"x\")", formula[1]);
        Assert.Equal(("Exempt", "", "true"), (formula[4], formula[6], formula[7]));
    }

    [Fact]
    public async Task AnExport_ReimportsWithNoChanges()
    {
        await using var shop = await ShopAsync(postgres);
        var (_, category) = await BasicsAsync(shop);
        var bananas = ProductBody("Bananas", category, 69, "4011");
        (bananas["unit"], bananas["minQuantity"], bananas["step"], bananas["minAge"]) = ("kg", "0.005", "0.005", 18);
        await ProductAsync(shop.Owner, shop.Id, bananas);
        await ProductAsync(shop.Owner, shop.Id, ProductBody("-50% sale bin", category, 100));

        using var exported = await ExportAsync(shop.Owner, shop.Id);
        var preview = await JsonAsync(await CatalogImportTests.PreviewAsync(shop.Owner, shop.Id, await exported.Content.ReadAsStringAsync(Ct)));

        Assert.Equal((0, 0, 2, 0), ((int)preview["create"]!, (int)preview["update"]!, (int)preview["unchanged"]!, (int)preview["errors"]!));
    }

    [Fact]
    public async Task History_ListsChangesNewestFirst_WithWhoAndWhat()
    {
        await using var shop = await ShopAsync(postgres);
        var (_, category) = await BasicsAsync(shop);
        var body = ProductBody("Cola", category, 199);
        var (product, version) = await ProductAsync(shop.Owner, shop.Id, body);
        body["unitPrice"] = 219;
        await UpdateProductAsync(shop.Manager, shop.Id, product, body, version);

        var history = await JsonAsync(await AssertContractAsync(await ChangesAsync(shop.Owner, shop.Id, $"?entityId={product}"), HttpStatusCode.OK, "listCatalogChanges"));

        var changes = history.AsArray();
        Assert.Equal(["updated", "created"], changes.Select(c => (string)c!["action"]!));
        Assert.Equal(["manager", "Owner"], changes.Select(c => (string)c!["actor"]!));
        var price = changes[0]!["changes"]!.AsArray().Single(c => (string)c!["field"]! == "unitPrice")!;
        Assert.Equal(("199", "219"), ((string)price["old"]!, (string)price["new"]!));
        Assert.Null(changes[1]!["changes"]!.AsArray().Single(c => (string)c!["field"]! == "name")!["old"]);
    }

    [Fact]
    public async Task ExportAndHistory_ShowOnlyTheOpenBusiness()
    {
        await using var shop = await ShopAsync(postgres);
        var (_, category) = await BasicsAsync(shop);
        await ProductAsync(shop.Owner, shop.Id, ProductBody("Theirs", category, 100));
        var other = await CreateIdAsync(shop.Owner, "Other");
        await OpenAsync(shop.Owner, other);

        using var exported = await ExportAsync(shop.Owner, other);
        var (rows, _) = CsvReader.Parse(await exported.Content.ReadAsStringAsync(Ct));

        Assert.Single(rows);
        Assert.Empty((await JsonAsync(await ChangesAsync(shop.Owner, other))).AsArray());
    }

    private static Task<HttpResponseMessage> ExportAsync(HttpClient client, Guid shop) =>
        StaffApi.SendAsync(client, HttpMethod.Get, "/catalog/products/export", shop);

    private static Task<HttpResponseMessage> ChangesAsync(HttpClient client, Guid shop, string query = "") =>
        StaffApi.SendAsync(client, HttpMethod.Get, "/catalog/changes" + query, shop);
}
