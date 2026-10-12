using HelloPos.Web.ApiClient.Models;
using Microsoft.Kiota.Abstractions;

namespace HelloPos.Web.Services;

/// <summary>One tax component as shown and edited ("CGST", "2.5").</summary>
public sealed record TaxComponentItem(string Label, string RatePercent);

public sealed record TaxProfileVersionItem(Guid Id, DateOnly EffectiveFrom, IReadOnlyList<TaxComponentItem> Components, bool Current, bool Editable);

/// <summary>A tax profile; <see cref="Kind"/> is the wire name (taxable, zeroRated, exempt).</summary>
public sealed record TaxProfileItem(Guid Id, string Name, string Kind, bool Active, int Version, IReadOnlyList<TaxProfileVersionItem> Versions)
{
    public TaxProfileVersionItem? CurrentVersion => Versions.FirstOrDefault(v => v.Current);

    public string RatesText => Kind switch
    {
        "zeroRated" => "Zero-rated",
        "exempt" => "Exempt",
        _ => CurrentVersion is { } current ? string.Join(" + ", current.Components.Select(c => $"{c.Label} {c.RatePercent}%")) : "No rates in force yet",
    };
}

public sealed record CategoryItem(Guid Id, string Name, int SortOrder, Guid DefaultTaxProfileId, bool Active, int Version);

/// <summary>A product as listed and edited; money in minor units of <see cref="Currency"/>.</summary>
public sealed record ProductItem(
    Guid Id,
    string Name,
    string? Sku,
    Guid CategoryId,
    Guid? TaxProfileId,
    Guid EffectiveTaxProfileId,
    string Unit,
    long? UnitPrice,
    bool OpenPrice,
    string MinQuantity,
    string Step,
    int? MinAge,
    long DepositPerUnit,
    IReadOnlyList<string> Barcodes,
    string Currency,
    bool Active,
    int Version);

public sealed record ProductPageItem(IReadOnlyList<ProductItem> Items, int Page, int PageSize, int Total)
{
    public int Pages => Math.Max(1, (Total + PageSize - 1) / PageSize);
}

/// <summary>A scheduled discount; <see cref="Value"/> is a percent, or minor units for a fixed amount per unit.</summary>
public sealed record DiscountItem(Guid Id, string Kind, string Value, DateOnly From, DateOnly? To, string Status);

public sealed record ImportRowItem(int Row, string Outcome, string? Sku, string? Name, IReadOnlyList<(string Column, string Message)> Errors);

/// <summary>A preview; <see cref="FileError"/> is set when the file as a whole was refused (header, size).</summary>
public sealed record ImportPreviewItem(IReadOnlyList<ImportRowItem> Rows, int Create, int Update, int Unchanged, int Errors, string? FileError);

public sealed record ImportResultItem(int Rows, int Created, int Updated, int Unchanged);

/// <summary>One history entry: who did what, and each changed field (old and new as text).</summary>
public sealed record ChangeItem(DateTimeOffset At, string Actor, string Action, IReadOnlyList<(string Field, string? Old, string? New)> Fields);

/// <summary>What the product form sends; prices already in minor units.</summary>
public sealed record ProductForm(
    string Name,
    string? Sku,
    Guid CategoryId,
    Guid? TaxProfileId,
    string Unit,
    long? UnitPrice,
    bool OpenPrice,
    string MinQuantity,
    string Step,
    int? MinAge,
    long DepositPerUnit,
    IReadOnlyList<string> Barcodes);

public interface ICatalogService
{
    Task<LoadResult<IReadOnlyList<TaxProfileItem>>> ListTaxProfilesAsync(Guid pageBusiness, CancellationToken cancellationToken = default);

    Task<OwnerActionResult> CreateTaxProfileAsync(Guid pageBusiness, string name, string kind, IReadOnlyList<TaxComponentItem> components, CancellationToken cancellationToken = default);

    Task<OwnerActionResult> RenameTaxProfileAsync(Guid pageBusiness, TaxProfileItem profile, string name, CancellationToken cancellationToken = default);

