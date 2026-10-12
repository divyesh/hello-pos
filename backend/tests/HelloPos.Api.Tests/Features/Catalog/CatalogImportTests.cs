using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using HelloPos.Api.Tests.Infrastructure.Database;
using static HelloPos.Api.Tests.Features.Catalog.CatalogApi;
using static HelloPos.Api.Tests.Features.Owners.OwnerApi;

namespace HelloPos.Api.Tests.Features.Catalog;

/// <summary>US4 CSV import: FR-011, SC-002 (research R8).</summary>
[Collection(PostgresTests.Name)]
public sealed class CatalogImportTests(PostgresFixture postgres)
{
    private const string Header = "sku,name,category,category_tax_profile,tax_profile,unit,price,open_price,min_quantity,step,barcodes,min_age,deposit,active";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Preview_ReportsEveryRow_AndSavesNothing()
    {
        await using var shop = await ShopAsync(postgres);
        await TaxProfileAsync(shop.Owner, shop.Id, "GST 5%");

        var preview = await JsonAsync(await AssertContractAsync(await PreviewAsync(shop.Manager, shop.Id, Csv(
            "COLA,Cola 355 ml,Drinks,GST 5%,,piece,1.99,false,1,1,067000004568,,0.10,true",
            "BAN,Bananas,Produce,GST 5%,,kg,0.69,false,0.005,0.005,4011,,0,true")), HttpStatusCode.OK, "previewCatalogImport"));

        Assert.Equal(2, (int)preview["create"]!);
        Assert.Equal(["create", "create"], preview["rows"]!.AsArray().Select(r => (string)r!["outcome"]!));
        Assert.Equal([2, 3], preview["rows"]!.AsArray().Select(r => (int)r!["row"]!));
        Assert.Equal(0, (int)(await JsonAsync(await ListProductsAsync(shop.Owner, shop.Id)))["total"]!);
        Assert.Empty((await JsonAsync(await ListCategoriesAsync(shop.Owner, shop.Id))).AsArray());
    }

    [Fact]
    public async Task Import_CreatesProductsAndMissingCategories_AndReimportChangesNothing()
    {
        await using var shop = await ShopAsync(postgres);
        await TaxProfileAsync(shop.Owner, shop.Id, "GST 5%");
        await TaxProfileAsync(shop.Owner, shop.Id, "Exempt", "exempt");
        var file = Csv(
            "COLA,\"Cola, 355 ml\",Drinks,GST 5%,,piece,1.99,false,1,1,067000004568,,0.10,true",
            ",Bananas,Produce,Exempt,,kg,0.69,false,0.005,0.005,4011,,,",
            "BEER,Beer 6-pack,Drinks,,,piece,15.99,false,1,1,5901234123457|9780306406157,19,0.60,true",
            "CAKE,Custom cake,Bakery,GST 5%,Exempt,piece,,true,1,1,,,0,true");

        var imported = await JsonAsync(await AssertContractAsync(await ImportAsync(shop.Owner, shop.Id, file), HttpStatusCode.OK, "importCatalog"));

        Assert.Equal((4, 4, 0, 0), ((int)imported["rows"]!, (int)imported["created"]!, (int)imported["updated"]!, (int)imported["unchanged"]!));
        Assert.Equal(["Bakery", "Drinks", "Produce"], (await JsonAsync(await ListCategoriesAsync(shop.Owner, shop.Id))).AsArray().Select(c => (string)c!["name"]!).Order());
        var beer = (await JsonAsync(await ListProductsAsync(shop.Owner, shop.Id, "?q=beer")))["items"]![0]!;
        Assert.Equal((1599L, 60L, 19), ((long)beer["unitPrice"]!, (long)beer["depositPerUnit"]!, (int)beer["minAge"]!));
        Assert.Equal(2, beer["barcodes"]!.AsArray().Count);
        var cake = (await JsonAsync(await ListProductsAsync(shop.Owner, shop.Id, "?q=cake")))["items"]![0]!;
        Assert.True((bool?)cake["openPrice"]);
        Assert.NotNull(cake["taxProfileId"]);
        Assert.Equal(4, (await HistoryAsync(shop.App)).Count(h => h == "product:created"));

        var again = await JsonAsync(await ImportAsync(shop.Owner, shop.Id, file));
        Assert.Equal((0, 0, 4), ((int)again["created"]!, (int)again["updated"]!, (int)again["unchanged"]!));
        Assert.Equal(2, (await JsonAsync(await AssertContractAsync(await ListImportsAsync(shop.Owner, shop.Id), HttpStatusCode.OK, "listCatalogImports"))).AsArray().Count);
    }

