using System.Net;
using HelloPos.Api.Tests.Infrastructure.Database;
using HelloPos.Kernel.Staff;
using static HelloPos.Api.Tests.Features.Catalog.CatalogApi;
using static HelloPos.Api.Tests.Features.Owners.OwnerApi;

namespace HelloPos.Api.Tests.Features.Catalog;

/// <summary>US1 categories: FR-001, FR-003, FR-009, FR-010, FR-015.</summary>
[Collection(PostgresTests.Name)]
public sealed class CategoryTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Manager_CreatesCategories_ListedInSortOrder()
    {
        await using var shop = await ShopAsync(postgres);
        var (gst, _) = await TaxProfileAsync(shop.Owner, shop.Id);

        var drinks = await JsonAsync(await AssertContractAsync(await CreateCategoryAsync(shop.Manager, shop.Id, "Beverages", gst, sortOrder: 2), HttpStatusCode.Created, "createCategory"));
        await CategoryAsync(shop.Manager, shop.Id, "Produce", gst, sortOrder: 1);
        await CategoryAsync(shop.Manager, shop.Id, "Bakery", gst, sortOrder: 2);

        Assert.Equal(gst.ToString(), (string?)drinks["defaultTaxProfileId"]);
        var listed = await JsonAsync(await AssertContractAsync(await ListCategoriesAsync(shop.Manager, shop.Id), HttpStatusCode.OK, "listCategories"));
        Assert.Equal(["Produce", "Bakery", "Beverages"], listed.AsArray().Select(c => (string)c!["name"]!));
        Assert.Equal(3, (await HistoryAsync(shop.App)).Count(h => h == "category:created"));
    }

    [Fact]
    public async Task Names_AreUniqueIgnoringCase_AndRequired()
    {
        await using var shop = await ShopAsync(postgres);
        var (gst, _) = await TaxProfileAsync(shop.Owner, shop.Id);
        await CategoryAsync(shop.Owner, shop.Id, "Snacks", gst);

        var duplicate = await JsonAsync(await AssertContractAsync(await CreateCategoryAsync(shop.Owner, shop.Id, " SNACKS", gst), HttpStatusCode.BadRequest, "createCategory"));
        var empty = await JsonAsync(await AssertContractAsync(await CreateCategoryAsync(shop.Owner, shop.Id, " ", gst), HttpStatusCode.BadRequest, "createCategory"));

        Assert.NotNull(duplicate["errors"]!["name"]);
        Assert.NotNull(empty["errors"]!["name"]);
    }

    [Fact]
    public async Task TheDefaultTaxProfile_MustExist_AndBeActive()
    {
        await using var shop = await ShopAsync(postgres);
        var (gst, version) = await TaxProfileAsync(shop.Owner, shop.Id);
        await SetTaxProfileActiveAsync(shop.Owner, shop.Id, gst, false, version);

        var inactive = await JsonAsync(await AssertContractAsync(await CreateCategoryAsync(shop.Owner, shop.Id, "Snacks", gst), HttpStatusCode.BadRequest, "createCategory"));
        var unknown = await JsonAsync(await AssertContractAsync(await CreateCategoryAsync(shop.Owner, shop.Id, "Snacks", Guid.CreateVersion7()), HttpStatusCode.BadRequest, "createCategory"));

        Assert.NotNull(inactive["errors"]!["defaultTaxProfileId"]);
        Assert.NotNull(unknown["errors"]!["defaultTaxProfileId"]);
    }

    [Fact]
    public async Task Update_Deactivate_Reactivate_WithStaleVersionsRefused()
    {
        await using var shop = await ShopAsync(postgres);
        var (gst, _) = await TaxProfileAsync(shop.Owner, shop.Id);
        var (exempt, _) = await TaxProfileAsync(shop.Owner, shop.Id, "Exempt", "exempt");
        var (id, version) = await CategoryAsync(shop.Manager, shop.Id, "Snacks", gst);

        var updated = await JsonAsync(await AssertContractAsync(await UpdateCategoryAsync(shop.Manager, shop.Id, id, "Chips", 5, exempt, version), HttpStatusCode.OK, "updateCategory"));
        Assert.Equal(exempt.ToString(), (string?)updated["defaultTaxProfileId"]);
        await AssertContractAsync(await UpdateCategoryAsync(shop.Manager, shop.Id, id, "Stale", 5, exempt, version), HttpStatusCode.Conflict, "updateCategory");

        var off = await JsonAsync(await AssertContractAsync(await SetCategoryActiveAsync(shop.Manager, shop.Id, id, false, (int)updated["version"]!), HttpStatusCode.OK, "deactivateCategory"));
        Assert.Empty((await JsonAsync(await ListCategoriesAsync(shop.Manager, shop.Id, "?active=true"))).AsArray());
        Assert.Single((await JsonAsync(await ListCategoriesAsync(shop.Manager, shop.Id, "?active=false"))).AsArray());
        await AssertContractAsync(await SetCategoryActiveAsync(shop.Manager, shop.Id, id, true, (int)off["version"]!), HttpStatusCode.OK, "reactivateCategory");

        Assert.Equal(["category:created", "category:updated", "category:deactivated", "category:reactivated"],
            (await HistoryAsync(shop.App)).Where(h => h.StartsWith("category", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Cashiers_CannotEvenOpenTheBackOffice()
    {
        await using var shop = await ShopAsync(postgres);
        using var cashier = await shop.App.SignUpVerifiedOwnerAsync("cashier@example.com");
        await shop.App.AddStaffAsync(shop.Id, StaffRole.Cashier, await HelloPos.Api.Tests.Tenancy.TenancyApp.AccountIdAsync(cashier), "Cash");

        // Feature 006: a cashier row grants no back-office access, so there is no catalog to reach.
        Assert.Equal(HttpStatusCode.NotFound, (await HelloPos.Api.Tests.Features.Businesses.OpenAndEditTests.OpenAsync(cashier, shop.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ListCategoriesAsync(cashier, shop.Id)).StatusCode);
    }

    [Fact]
    public async Task Unknown_Is404()
    {
        await using var shop = await ShopAsync(postgres);

        await AssertContractAsync(await GetCategoryAsync(shop.Owner, shop.Id, Guid.CreateVersion7()), HttpStatusCode.NotFound, "getCategory");
    }
}