    Task<OwnerActionResult> AddTaxVersionAsync(Guid pageBusiness, TaxProfileItem profile, DateOnly effectiveFrom, IReadOnlyList<TaxComponentItem> components, CancellationToken cancellationToken = default);

    Task<OwnerActionResult> DeleteTaxVersionAsync(Guid pageBusiness, TaxProfileItem profile, Guid versionId, CancellationToken cancellationToken = default);

    Task<OwnerActionResult> SetTaxProfileActiveAsync(Guid pageBusiness, TaxProfileItem profile, bool active, CancellationToken cancellationToken = default);

    Task<LoadResult<IReadOnlyList<CategoryItem>>> ListCategoriesAsync(Guid pageBusiness, CancellationToken cancellationToken = default);

    Task<OwnerActionResult> CreateCategoryAsync(Guid pageBusiness, string name, int sortOrder, Guid taxProfileId, CancellationToken cancellationToken = default);

    Task<OwnerActionResult> UpdateCategoryAsync(Guid pageBusiness, CategoryItem category, string name, int sortOrder, Guid taxProfileId, CancellationToken cancellationToken = default);

    Task<OwnerActionResult> SetCategoryActiveAsync(Guid pageBusiness, CategoryItem category, bool active, CancellationToken cancellationToken = default);

    Task<LoadResult<ProductPageItem>> ListProductsAsync(Guid pageBusiness, string? search, Guid? categoryId, bool? active, int page, CancellationToken cancellationToken = default);

    Task<LoadResult<ProductItem>> GetProductAsync(Guid pageBusiness, Guid productId, CancellationToken cancellationToken = default);

    Task<OwnerActionResult> CreateProductAsync(Guid pageBusiness, ProductForm form, CancellationToken cancellationToken = default);

    Task<OwnerActionResult> UpdateProductAsync(Guid pageBusiness, ProductItem product, ProductForm form, CancellationToken cancellationToken = default);

    Task<OwnerActionResult> SetProductActiveAsync(Guid pageBusiness, ProductItem product, bool active, CancellationToken cancellationToken = default);

    Task<LoadResult<IReadOnlyList<DiscountItem>>> ListDiscountsAsync(Guid pageBusiness, Guid productId, CancellationToken cancellationToken = default);

    /// <summary>Creates a discount, or with <paramref name="discountId"/> changes one (a running one may only end early).</summary>
    Task<OwnerActionResult> SaveDiscountAsync(Guid pageBusiness, Guid productId, Guid? discountId, string kind, string value, DateOnly fromDate, DateOnly? toDate, CancellationToken cancellationToken = default);

    Task<OwnerActionResult> DeleteDiscountAsync(Guid pageBusiness, Guid productId, Guid discountId, CancellationToken cancellationToken = default);

    Task<LoadResult<ImportPreviewItem>> PreviewImportAsync(Guid pageBusiness, string fileName, byte[] content, CancellationToken cancellationToken = default);

    Task<LoadResult<ImportResultItem>> ImportAsync(Guid pageBusiness, string fileName, byte[] content, CancellationToken cancellationToken = default);

    /// <summary>The price list as CSV text (the import format).</summary>
    Task<LoadResult<string>> ExportAsync(Guid pageBusiness, CancellationToken cancellationToken = default);

    Task<LoadResult<IReadOnlyList<ChangeItem>>> ListChangesAsync(Guid pageBusiness, Guid entityId, CancellationToken cancellationToken = default);
}

/// <summary>Catalog calls (feature #11) through the client generated from the contract.</summary>
public sealed class CatalogService(HttpClient http, ILogger<CatalogService> logger) : ICatalogService
{
    public Task<LoadResult<IReadOnlyList<TaxProfileItem>>> ListTaxProfilesAsync(Guid pageBusiness, CancellationToken cancellationToken = default) =>
        StaffService.LoadAsync<IReadOnlyList<TaxProfileItem>>(http, logger, "list-tax-profiles", async () =>
            (await ApiClients.Create(http).Catalog.TaxProfiles.GetAsync(c => Page(c, pageBusiness), cancellationToken).ConfigureAwait(false) ?? [])
            .Select(ToItem).ToList());

