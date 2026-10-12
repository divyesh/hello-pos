using HelloPos.Web.Services;

namespace HelloPos.Web.Tests.Fakes;

/// <summary>In-memory catalog for page tests; the next change result can be set.</summary>
public sealed class FakeCatalogService : ICatalogService
{
    public List<TaxProfileItem> Profiles { get; } = [];

    public List<CategoryItem> Categories { get; } = [];

    public List<string> Calls { get; } = [];

    public OwnerActionResult NextChange { get; set; } = OwnerActionResult.Success;

    public static TaxProfileItem Profile(string name, string kind = "taxable", bool active = true, params TaxComponentItem[] components) =>
        new(Guid.NewGuid(), name, kind, active, 1,
            [new TaxProfileVersionItem(Guid.NewGuid(), new DateOnly(2026, 1, 1), components, Current: true, Editable: false)]);

    public Task<LoadResult<IReadOnlyList<TaxProfileItem>>> ListTaxProfilesAsync(Guid pageBusiness, CancellationToken cancellationToken = default) =>
        Task.FromResult<LoadResult<IReadOnlyList<TaxProfileItem>>>(new LoadResult<IReadOnlyList<TaxProfileItem>>.Loaded(Profiles.ToList()));

    public Task<OwnerActionResult> CreateTaxProfileAsync(Guid pageBusiness, string name, string kind, IReadOnlyList<TaxComponentItem> components, CancellationToken cancellationToken = default) =>
        Change($"create-profile {name} {kind} {string.Join("+", components.Select(c => $"{c.Label}:{c.RatePercent}"))}",
            () => Profiles.Add(Profile(name, kind, true, [.. components])));

    public Task<OwnerActionResult> RenameTaxProfileAsync(Guid pageBusiness, TaxProfileItem profile, string name, CancellationToken cancellationToken = default) =>
        Change($"rename-profile {profile.Name} {name}", () => Replace(profile, profile with { Name = name }));

    public Task<OwnerActionResult> AddTaxVersionAsync(Guid pageBusiness, TaxProfileItem profile, DateOnly effectiveFrom, IReadOnlyList<TaxComponentItem> components, CancellationToken cancellationToken = default) =>
        Change($"add-version {profile.Name} {effectiveFrom:yyyy-MM-dd} {string.Join("+", components.Select(c => $"{c.Label}:{c.RatePercent}"))}",
            () => Replace(profile, profile with { Versions = [.. profile.Versions, new TaxProfileVersionItem(Guid.NewGuid(), effectiveFrom, components, false, true)] }));

    public Task<OwnerActionResult> DeleteTaxVersionAsync(Guid pageBusiness, TaxProfileItem profile, Guid versionId, CancellationToken cancellationToken = default) =>
        Change($"delete-version {profile.Name}", () => Replace(profile, profile with { Versions = profile.Versions.Where(v => v.Id != versionId).ToList() }));

    public Task<OwnerActionResult> SetTaxProfileActiveAsync(Guid pageBusiness, TaxProfileItem profile, bool active, CancellationToken cancellationToken = default) =>
        Change($"{(active ? "reactivate" : "deactivate")}-profile {profile.Name}", () => Replace(profile, profile with { Active = active }));

    public Task<LoadResult<IReadOnlyList<CategoryItem>>> ListCategoriesAsync(Guid pageBusiness, CancellationToken cancellationToken = default) =>
        Task.FromResult<LoadResult<IReadOnlyList<CategoryItem>>>(new LoadResult<IReadOnlyList<CategoryItem>>.Loaded(
            Categories.OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToList()));

    public Task<OwnerActionResult> CreateCategoryAsync(Guid pageBusiness, string name, int sortOrder, Guid taxProfileId, CancellationToken cancellationToken = default) =>
        Change($"create-category {name} {sortOrder} {Profiles.Single(p => p.Id == taxProfileId).Name}",
            () => Categories.Add(new CategoryItem(Guid.NewGuid(), name, sortOrder, taxProfileId, true, 1)));

    public Task<OwnerActionResult> UpdateCategoryAsync(Guid pageBusiness, CategoryItem category, string name, int sortOrder, Guid taxProfileId, CancellationToken cancellationToken = default) =>
        Change($"update-category {category.Name} -> {name} {sortOrder} v{category.Version}", () =>
        {
            var i = Categories.FindIndex(c => c.Id == category.Id);
            Categories[i] = category with { Name = name, SortOrder = sortOrder, DefaultTaxProfileId = taxProfileId, Version = category.Version + 1 };
        });

    public Task<OwnerActionResult> SetCategoryActiveAsync(Guid pageBusiness, CategoryItem category, bool active, CancellationToken cancellationToken = default) =>
        Change($"{(active ? "reactivate" : "deactivate")}-category {category.Name}", () =>
        {
            var i = Categories.FindIndex(c => c.Id == category.Id);
            Categories[i] = category with { Active = active, Version = category.Version + 1 };
        });

    public List<ProductItem> Products { get; } = [];

    public List<(string? Search, Guid? Category, bool? Active, int Page)> Searches { get; } = [];

    public List<ProductForm> Saved { get; } = [];

    public static ProductItem Product(string name, Guid categoryId, long? price = 199, string currency = "CAD", params string[] barcodes) =>
        new(Guid.NewGuid(), name, null, categoryId, null, Guid.Empty, "piece", price, price is null, "1", "1", null, 0, barcodes, currency, true, 1);

