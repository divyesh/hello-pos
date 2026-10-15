using System.Net;
using System.Text.Json.Nodes;
using HelloPos.Api.Tests.Features.Staff;
using HelloPos.Api.Tests.Tenancy;
using Npgsql;

namespace HelloPos.Api.Tests.Features.BusinessSettings;

/// <summary>Calls and helpers for the settings tests (feature #12). Shops come from <c>CatalogApi.ShopAsync</c>.</summary>
internal static class SettingsApi
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static Task<HttpResponseMessage> ListPaymentTypesAsync(HttpClient client, Guid shop) =>
        StaffApi.SendAsync(client, HttpMethod.Get, "/settings/payment-types", shop);

    public static JsonObject PaymentTypeBody(string name, string kind, bool? opensDrawer = null, bool? asksReference = null)
    {
        var body = new JsonObject { ["name"] = name, ["kind"] = kind };
        if (opensDrawer is { } drawer)
        {
            body["opensDrawer"] = drawer;
        }

        if (asksReference is { } reference)
        {
            body["asksReference"] = reference;
        }

        return body;
    }

    public static Task<HttpResponseMessage> CreatePaymentTypeAsync(HttpClient client, Guid shop, JsonObject body) =>
        StaffApi.SendAsync(client, HttpMethod.Post, "/settings/payment-types", shop, body);

    /// <summary>Creates a type and returns (id, version).</summary>
    public static async Task<(Guid Id, int Version)> PaymentTypeAsync(HttpClient client, Guid shop, string name, string kind = "digitalWallet")
    {
        using var created = await CreatePaymentTypeAsync(client, shop, PaymentTypeBody(name, kind));
        var text = await created.Content.ReadAsStringAsync(Ct);
        Assert.True(created.StatusCode == HttpStatusCode.Created, text);
        var json = JsonNode.Parse(text)!;
        return (Guid.Parse((string)json["id"]!), (int)json["version"]!);
    }

    public static Task<HttpResponseMessage> UpdatePaymentTypeAsync(
        HttpClient client, Guid shop, Guid id, string name, string kind, bool opensDrawer, bool asksReference, int version) =>
        StaffApi.SendAsync(client, HttpMethod.Put, $"/settings/payment-types/{id}", shop, new JsonObject
        {
            ["name"] = name, ["kind"] = kind, ["opensDrawer"] = opensDrawer, ["asksReference"] = asksReference, ["version"] = version,
        });

    public static Task<HttpResponseMessage> SetPaymentTypeActiveAsync(HttpClient client, Guid shop, Guid id, bool active, int version) =>
        StaffApi.SendAsync(client, HttpMethod.Post, $"/settings/payment-types/{id}/{(active ? "reactivate" : "deactivate")}", shop,
            new JsonObject { ["version"] = version });

    public static Task<HttpResponseMessage> ReorderPaymentTypesAsync(HttpClient client, Guid shop, params Guid[] ids) =>
        StaffApi.SendAsync(client, HttpMethod.Put, "/settings/payment-types/order", shop,
            new JsonObject { ["ids"] = new JsonArray(ids.Select(i => (JsonNode)i.ToString()).ToArray()) });

    public static Task<HttpResponseMessage> GetSellingAsync(HttpClient client, Guid shop) =>
        StaffApi.SendAsync(client, HttpMethod.Get, "/settings/selling", shop);

    public static JsonObject SellingBody(JsonNode current) => new()
    {
        ["usesTables"] = (bool)current["usesTables"]!,
        ["discountLimitPercent"] = (string)current["discountLimitPercent"]!,
        ["discountLimitAmount"] = current["discountLimitAmount"]?.DeepClone(),
        ["tipsEnabled"] = (bool)current["tipsEnabled"]!,
        ["tipPresets"] = current["tipPresets"]!.DeepClone(),
        ["version"] = (int)current["version"]!,
    };

    public static Task<HttpResponseMessage> UpdateSellingAsync(HttpClient client, Guid shop, JsonObject body)
    {
        if (body["discountLimitAmount"] is null)
        {
            body.Remove("discountLimitAmount");
        }

        return StaffApi.SendAsync(client, HttpMethod.Put, "/settings/selling", shop, body);
    }

    public static Task<HttpResponseMessage> ListAreasAsync(HttpClient client, Guid shop) =>
        StaffApi.SendAsync(client, HttpMethod.Get, "/settings/tables/areas", shop);

    public static Task<HttpResponseMessage> CreateAreaAsync(HttpClient client, Guid shop, string name) =>
        StaffApi.SendAsync(client, HttpMethod.Post, "/settings/tables/areas", shop, new JsonObject { ["name"] = name });

    /// <summary>Creates an area and returns (id, version).</summary>
    public static Task<(Guid Id, int Version)> AreaAsync(HttpClient client, Guid shop, string name) =>
        IdAndVersionAsync(CreateAreaAsync(client, shop, name));

    public static Task<HttpResponseMessage> UpdateAreaAsync(HttpClient client, Guid shop, Guid id, string name, int position, int version) =>
        StaffApi.SendAsync(client, HttpMethod.Put, $"/settings/tables/areas/{id}", shop,
            new JsonObject { ["name"] = name, ["position"] = position, ["version"] = version });

    public static Task<HttpResponseMessage> SetAreaActiveAsync(HttpClient client, Guid shop, Guid id, bool active, int version) =>
        StaffApi.SendAsync(client, HttpMethod.Post, $"/settings/tables/areas/{id}/{(active ? "reactivate" : "deactivate")}", shop,
            new JsonObject { ["version"] = version });

    public static Task<HttpResponseMessage> CreateTableAsync(HttpClient client, Guid shop, Guid areaId, string name, int? seats = null)
    {
        var body = new JsonObject { ["areaId"] = areaId.ToString(), ["name"] = name };
        if (seats is { } count)
        {
            body["seats"] = count;
        }

        return StaffApi.SendAsync(client, HttpMethod.Post, "/settings/tables", shop, body);
    }

    /// <summary>Creates a table and returns (id, version).</summary>
    public static Task<(Guid Id, int Version)> TableAsync(HttpClient client, Guid shop, Guid areaId, string name, int? seats = null) =>
        IdAndVersionAsync(CreateTableAsync(client, shop, areaId, name, seats));

    public static Task<HttpResponseMessage> CreateTablesAsync(
        HttpClient client, Guid shop, Guid areaId, int from, int to, string? prefix = null, string? suffix = null, int? seats = null)
    {
        var body = new JsonObject { ["areaId"] = areaId.ToString(), ["from"] = from, ["to"] = to };
        if (prefix is not null)
        {
            body["prefix"] = prefix;
        }

        if (suffix is not null)
        {
            body["suffix"] = suffix;
        }

        if (seats is { } count)
        {
            body["seats"] = count;
        }

        return StaffApi.SendAsync(client, HttpMethod.Post, "/settings/tables/bulk", shop, body);
    }

    public static Task<HttpResponseMessage> UpdateTableAsync(
        HttpClient client, Guid shop, Guid id, Guid areaId, string name, int seats, int position, int version) =>
        StaffApi.SendAsync(client, HttpMethod.Put, $"/settings/tables/{id}", shop, new JsonObject
        {
            ["areaId"] = areaId.ToString(), ["name"] = name, ["seats"] = seats, ["position"] = position, ["version"] = version,
        });

    public static Task<HttpResponseMessage> SetTableActiveAsync(HttpClient client, Guid shop, Guid id, bool active, int version) =>
        StaffApi.SendAsync(client, HttpMethod.Post, $"/settings/tables/{id}/{(active ? "reactivate" : "deactivate")}", shop,
            new JsonObject { ["version"] = version });

    public static Task<HttpResponseMessage> ListChangesAsync(HttpClient client, Guid shop, string query = "") =>
        StaffApi.SendAsync(client, HttpMethod.Get, "/settings/changes" + query, shop);

    private static async Task<(Guid Id, int Version)> IdAndVersionAsync(Task<HttpResponseMessage> request)
    {
        using var created = await request;
        var text = await created.Content.ReadAsStringAsync(Ct);
        Assert.True(created.StatusCode == HttpStatusCode.Created, text);
        var json = JsonNode.Parse(text)!;
        return (Guid.Parse((string)json["id"]!), (int)json["version"]!);
    }

    /// <summary>Settings history as "entity:action" in time order (read as the migrator).</summary>
    public static async Task<List<string>> SettingsHistoryAsync(TenancyApp app)
    {
        await using var connection = new NpgsqlConnection(app.Logins.Migrator);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("select entity || ':' || action from pos.settings_changes order by at, id", connection);
        var result = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }
}