    public Task<OwnerActionResult> CreateTaxProfileAsync(Guid pageBusiness, string name, string kind, IReadOnlyList<TaxComponentItem> components, CancellationToken cancellationToken = default) =>
        Run("create-tax-profile", () => ApiClients.Create(http).Catalog.TaxProfiles.PostAsync(
            new NewTaxProfile
            {
                Name = name,
                Kind = kind switch { "zeroRated" => NewTaxProfile_kind.ZeroRated, "exempt" => NewTaxProfile_kind.Exempt, _ => NewTaxProfile_kind.Taxable },
                Components = ToApi(components),
            },
            c => Page(c, pageBusiness), cancellationToken));

    public Task<OwnerActionResult> RenameTaxProfileAsync(Guid pageBusiness, TaxProfileItem profile, string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return Run("rename-tax-profile", () => ApiClients.Create(http).Catalog.TaxProfiles[profile.Id].PutAsync(
            new TaxProfileRename { Name = name, Version = profile.Version }, c => Page(c, pageBusiness), cancellationToken));
    }

    public Task<OwnerActionResult> AddTaxVersionAsync(Guid pageBusiness, TaxProfileItem profile, DateOnly effectiveFrom, IReadOnlyList<TaxComponentItem> components, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return Run("add-tax-version", () => ApiClients.Create(http).Catalog.TaxProfiles[profile.Id].Versions.PostAsync(
            new TaxProfileVersionInput
            {
                EffectiveFrom = new Date(effectiveFrom.Year, effectiveFrom.Month, effectiveFrom.Day),
                Components = ToApi(components),
                Version = profile.Version,
            },
            c => Page(c, pageBusiness), cancellationToken));
    }

    public Task<OwnerActionResult> DeleteTaxVersionAsync(Guid pageBusiness, TaxProfileItem profile, Guid versionId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return Run("delete-tax-version", () => ApiClients.Create(http).Catalog.TaxProfiles[profile.Id].Versions[versionId].DeleteAsync(
            c => Page(c, pageBusiness), cancellationToken));
    }

    public Task<OwnerActionResult> SetTaxProfileActiveAsync(Guid pageBusiness, TaxProfileItem profile, bool active, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var body = new CatalogVersion { Version = profile.Version };
        var item = ApiClients.Create(http).Catalog.TaxProfiles[profile.Id];
        return active
            ? Run("reactivate-tax-profile", () => item.Reactivate.PostAsync(body, c => Page(c, pageBusiness), cancellationToken))
            : Run("deactivate-tax-profile", () => item.Deactivate.PostAsync(body, c => Page(c, pageBusiness), cancellationToken));
    }

    public Task<LoadResult<IReadOnlyList<CategoryItem>>> ListCategoriesAsync(Guid pageBusiness, CancellationToken cancellationToken = default) =>
        StaffService.LoadAsync<IReadOnlyList<CategoryItem>>(http, logger, "list-categories", async () =>
            (await ApiClients.Create(http).Catalog.Categories.GetAsync(c => Page(c, pageBusiness), cancellationToken).ConfigureAwait(false) ?? [])
            .Select(c => new CategoryItem(c.Id ?? Guid.Empty, c.Name ?? string.Empty, c.SortOrder ?? 0, c.DefaultTaxProfileId ?? Guid.Empty, c.Active ?? false, c.Version ?? 0))
            .ToList());

    public Task<OwnerActionResult> CreateCategoryAsync(Guid pageBusiness, string name, int sortOrder, Guid taxProfileId, CancellationToken cancellationToken = default) =>
        Run("create-category", () => ApiClients.Create(http).Catalog.Categories.PostAsync(
            new CategoryInput { Name = name, SortOrder = sortOrder, DefaultTaxProfileId = taxProfileId }, c => Page(c, pageBusiness), cancellationToken));

    public Task<OwnerActionResult> UpdateCategoryAsync(Guid pageBusiness, CategoryItem category, string name, int sortOrder, Guid taxProfileId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(category);
        return Run("update-category", () => ApiClients.Create(http).Catalog.Categories[category.Id].PutAsync(
            new CategoryUpdate { Name = name, SortOrder = sortOrder, DefaultTaxProfileId = taxProfileId, Version = category.Version },
            c => Page(c, pageBusiness), cancellationToken));
    }