    [Fact]
    public async Task ChangedRows_AreUpdates_MatchedBySkuThenBarcode()
    {
        await using var shop = await ShopAsync(postgres);
        var (_, category) = await BasicsAsync(shop);
        var cola = ProductBody("Cola", category, 199, "067000004568");
        cola["sku"] = "COLA";
        await ProductAsync(shop.Owner, shop.Id, cola);
        await ProductAsync(shop.Owner, shop.Id, ProductBody("Bananas", category, 69, "4011"));

        var preview = await JsonAsync(await PreviewAsync(shop.Owner, shop.Id, Csv(
            "cola,Cola,General,,,piece,2.19,false,1,1,067000004568,,0,true",
            ",Bananas,General,,,piece,0.69,false,1,1,4011,,0,true")));

        Assert.Equal(["update", "unchanged"], preview["rows"]!.AsArray().Select(r => (string)r!["outcome"]!));
        await ImportAsync(shop.Owner, shop.Id, Csv("cola,Cola,General,,,piece,2.19,false,1,1,067000004568,,0,true"));
        var updated = (await JsonAsync(await ListProductsAsync(shop.Owner, shop.Id, "?q=COLA")))["items"]![0]!;
        Assert.Equal(219, (long)updated["unitPrice"]!);
        Assert.Contains("product:updated", await HistoryAsync(shop.App));
    }

    [Fact]
    public async Task BadRows_AreListedWithRowAndColumn_AndTheImportIsRefusedWhole()
    {
        await using var shop = await ShopAsync(postgres);
        var (_, category) = await BasicsAsync(shop);
        var taken = ProductBody("Taken", category, 100, "4011");
        taken["sku"] = "TAKEN";
        await ProductAsync(shop.Owner, shop.Id, taken);
        var file = Csv(
            "OK,Good row,General,,,piece,1.00,false,1,1,,,0,true",
            "P1,Comma price,General,,,piece,\"1,99\",false,1,1,,,0,true",
            "P2,Unknown tax,General,,Nope,piece,1.00,false,1,1,,,0,true",
            "P3,New category,Snacks,,,piece,1.00,false,1,1,,,0,true",
            "P4,Bad barcode,General,,,piece,1.00,false,1,1,5901234123458,,0,true",
            "P5,Duplicate sku,General,,,piece,1.00,false,1,1,,,0,true",
            "P5,Duplicate sku again,General,,,piece,1.00,false,1,1,,,0,true",
            "P6,Someone else's barcode,General,,,piece,1.00,false,1,1,4011,,0,true",
            "P7,Too precise,General,,,piece,1.999,false,1,1,,,0,true",
            "P8,Bad flag,General,,,piece,1.00,maybe,1,1,,,0,true");

        var preview = await JsonAsync(await PreviewAsync(shop.Owner, shop.Id, file));
        var errors = preview["rows"]!.AsArray().Where(r => (string)r!["outcome"]! == "error")
            .ToDictionary(r => (int)r!["row"]!, r => r!["errors"]!.AsArray().Select(e => (string)e!["column"]!).ToList());

        Assert.Equal(8, (int)preview["errors"]!);
        Assert.Equal(["price"], errors[3]);
        Assert.Equal(["tax_profile"], errors[4]);
        Assert.Equal(["category_tax_profile"], errors[5]);
        Assert.Equal(["barcodes"], errors[6]);
        Assert.Equal(["sku"], errors[8]);
        Assert.Equal(["barcodes"], errors[9]);
        Assert.Equal(["price"], errors[10]);
        Assert.Equal(["open_price"], errors[11]);
        Assert.Contains("TAKEN", (string)preview["rows"]![7]!["errors"]![0]!["message"]!, StringComparison.Ordinal);

        await AssertContractAsync(await ImportAsync(shop.Owner, shop.Id, file), HttpStatusCode.BadRequest, "importCatalog");
        Assert.Equal(1, (int)(await JsonAsync(await ListProductsAsync(shop.Owner, shop.Id)))["total"]!);
    }