    public Task<LoadResult<ProductPageItem>> ListProductsAsync(Guid pageBusiness, string? search, Guid? categoryId, bool? active, int page, CancellationToken cancellationToken = default)
    {
        Searches.Add((search, categoryId, active, page));
        var matching = Products.Where(p => (string.IsNullOrEmpty(search) || p.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            && (categoryId == null || p.CategoryId == categoryId) && (active == null || p.Active == active)).ToList();
        return Task.FromResult<LoadResult<ProductPageItem>>(new LoadResult<ProductPageItem>.Loaded(
            new ProductPageItem(matching.Skip((page - 1) * 2).Take(2).ToList(), page, 2, matching.Count)));
    }

    public Task<LoadResult<ProductItem>> GetProductAsync(Guid pageBusiness, Guid productId, CancellationToken cancellationToken = default) =>
        Task.FromResult<LoadResult<ProductItem>>(Products.FirstOrDefault(p => p.Id == productId) is { } product
            ? new LoadResult<ProductItem>.Loaded(product)
            : new LoadResult<ProductItem>.Failed("Not found."));

    public Task<OwnerActionResult> CreateProductAsync(Guid pageBusiness, ProductForm form, CancellationToken cancellationToken = default) =>
        Change($"create-product {form.Name}", () => Saved.Add(form));

    public Task<OwnerActionResult> UpdateProductAsync(Guid pageBusiness, ProductItem product, ProductForm form, CancellationToken cancellationToken = default) =>
        Change($"update-product {product.Name} v{product.Version}", () =>
        {
            Saved.Add(form);
            Products[Products.FindIndex(p => p.Id == product.Id)] = product with
            {
                Name = form.Name, UnitPrice = form.UnitPrice, Barcodes = form.Barcodes, Version = product.Version + 1,
            };
        });

    public Task<OwnerActionResult> SetProductActiveAsync(Guid pageBusiness, ProductItem product, bool active, CancellationToken cancellationToken = default) =>
        Change($"{(active ? "reactivate" : "deactivate")}-product {product.Name}",
            () => Products[Products.FindIndex(p => p.Id == product.Id)] = product with { Active = active, Version = product.Version + 1 });

    public List<DiscountItem> Discounts { get; } = [];

    public Task<LoadResult<IReadOnlyList<DiscountItem>>> ListDiscountsAsync(Guid pageBusiness, Guid productId, CancellationToken cancellationToken = default) =>
        Task.FromResult<LoadResult<IReadOnlyList<DiscountItem>>>(new LoadResult<IReadOnlyList<DiscountItem>>.Loaded(Discounts.ToList()));

    public Task<OwnerActionResult> SaveDiscountAsync(Guid pageBusiness, Guid productId, Guid? discountId, string kind, string value, DateOnly fromDate, DateOnly? toDate, CancellationToken cancellationToken = default) =>
        Change($"{(discountId is null ? "create" : "update")}-discount {kind} {value} {fromDate:yyyy-MM-dd} {toDate:yyyy-MM-dd}".TrimEnd(), () =>
        {
            if (discountId is { } id)
            {
                var i = Discounts.FindIndex(d => d.Id == id);
                Discounts[i] = Discounts[i] with { To = toDate };
            }
            else
            {
                Discounts.Add(new DiscountItem(Guid.NewGuid(), kind, value, fromDate, toDate, "future"));
            }
        });

    public Task<OwnerActionResult> DeleteDiscountAsync(Guid pageBusiness, Guid productId, Guid discountId, CancellationToken cancellationToken = default) =>
        Change("delete-discount", () => Discounts.RemoveAll(d => d.Id == discountId));

    public LoadResult<ImportPreviewItem>? NextPreview { get; set; }

    public LoadResult<ImportResultItem>? NextImport { get; set; }

    public List<(string Name, string Content)> Uploaded { get; } = [];

    public Task<LoadResult<ImportPreviewItem>> PreviewImportAsync(Guid pageBusiness, string fileName, byte[] content, CancellationToken cancellationToken = default)
    {
        Uploaded.Add((fileName, System.Text.Encoding.UTF8.GetString(content)));
        return Task.FromResult(NextPreview ?? new LoadResult<ImportPreviewItem>.Loaded(new ImportPreviewItem([], 0, 0, 0, 0, null)));
    }

    public Task<LoadResult<ImportResultItem>> ImportAsync(Guid pageBusiness, string fileName, byte[] content, CancellationToken cancellationToken = default)
    {
        Calls.Add($"import {fileName}");
        return Task.FromResult(NextImport ?? new LoadResult<ImportResultItem>.Loaded(new ImportResultItem(0, 0, 0, 0)));
    }

    public string ExportCsv { get; set; } = "sku,name\nCOLA,Cola\n";

    public List<ChangeItem> Changes { get; } = [];

    public Task<LoadResult<string>> ExportAsync(Guid pageBusiness, CancellationToken cancellationToken = default) =>
        Task.FromResult<LoadResult<string>>(new LoadResult<string>.Loaded(ExportCsv));

    public Task<LoadResult<IReadOnlyList<ChangeItem>>> ListChangesAsync(Guid pageBusiness, Guid entityId, CancellationToken cancellationToken = default) =>
        Task.FromResult<LoadResult<IReadOnlyList<ChangeItem>>>(new LoadResult<IReadOnlyList<ChangeItem>>.Loaded(Changes.ToList()));

    private Task<OwnerActionResult> Change(string call, Action apply)
    {
        if (NextChange is OwnerActionResult.Succeeded)
        {
            Calls.Add(call);
            apply();
        }

        return Task.FromResult(NextChange);
    }

    private void Replace(TaxProfileItem old, TaxProfileItem updated) => Profiles[Profiles.FindIndex(p => p.Id == old.Id)] = updated with { Version = old.Version + 1 };
}