    public Task<OwnerActionResult> SetCategoryActiveAsync(Guid pageBusiness, CategoryItem category, bool active, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(category);
        var body = new CatalogVersion { Version = category.Version };
        var item = ApiClients.Create(http).Catalog.Categories[category.Id];
        return active
            ? Run("reactivate-category", () => item.Reactivate.PostAsync(body, c => Page(c, pageBusiness), cancellationToken))
            : Run("deactivate-category", () => item.Deactivate.PostAsync(body, c => Page(c, pageBusiness), cancellationToken));
    }

    public const int ProductPageSize = 50;

    public Task<LoadResult<ProductPageItem>> ListProductsAsync(Guid pageBusiness, string? search, Guid? categoryId, bool? active, int page, CancellationToken cancellationToken = default) =>
        StaffService.LoadAsync(http, logger, "list-products", async () =>
        {
            var result = await ApiClients.Create(http).Catalog.Products.GetAsync(c =>
            {
                Page(c, pageBusiness);
                c.QueryParameters.Q = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
                c.QueryParameters.CategoryId = categoryId;
                c.QueryParameters.Active = active;
                c.QueryParameters.Page = page;
                c.QueryParameters.PageSize = ProductPageSize;
            }, cancellationToken).ConfigureAwait(false);
            return new ProductPageItem((result?.Items ?? []).Select(ToItem).ToList(), result?.Page ?? page, result?.PageSize ?? ProductPageSize, result?.Total ?? 0);
        });

    public Task<LoadResult<ProductItem>> GetProductAsync(Guid pageBusiness, Guid productId, CancellationToken cancellationToken = default) =>
        StaffService.LoadAsync(http, logger, "get-product", async () =>
            ToItem((await ApiClients.Create(http).Catalog.Products[productId].GetAsync(c => Page(c, pageBusiness), cancellationToken).ConfigureAwait(false))!));

    public Task<OwnerActionResult> CreateProductAsync(Guid pageBusiness, ProductForm form, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(form);
        return Run("create-product", () => ApiClients.Create(http).Catalog.Products.PostAsync(new ProductInput
        {
            Name = form.Name,
            Sku = form.Sku,
            CategoryId = form.CategoryId,
            TaxProfileId = form.TaxProfileId,
            Unit = Enum.Parse<ProductInput_unit>(form.Unit, ignoreCase: true),
            UnitPrice = form.UnitPrice,
            OpenPrice = form.OpenPrice,
            MinQuantity = form.MinQuantity,
            Step = form.Step,
            MinAge = form.MinAge,
            DepositPerUnit = form.DepositPerUnit,
            Barcodes = form.Barcodes.ToList(),
        }, c => Page(c, pageBusiness), cancellationToken));
    }

    public Task<OwnerActionResult> UpdateProductAsync(Guid pageBusiness, ProductItem product, ProductForm form, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(form);
        return Run("update-product", () => ApiClients.Create(http).Catalog.Products[product.Id].PutAsync(new ProductUpdate
        {
            Name = form.Name,
            Sku = form.Sku,
            CategoryId = form.CategoryId,
            TaxProfileId = form.TaxProfileId,
            Unit = Enum.Parse<ProductUpdate_unit>(form.Unit, ignoreCase: true),
            UnitPrice = form.UnitPrice,
            OpenPrice = form.OpenPrice,
            MinQuantity = form.MinQuantity,
            Step = form.Step,
            MinAge = form.MinAge,
            DepositPerUnit = form.DepositPerUnit,
            Barcodes = form.Barcodes.ToList(),
            Version = product.Version,
        }, c => Page(c, pageBusiness), cancellationToken));
    }

    public Task<OwnerActionResult> SetProductActiveAsync(Guid pageBusiness, ProductItem product, bool active, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(product);
        var body = new CatalogVersion { Version = product.Version };
        var item = ApiClients.Create(http).Catalog.Products[product.Id];
        return active
            ? Run("reactivate-product", () => item.Reactivate.PostAsync(body, c => Page(c, pageBusiness), cancellationToken))
            : Run("deactivate-product", () => item.Deactivate.PostAsync(body, c => Page(c, pageBusiness), cancellationToken));
    }

