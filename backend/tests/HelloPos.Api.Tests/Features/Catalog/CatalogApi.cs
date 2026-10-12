using System.Net;
using System.Text.Json.Nodes;
using HelloPos.Api.Tests.Features.Staff;
using HelloPos.Api.Tests.Infrastructure.Database;
using HelloPos.Api.Tests.Tenancy;
using HelloPos.Kernel.Staff;
using Npgsql;
using static HelloPos.Api.Tests.Features.Businesses.OpenAndEditTests;

namespace HelloPos.Api.Tests.Features.Catalog;

/// <summary>An owner and a manager of one open shop (INR, tax-inclusive, Asia/Kolkata), for catalog tests.</summary>
internal sealed record CatalogShop(TenancyApp App, HttpClient Owner, HttpClient Manager, Guid Id) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        Owner.Dispose();
        Manager.Dispose();
        await App.DisposeAsync();
    }
}

/// <summary>Calls and helpers for the catalog tests (feature #11).</summary>
internal static class CatalogApi
{
    public static readonly TimeZoneInfo ShopZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<CatalogShop> ShopAsync(PostgresFixture postgres)
    {
        var app = await TenancyApp.CreateAsync(postgres);
        var owner = await app.SignUpVerifiedOwnerAsync("owner@example.com");
        var id = await CreateIdAsync(owner, "Shop");
        await OpenAsync(owner, id);
        var (manager, _) = await app.SignedInStaffClientAsync("manager@example.com", id, StaffRole.Manager);
        return new CatalogShop(app, owner, manager, id);
    }

