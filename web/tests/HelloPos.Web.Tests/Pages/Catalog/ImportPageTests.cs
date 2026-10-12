using Bunit;
using HelloPos.Kernel.Staff;
using HelloPos.Web.Pages.Catalog;
using HelloPos.Web.Services;
using HelloPos.Web.Tests.Fakes;
using HelloPos.Web.Tests.Pages.Owners;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace HelloPos.Web.Tests.Pages.Catalog;

/// <summary>Feature #11 US4 web: upload, preview with errors, import only when clean.</summary>
public sealed class ImportPageTests : BunitContext
{
    private static readonly Guid Shop = Guid.Parse("0199d1c4-8000-7000-8000-000000000201");

    private readonly FakeCatalogService catalog = new();

    public ImportPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton<ICatalogService>(catalog);
        Services.AddSingleton<IBusinessService>(new FakeBusinessService());
        Services.AddSingleton<IOwnerAccountService>(new FakeOwnerAccountService());
        Services.AddSingleton<OpenBusinessState>();
    }

    [Fact]
    public async Task ACleanFile_IsPreviewed_ThenImported()
    {
        catalog.NextPreview = new LoadResult<ImportPreviewItem>.Loaded(new ImportPreviewItem(
            [new ImportRowItem(2, "create", "COLA", "Cola", []), new ImportRowItem(3, "unchanged", "TEA", "Tea", [])], 1, 0, 1, 0, null));
        catalog.NextImport = new LoadResult<ImportResultItem>.Loaded(new ImportResultItem(2, 1, 0, 1));
        await OpenAsync();
        var page = Render<Import>();

        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("sku,name\nCOLA,Cola\n", "catalog.csv"));

        Assert.Equal("catalog.csv", Assert.Single(catalog.Uploaded).Name);
        Assert.Equal(["create", "unchanged"], page.FindAll("[data-testid=outcome]").Select(e => e.TextContent));
        page.Find("[data-testid=import]").Click();
        Assert.Equal(["import catalog.csv"], catalog.Calls);
        Assert.Contains("1 new, 0 changed, 1 unchanged", page.Find("[data-testid=message]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Errors_AreShownFirst_AndBlockTheImport()
    {
        catalog.NextPreview = new LoadResult<ImportPreviewItem>.Loaded(new ImportPreviewItem(
            [new ImportRowItem(2, "create", "OK", "Fine", []), new ImportRowItem(3, "error", "BAD", "Bad", [("price", "Use a dot as the decimal separator, for example 1.99.")])], 1, 0, 0, 1, null));
        await OpenAsync();
        var page = Render<Import>();

        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("x", "catalog.csv"));

        Assert.True(page.Find("[data-testid=import]").HasAttribute("disabled"));
        Assert.Equal(["error"], page.FindAll("[data-testid=outcome]").Select(e => e.TextContent));
        Assert.Contains("price: Use a dot", page.Find("[data-testid=problems]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFileLevelProblem_IsShown()
    {
        catalog.NextPreview = new LoadResult<ImportPreviewItem>.Loaded(new ImportPreviewItem([], 0, 0, 0, 0, "Missing columns: sku, unit."));
        await OpenAsync();
        var page = Render<Import>();

        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("name\n", "catalog.csv"));

        Assert.Contains("Missing columns", page.Find("[data-testid=message]").TextContent, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("[data-testid=import]"));
    }

    [Fact]
    public async Task TheTemplate_HasTheHeader()
    {
        await OpenAsync();
        var page = Render<Import>();

        Assert.Contains("sku%2Cname%2Ccategory", page.Find("[data-testid=template]").GetAttribute("href"), StringComparison.Ordinal);
    }

    private async Task OpenAsync() =>
        await Services.GetRequiredService<OpenBusinessState>().OpenAsync(Shop, "Shop", StaffRole.Manager);
}