    public Task<LoadResult<IReadOnlyList<DiscountItem>>> ListDiscountsAsync(Guid pageBusiness, Guid productId, CancellationToken cancellationToken = default) =>
        StaffService.LoadAsync<IReadOnlyList<DiscountItem>>(http, logger, "list-discounts", async () =>
            (await ApiClients.Create(http).Catalog.Products[productId].Discounts.GetAsync(c => Page(c, pageBusiness), cancellationToken).ConfigureAwait(false) ?? [])
            .Select(d => new DiscountItem(
                d.Id ?? Guid.Empty,
                d.Kind == ItemDiscount_kind.Fixed ? "fixed" : "percent",
                d.Value ?? "0",
                ToDateOnly(d.FromDate) ?? default,
                ToDateOnly(d.ToDate),
                d.Status?.ToString().ToLowerInvariant() ?? "running")).ToList());

    public Task<OwnerActionResult> SaveDiscountAsync(Guid pageBusiness, Guid productId, Guid? discountId, string kind, string value, DateOnly fromDate, DateOnly? toDate, CancellationToken cancellationToken = default)
    {
        var body = new ItemDiscountInput
        {
            Kind = kind == "fixed" ? ItemDiscountInput_kind.Fixed : ItemDiscountInput_kind.Percent,
            Value = value,
            FromDate = new Date(fromDate.Year, fromDate.Month, fromDate.Day),
            ToDate = toDate is { } end ? new Date(end.Year, end.Month, end.Day) : null,
        };
        var discounts = ApiClients.Create(http).Catalog.Products[productId].Discounts;
        return discountId is { } id
            ? Run("update-discount", () => discounts[id].PutAsync(body, c => Page(c, pageBusiness), cancellationToken))
            : Run("create-discount", () => discounts.PostAsync(body, c => Page(c, pageBusiness), cancellationToken));
    }

    public Task<OwnerActionResult> DeleteDiscountAsync(Guid pageBusiness, Guid productId, Guid discountId, CancellationToken cancellationToken = default) =>
        Run("delete-discount", () => ApiClients.Create(http).Catalog.Products[productId].Discounts[discountId].DeleteAsync(c => Page(c, pageBusiness), cancellationToken));

    public Task<LoadResult<ImportPreviewItem>> PreviewImportAsync(Guid pageBusiness, string fileName, byte[] content, CancellationToken cancellationToken = default) =>
        StaffService.LoadAsync(http, logger, "preview-import", async () =>
        {
            try
            {
                var preview = await ApiClients.Create(http).Catalog.Imports.Preview.PostAsync(File(fileName, content), c => Page(c, pageBusiness), cancellationToken).ConfigureAwait(false);
                return new ImportPreviewItem(
                    (preview?.Rows ?? []).Select(r => new ImportRowItem(
                        r.Row ?? 0, r.Outcome?.ToString().ToLowerInvariant() ?? "error", r.Sku, r.Name,
                        (r.Errors ?? []).Select(e => (e.Column ?? string.Empty, e.Message ?? string.Empty)).ToList())).ToList(),
                    preview?.Create ?? 0, preview?.Update ?? 0, preview?.Unchanged ?? 0, preview?.Errors ?? 0, null);
            }
            catch (ValidationProblemDetails problem)
            {
                return new ImportPreviewItem([], 0, 0, 0, 0, string.Join(" ", OwnerAccountService.FieldMessages(problem).Values));
            }
        });

    public Task<LoadResult<ImportResultItem>> ImportAsync(Guid pageBusiness, string fileName, byte[] content, CancellationToken cancellationToken = default) =>
        StaffService.LoadAsync(http, logger, "import", async () =>
        {
            var result = await ApiClients.Create(http).Catalog.Imports.PostAsync(File(fileName, content), c => Page(c, pageBusiness), cancellationToken).ConfigureAwait(false);
            return new ImportResultItem(result?.Rows ?? 0, result?.Created ?? 0, result?.Updated ?? 0, result?.Unchanged ?? 0);
        });