    /// <summary>Today in the shop's time zone, by the test clock.</summary>
    public static DateOnly Today(TenancyApp app) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(app.Clock.GetUtcNow(), ShopZone).DateTime);

    public static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public static JsonArray Components(params (string Label, string Rate)[] components) =>
        new(components.Select(c => (JsonNode)new JsonObject { ["label"] = c.Label, ["ratePercent"] = c.Rate }).ToArray());

    public static Task<HttpResponseMessage> CreateTaxProfileAsync(
        HttpClient client, Guid shop, string name, string kind, JsonArray components, DateOnly? effectiveFrom = null)
    {
        var body = new JsonObject { ["name"] = name, ["kind"] = kind, ["components"] = components };
        if (effectiveFrom is { } date)
        {
            body["effectiveFrom"] = Day(date);
        }

        return StaffApi.SendAsync(client, HttpMethod.Post, "/catalog/tax-profiles", shop, body);
    }

    /// <summary>Creates a profile and returns (id, version).</summary>
    public static async Task<(Guid Id, int Version)> TaxProfileAsync(
        HttpClient owner, Guid shop, string name = "GST 5%", string kind = "taxable", JsonArray? components = null)
    {
        using var created = await CreateTaxProfileAsync(owner, shop, name, kind,
            components ?? (kind == "taxable" ? Components(("CGST", "2.5"), ("SGST", "2.5")) : []));
        var body = await created.Content.ReadAsStringAsync(Ct);
        Assert.True(created.StatusCode == HttpStatusCode.Created, body);
        var json = JsonNode.Parse(body)!;
        return (Guid.Parse((string)json["id"]!), (int)json["version"]!);
    }

    public static Task<HttpResponseMessage> ListTaxProfilesAsync(HttpClient client, Guid shop, string query = "") =>
        StaffApi.SendAsync(client, HttpMethod.Get, "/catalog/tax-profiles" + query, shop);

    public static Task<HttpResponseMessage> GetTaxProfileAsync(HttpClient client, Guid shop, Guid id) =>
        StaffApi.SendAsync(client, HttpMethod.Get, $"/catalog/tax-profiles/{id}", shop);

    public static Task<HttpResponseMessage> RenameTaxProfileAsync(HttpClient client, Guid shop, Guid id, string name, int version) =>
        StaffApi.SendAsync(client, HttpMethod.Put, $"/catalog/tax-profiles/{id}", shop, new JsonObject { ["name"] = name, ["version"] = version });

    public static Task<HttpResponseMessage> AddVersionAsync(HttpClient client, Guid shop, Guid id, DateOnly from, JsonArray components, int version) =>
        StaffApi.SendAsync(client, HttpMethod.Post, $"/catalog/tax-profiles/{id}/versions", shop,
            new JsonObject { ["effectiveFrom"] = Day(from), ["components"] = components, ["version"] = version });

    public static Task<HttpResponseMessage> UpdateVersionAsync(HttpClient client, Guid shop, Guid id, Guid versionId, DateOnly from, JsonArray components, int version) =>
        StaffApi.SendAsync(client, HttpMethod.Put, $"/catalog/tax-profiles/{id}/versions/{versionId}", shop,
            new JsonObject { ["effectiveFrom"] = Day(from), ["components"] = components, ["version"] = version });

    public static Task<HttpResponseMessage> DeleteVersionAsync(HttpClient client, Guid shop, Guid id, Guid versionId) =>
        StaffApi.SendAsync(client, HttpMethod.Delete, $"/catalog/tax-profiles/{id}/versions/{versionId}", shop);

    public static Task<HttpResponseMessage> SetTaxProfileActiveAsync(HttpClient client, Guid shop, Guid id, bool active, int version) =>
        StaffApi.SendAsync(client, HttpMethod.Post, $"/catalog/tax-profiles/{id}/{(active ? "reactivate" : "deactivate")}", shop,
            new JsonObject { ["version"] = version });

    public static Task<HttpResponseMessage> CreateCategoryAsync(HttpClient client, Guid shop, string name, Guid taxProfileId, int sortOrder = 0) =>
        StaffApi.SendAsync(client, HttpMethod.Post, "/catalog/categories", shop,
            new JsonObject { ["name"] = name, ["sortOrder"] = sortOrder, ["defaultTaxProfileId"] = taxProfileId });

    public static async Task<(Guid Id, int Version)> CategoryAsync(HttpClient client, Guid shop, string name, Guid taxProfileId, int sortOrder = 0)
    {
        using var created = await CreateCategoryAsync(client, shop, name, taxProfileId, sortOrder);
        var body = await created.Content.ReadAsStringAsync(Ct);
        Assert.True(created.StatusCode == HttpStatusCode.Created, body);
        var json = JsonNode.Parse(body)!;
        return (Guid.Parse((string)json["id"]!), (int)json["version"]!);
    }

    public static Task<HttpResponseMessage> ListCategoriesAsync(HttpClient client, Guid shop, string query = "") =>
        StaffApi.SendAsync(client, HttpMethod.Get, "/catalog/categories" + query, shop);

    public static Task<HttpResponseMessage> GetCategoryAsync(HttpClient client, Guid shop, Guid id) =>
        StaffApi.SendAsync(client, HttpMethod.Get, $"/catalog/categories/{id}", shop);

    public static Task<HttpResponseMessage> UpdateCategoryAsync(HttpClient client, Guid shop, Guid id, string name, int sortOrder, Guid taxProfileId, int version) =>
        StaffApi.SendAsync(client, HttpMethod.Put, $"/catalog/categories/{id}", shop,
            new JsonObject { ["name"] = name, ["sortOrder"] = sortOrder, ["defaultTaxProfileId"] = taxProfileId, ["version"] = version });

    public static Task<HttpResponseMessage> SetCategoryActiveAsync(HttpClient client, Guid shop, Guid id, bool active, int version) =>
        StaffApi.SendAsync(client, HttpMethod.Post, $"/catalog/categories/{id}/{(active ? "reactivate" : "deactivate")}", shop,
            new JsonObject { ["version"] = version });

    /// <summary>A product body; override fields with the indexer before sending.</summary>
    public static JsonObject ProductBody(string name, Guid categoryId, long? unitPrice = 199, params string[] barcodes)
    {
        var body = new JsonObject
        {
            ["name"] = name,
            ["categoryId"] = categoryId,
            ["unit"] = "piece",
            ["openPrice"] = unitPrice is null,
            ["minQuantity"] = "1",
            ["step"] = "1",
            ["depositPerUnit"] = 0,
            ["barcodes"] = new JsonArray(barcodes.Select(b => (JsonNode)b).ToArray()),
        };
        if (unitPrice is { } price)
        {
            body["unitPrice"] = price;
        }

        return body;
    }

    public static Task<HttpResponseMessage> CreateProductAsync(HttpClient client, Guid shop, JsonObject body) =>
        StaffApi.SendAsync(client, HttpMethod.Post, "/catalog/products", shop, body);

    public static async Task<(Guid Id, int Version)> ProductAsync(HttpClient client, Guid shop, JsonObject body)
    {
        using var created = await CreateProductAsync(client, shop, body);
        var text = await created.Content.ReadAsStringAsync(Ct);
        Assert.True(created.StatusCode == HttpStatusCode.Created, text);
        var json = JsonNode.Parse(text)!;
        return (Guid.Parse((string)json["id"]!), (int)json["version"]!);
    }

    public static Task<HttpResponseMessage> GetProductAsync(HttpClient client, Guid shop, Guid id) =>
        StaffApi.SendAsync(client, HttpMethod.Get, $"/catalog/products/{id}", shop);

    public static Task<HttpResponseMessage> UpdateProductAsync(HttpClient client, Guid shop, Guid id, JsonObject body, int version)
    {
        var update = (JsonObject)body.DeepClone();
        update["version"] = version;
        return StaffApi.SendAsync(client, HttpMethod.Put, $"/catalog/products/{id}", shop, update);
    }

    public static Task<HttpResponseMessage> ListProductsAsync(HttpClient client, Guid shop, string query = "") =>
        StaffApi.SendAsync(client, HttpMethod.Get, "/catalog/products" + query, shop);

    public static Task<HttpResponseMessage> SetProductActiveAsync(HttpClient client, Guid shop, Guid id, bool active, int version) =>
        StaffApi.SendAsync(client, HttpMethod.Post, $"/catalog/products/{id}/{(active ? "reactivate" : "deactivate")}", shop,
            new JsonObject { ["version"] = version });

    /// <summary>A shop with a GST 5% profile and a "General" category, as (profile, category).</summary>
    public static async Task<(Guid Profile, Guid Category)> BasicsAsync(CatalogShop shop)
    {
        var (profile, _) = await TaxProfileAsync(shop.Owner, shop.Id);
        var (category, _) = await CategoryAsync(shop.Owner, shop.Id, "General", profile);
        return (profile, category);
    }

    public static JsonObject DiscountBody(string kind, string value, DateOnly from, DateOnly? to = null)
    {
        var body = new JsonObject { ["kind"] = kind, ["value"] = value, ["fromDate"] = Day(from) };
        if (to is { } end)
        {
            body["toDate"] = Day(end);
        }

        return body;
    }

    public static Task<HttpResponseMessage> CreateDiscountAsync(HttpClient client, Guid shop, Guid product, JsonObject body) =>
        StaffApi.SendAsync(client, HttpMethod.Post, $"/catalog/products/{product}/discounts", shop, body);

    public static Task<HttpResponseMessage> ListDiscountsAsync(HttpClient client, Guid shop, Guid product) =>
        StaffApi.SendAsync(client, HttpMethod.Get, $"/catalog/products/{product}/discounts", shop);

    public static Task<HttpResponseMessage> UpdateDiscountAsync(HttpClient client, Guid shop, Guid product, Guid discount, JsonObject body) =>
        StaffApi.SendAsync(client, HttpMethod.Put, $"/catalog/products/{product}/discounts/{discount}", shop, body);

    public static Task<HttpResponseMessage> DeleteDiscountAsync(HttpClient client, Guid shop, Guid product, Guid discount) =>
        StaffApi.SendAsync(client, HttpMethod.Delete, $"/catalog/products/{product}/discounts/{discount}", shop);

    /// <summary>Catalog history as "entity:action" in time order (read as the migrator).</summary>
    public static async Task<List<string>> HistoryAsync(TenancyApp app)
    {
        await using var connection = new NpgsqlConnection(app.Logins.Migrator);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("select entity || ':' || action from pos.catalog_changes order by at, id", connection);
        var result = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }

    public static async Task<JsonNode> JsonAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!;

    public static Task<JsonNode> JsonAsync(string body) => Task.FromResult(JsonNode.Parse(body)!);
}
