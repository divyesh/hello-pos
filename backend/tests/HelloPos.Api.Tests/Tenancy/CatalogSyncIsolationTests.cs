using System.Net;
using System.Text.Json.Nodes;
using HelloPos.Api.Tests.Features.Catalog;
using HelloPos.Api.Tests.Infrastructure.Database;
using static HelloPos.Api.Tests.Features.Businesses.OpenAndEditTests;
using static HelloPos.Api.Tests.Features.Catalog.CatalogApi;
using static HelloPos.Api.Tests.Features.Pos.CatalogSyncTests;
using static HelloPos.Api.Tests.Features.Pos.PosApi;

namespace HelloPos.Api.Tests.Tenancy;

/// <summary>
/// Feature #13 FR-002, FR-021 (Constitution III): a till receives only the catalog of the business it is
/// paired to, in full and change downloads, whatever it sends.
/// </summary>
[Collection(PostgresTests.Name)]
public sealed class CatalogSyncIsolationTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ATill_NeverReceivesAnotherBusinesssCatalog()
    {
        await using var shop = await ShopAsync(postgres);
        var (profile, category) = await BasicsAsync(shop);
        var (ours, _) = await ProductAsync(shop.Owner, shop.Id, ProductBody("Our cola", category, 199, "067000004568"));
        using var device = await RegisterDeviceAsync(shop.App, shop.Owner, shop.Id);
        using var till = DeviceClient(shop.App, device.Credential);
        var since = (long)JsonNode.Parse(await (await SyncAsync(till)).Content.ReadAsStringAsync(Ct))!["revision"]!;

        // The same owner's second business gets its own catalog, with the same barcode, after the till's read.
        var other = await CreateIdAsync(shop.Owner, "Other shop");
        await OpenAsync(shop.Owner, other);
        var (theirProfile, _) = await TaxProfileAsync(shop.Owner, other, "Their GST");
        var (theirCategory, _) = await CategoryAsync(shop.Owner, other, "Their drinks", theirProfile);
        var (theirs, _) = await ProductAsync(shop.Owner, other, ProductBody("Their cola", theirCategory, 99, "067000004568"));
        await CreateDiscountAsync(shop.Owner, other, theirs, DiscountBody("percent", "50", Today(shop.App)));

        foreach (var request in new long?[] { null, since, 0 })
        {
            using var response = await SyncAsync(till, request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var text = await response.Content.ReadAsStringAsync(Ct);
            var catalog = JsonNode.Parse(text)!;

            Assert.DoesNotContain(theirs.ToString(), text, StringComparison.Ordinal);
            Assert.DoesNotContain(theirProfile.ToString(), text, StringComparison.Ordinal);
            Assert.DoesNotContain(theirCategory.ToString(), text, StringComparison.Ordinal);
            Assert.DoesNotContain("Their", text, StringComparison.Ordinal);
            Assert.Empty(catalog["discounts"]!.AsArray());
            Assert.All(catalog["products"]!.AsArray(), p => Assert.Equal(ours, Guid.Parse((string)p!["id"]!)));
            Assert.All(catalog["taxProfiles"]!.AsArray(), p => Assert.Equal(profile, Guid.Parse((string)p!["id"]!)));
        }

        // And the other business's till sees only its own.
        using var theirDevice = await RegisterDeviceAsync(shop.App, shop.Owner, other, "Their till");
        using var theirTill = DeviceClient(shop.App, theirDevice.Credential);
        var theirCatalog = await (await SyncAsync(theirTill)).Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain(ours.ToString(), theirCatalog, StringComparison.Ordinal);
        Assert.Contains(theirs.ToString(), theirCatalog, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARevokedTill_GetsNothing()
    {
        await using var shop = await ShopAsync(postgres);
        await BasicsAsync(shop);
        using var device = await RegisterDeviceAsync(shop.App, shop.Owner, shop.Id);
        using (await RevokeAsync(shop.Owner, shop.Id, device.DeviceId, 1))
        {
        }

        using var till = DeviceClient(shop.App, device.Credential);
        using var response = await SyncAsync(till);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("General", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }
}