    public Task<LoadResult<string>> ExportAsync(Guid pageBusiness, CancellationToken cancellationToken = default) =>
        StaffService.LoadAsync(http, logger, "export", async () =>
        {
            await using var stream = await ApiClients.Create(http).Catalog.Products.Export.GetAsync(c => Page(c, pageBusiness), cancellationToken).ConfigureAwait(false);
            if (stream is null)
            {
                return string.Empty;
            }

            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        });

    public Task<LoadResult<IReadOnlyList<ChangeItem>>> ListChangesAsync(Guid pageBusiness, Guid entityId, CancellationToken cancellationToken = default) =>
        StaffService.LoadAsync<IReadOnlyList<ChangeItem>>(http, logger, "list-changes", async () =>
            (await ApiClients.Create(http).Catalog.Changes.GetAsync(c =>
            {
                Page(c, pageBusiness);
                c.QueryParameters.EntityId = entityId;
            }, cancellationToken).ConfigureAwait(false) ?? [])
            .Select(c => new ChangeItem(
                c.At ?? default,
                c.Actor ?? string.Empty,
                c.Action?.ToString().ToLowerInvariant() ?? string.Empty,
                (c.Changes ?? []).Select(f => (f.Field ?? string.Empty, f.Old, f.New)).ToList()))
            .ToList());

    private static Microsoft.Kiota.Abstractions.MultipartBody File(string fileName, byte[] content)
    {
        var body = new Microsoft.Kiota.Abstractions.MultipartBody();
        body.AddOrReplacePart("file", "text/csv", content, fileName);
        return body;
    }

    private static DateOnly? ToDateOnly(Date? date) => date is { } d ? new DateOnly(d.Year, d.Month, d.Day) : null;

    private static ProductItem ToItem(Product product) => new(
        product.Id ?? Guid.Empty,
        product.Name ?? string.Empty,
        product.Sku,
        product.CategoryId ?? Guid.Empty,
        product.TaxProfileId,
        product.EffectiveTaxProfileId ?? Guid.Empty,
        product.Unit?.ToString().ToLowerInvariant() ?? "piece",
        product.UnitPrice,
        product.OpenPrice ?? false,
        product.MinQuantity ?? "1",
        product.Step ?? "1",
        product.MinAge,
        product.DepositPerUnit ?? 0,
        (product.Barcodes ?? []).Select(b => b.Code ?? string.Empty).ToList(),
        product.Currency ?? string.Empty,
        product.Active ?? false,
        product.Version ?? 0);

    private Task<OwnerActionResult> Run(string operation, Func<Task> call) => OwnerAccountService.RunAsync(http, logger, operation, call);

    private static void Page<T>(RequestConfiguration<T> configuration, Guid pageBusiness)
        where T : class, new() =>
        configuration.Headers.Add(BusinessService.BusinessHeader, pageBusiness.ToString());

    private static List<ApiClient.Models.TaxComponent> ToApi(IReadOnlyList<TaxComponentItem> components) =>
        components.Select(c => new ApiClient.Models.TaxComponent { Label = c.Label, RatePercent = c.RatePercent }).ToList();

    private static TaxProfileItem ToItem(TaxProfile profile) => new(
        profile.Id ?? Guid.Empty,
        profile.Name ?? string.Empty,
        profile.Kind switch { TaxProfile_kind.ZeroRated => "zeroRated", TaxProfile_kind.Exempt => "exempt", _ => "taxable" },
        profile.Active ?? false,
        profile.Version ?? 0,
        (profile.Versions ?? []).Select(v => new TaxProfileVersionItem(
            v.Id ?? Guid.Empty,
            v.EffectiveFrom is { } date ? new DateOnly(date.Year, date.Month, date.Day) : default,
            (v.Components ?? []).Select(c => new TaxComponentItem(c.Label ?? string.Empty, c.RatePercent ?? "0")).ToList(),
            v.Current ?? false,
            v.Editable ?? false)).ToList());
}