    [Theory]
    [InlineData("name,price\nCola,1.99", "header")]
    [InlineData("", "header")]
    public async Task AFileWithoutTheRequiredColumns_IsRefused(string content, string expected)
    {
        await using var shop = await ShopAsync(postgres);

        var problem = await JsonAsync(await AssertContractAsync(await PreviewAsync(shop.Owner, shop.Id, content), HttpStatusCode.BadRequest, "previewCatalogImport"));

        Assert.NotNull(problem["errors"]![expected]);
    }

    [Fact]
    public async Task MoreThanTenThousandRows_IsRefused()
    {
        await using var shop = await ShopAsync(postgres);
        var rows = Enumerable.Range(1, 10_001).Select(i => $"S{i},Product {i},General,GST 5%,,piece,1.00,false,1,1,,,0,true");

        var problem = await JsonAsync(await PreviewAsync(shop.Owner, shop.Id, Csv([.. rows])));

        Assert.NotNull(problem["errors"]?["file"]);
    }

    [Fact]
    public async Task AThousandRows_PreviewAndImportInTime()
    {
        await using var shop = await ShopAsync(postgres);
        await TaxProfileAsync(shop.Owner, shop.Id, "GST 5%");
        var file = Csv([.. Enumerable.Range(1, 1000).Select(i =>
            $"S{i},Product {i},Category {i % 20},GST 5%,,piece,{i % 50}.99,false,1,1,{Ean13(i)},,0,true")]);

        var watch = Stopwatch.StartNew();
        var preview = await JsonAsync(await PreviewAsync(shop.Owner, shop.Id, file));
        var previewTime = watch.Elapsed;
        watch.Restart();
        var imported = await JsonAsync(await ImportAsync(shop.Owner, shop.Id, file));
        var importTime = watch.Elapsed;

        Assert.Equal(1000, (int)preview["create"]!);
        Assert.Equal(1000, (int)imported["created"]!);
        Assert.True(previewTime < TimeSpan.FromSeconds(10), $"preview took {previewTime}");
        Assert.True(importTime < TimeSpan.FromSeconds(30), $"import took {importTime}");
    }

    [Fact]
    public async Task WithoutSkuOrBarcode_TheNameMatches_OnlyWhenUnambiguous()
    {
        await using var shop = await ShopAsync(postgres);
        var (_, category) = await BasicsAsync(shop);
        await ProductAsync(shop.Owner, shop.Id, ProductBody("Loose tea", category, 450));
        await ProductAsync(shop.Owner, shop.Id, ProductBody("Gift card", category, 1000));
        await ProductAsync(shop.Owner, shop.Id, ProductBody("Gift card", category, 2000));

        var preview = await JsonAsync(await PreviewAsync(shop.Owner, shop.Id, Csv(
            ",loose TEA,General,,,piece,4.75,false,1,1,,,0,true",
            ",Gift card,General,,,piece,10.00,false,1,1,,,0,true")));

        Assert.Equal(["update", "error"], preview["rows"]!.AsArray().Select(r => (string)r!["outcome"]!));
        Assert.Contains("add a SKU or barcode", (string)preview["rows"]![1]!["errors"]![0]!["message"]!, StringComparison.Ordinal);
    }

    internal static string Csv(params string[] rows) => Header + "\n" + string.Join("\n", rows) + "\n";

    internal static Task<HttpResponseMessage> PreviewAsync(HttpClient client, Guid shop, string csv) => SendFileAsync(client, shop, "/catalog/imports/preview", csv);

    internal static Task<HttpResponseMessage> ImportAsync(HttpClient client, Guid shop, string csv) => SendFileAsync(client, shop, "/catalog/imports", csv);

    internal static Task<HttpResponseMessage> ListImportsAsync(HttpClient client, Guid shop) =>
        Staff.StaffApi.SendAsync(client, HttpMethod.Get, "/catalog/imports", shop);

    /// <summary>A valid EAN-13 from a number (prefix 400, never the in-store range).</summary>
    internal static string Ean13(int n)
    {
        var body = "400" + n.ToString("D9", System.Globalization.CultureInfo.InvariantCulture);
        return body + HelloPos.Kernel.Catalog.BarcodeRules.CheckDigit(body).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<HttpResponseMessage> SendFileAsync(HttpClient client, Guid shop, string path, string csv)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add(Staff.StaffApi.BusinessHeader, shop.ToString());
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        var form = new MultipartFormDataContent { { file, "file", "catalog.csv" } };
        request.Content = form;
        return await client.SendAsync(request, Ct);
    }
}
