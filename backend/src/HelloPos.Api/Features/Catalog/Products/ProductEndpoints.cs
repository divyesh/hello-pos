using System.Text.Json.Serialization;
using HelloPos.Api.Infrastructure;
using HelloPos.Api.Infrastructure.Identity;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Infrastructure.Tenancy;
using HelloPos.Kernel.Catalog;
using HelloPos.Kernel.Pricing;
using HelloPos.Kernel.Staff;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace HelloPos.Api.Features.Catalog.Products;

/// <summary>Contract <c>ProductInput</c> / <c>ProductUpdate</c>; loosely typed so every bad value is a field error.</summary>
public sealed record ProductRequest(
    string? Name,
    string? Sku,
    Guid? CategoryId,
    Guid? TaxProfileId,
    string? Unit,
    long? UnitPrice,
    bool OpenPrice,
    string? MinQuantity,
    string? Step,
    int? MinAge,
    long DepositPerUnit,
    List<string>? Barcodes,
    int Version = 0);

public sealed record BarcodeResponse(string Code, string Kind);

/// <summary>Contract <c>Product</c>; absent optional fields are omitted, not null.</summary>
public sealed record ProductResponse(
    Guid Id,
    string Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Sku,
    Guid CategoryId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? TaxProfileId,
    Guid EffectiveTaxProfileId,
    string Unit,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? UnitPrice,
    bool OpenPrice,
    string MinQuantity,
    string Step,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? MinAge,
    long DepositPerUnit,
    List<BarcodeResponse> Barcodes,
    string Currency,
    bool Active,
    int Version);

public sealed record ProductPageResponse(List<ProductResponse> Items, int Page, int PageSize, int Total);

/// <summary>
/// Products (feature #11, US2, FR-004–FR-006, FR-012): owners and managers create, change, find,
/// deactivate and reactivate them. Money travels in minor units of the business currency.
/// </summary>
public sealed class ProductEndpoints : IEndpointMapper
{
    public const int MaxPageSize = 200;

    public void Map(IEndpointRouteBuilder app)
    {
        var products = app.MapGroup("/catalog/products")
            .WithTags(CatalogSupport.Tag)
            .RequireRole(StaffRole.Owner, StaffRole.Manager)
            .RequireSameBusiness();
        products.MapGet("", ListAsync).WithName("listProducts");
        products.MapGet("/{productId:guid}", GetAsync).WithName("getProduct");
        products.MapPost("", CreateAsync).WithName("createProduct").RequireClientHeader();
        products.MapPut("/{productId:guid}", UpdateAsync).WithName("updateProduct").RequireClientHeader();
        products.MapPost("/{productId:guid}/deactivate", (Guid productId, CatalogVersionRequest request, ICurrentStaff current, HelloPosDbContext db, TimeProvider clock, CancellationToken ct) =>
            SetActiveAsync(productId, request, active: false, current, db, clock, ct)).WithName("deactivateProduct").RequireClientHeader();
        products.MapPost("/{productId:guid}/reactivate", (Guid productId, CatalogVersionRequest request, ICurrentStaff current, HelloPosDbContext db, TimeProvider clock, CancellationToken ct) =>
            SetActiveAsync(productId, request, active: true, current, db, clock, ct)).WithName("reactivateProduct").RequireClientHeader();
    }

    public static async Task<Results<Ok<ProductPageResponse>, ValidationProblem>> ListAsync(
        string? q, Guid? categoryId, bool? active, int? page, int? pageSize, HelloPosDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var pageNumber = page ?? 1;
        var size = pageSize ?? 50;
        if (pageNumber < 1 || size is < 1 or > MaxPageSize || q?.Length > CatalogRules.ProductNameMaxLength)
        {
            return CatalogSupport.Invalid("page", $"Use page 1 or more, a page size of 1 to {MaxPageSize}, and a search of at most {CatalogRules.ProductNameMaxLength} characters.");
        }

        var query = db.Products.AsNoTracking()
            .Where(p => (categoryId == null || p.CategoryId == categoryId) && (active == null || p.Active == active));
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            var pattern = "%" + EscapeLike(term.ToLowerInvariant()) + "%";
            var sku = term.ToLowerInvariant();
            var barcode = BarcodeRules.Parse(term).Barcode?.Code;
            var byBarcode = db.ProductBarcodes.Where(b => b.Code == barcode).Select(b => b.ProductId);
            query = query.Where(p => EF.Functions.Like(p.Name.ToLower(), pattern, "\\") || p.NormalizedSku == sku || byBarcode.Contains(p.Id));
        }

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query.OrderBy(p => p.Name.ToLower()).ThenBy(p => p.Id)
            .Skip((pageNumber - 1) * size).Take(size)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var responses = await ResponsesAsync(db, items, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new ProductPageResponse(responses, pageNumber, size, total));
    }

    public static async Task<Results<Ok<ProductResponse>, ProblemHttpResult>> GetAsync(Guid productId, HelloPosDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == productId, cancellationToken).ConfigureAwait(false);
        return product is null
            ? CatalogSupport.NotFound()
            : TypedResults.Ok((await ResponsesAsync(db, [product], cancellationToken).ConfigureAwait(false))[0]);
    }

    public static async Task<Results<Created<ProductResponse>, ValidationProblem>> CreateAsync(
        ProductRequest request, ICurrentStaff current, HelloPosDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(clock);
        var business = await CatalogSupport.BusinessAsync(db, cancellationToken).ConfigureAwait(false);
        var (values, errors) = await ValidateAsync(db, request, business.CurrencyCode, null, cancellationToken).ConfigureAwait(false);
        if (values is null)
        {
            return CatalogSupport.Invalid(errors);
        }

        var actor = (await current.GetAsync(cancellationToken).ConfigureAwait(false))!;
        var now = clock.GetUtcNow();
        var product = new Product { Name = values.Name, Unit = values.Unit, CreatedAt = now, UpdatedAt = now };
        Apply(product, values);
        db.Products.Add(product);
        foreach (var barcode in values.Barcodes)
        {
            db.ProductBarcodes.Add(new ProductBarcode { ProductId = product.Id, Code = barcode.Code, Kind = KindName(barcode.Kind) });
        }

        db.RecordChange(clock, actor.Id, CatalogEntityNames.Product, product.Id, CatalogActions.Created, Describe(null, values, business.CurrencyCode));
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException e) when (CatalogSupport.IsUniqueViolation(e))
        {
            return CatalogSupport.Invalid("barcodes", "Another product took this SKU or barcode just now. Check and try again.");
        }

        return TypedResults.Created($"/catalog/products/{product.Id}", (await ResponsesAsync(db, [product], cancellationToken).ConfigureAwait(false))[0]);
    }

    public static async Task<Results<Ok<ProductResponse>, ValidationProblem, ProblemHttpResult>> UpdateAsync(
        Guid productId, ProductRequest request, ICurrentStaff current, HelloPosDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(db);
        var product = await db.Products.SingleOrDefaultAsync(p => p.Id == productId, cancellationToken).ConfigureAwait(false);
        if (product is null)
        {
            return CatalogSupport.NotFound();
        }

        if (product.Version != request.Version)
        {
            return CatalogSupport.CatalogChanged();
        }

        var business = await CatalogSupport.BusinessAsync(db, cancellationToken).ConfigureAwait(false);
        var (values, errors) = await ValidateAsync(db, request, business.CurrencyCode, product, cancellationToken).ConfigureAwait(false);
        if (values is null)
        {
            return CatalogSupport.Invalid(errors);
        }

        var existing = await db.ProductBarcodes.Where(b => b.ProductId == product.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        var before = ValuesOf(product, existing, business.CurrencyCode);
        var changes = Describe(before, values, business.CurrencyCode);
        if (changes.Count > 0)
        {
            var actor = (await current.GetAsync(cancellationToken).ConfigureAwait(false))!;
            db.RecordChange(clock, actor.Id, CatalogEntityNames.Product, product.Id, CatalogActions.Updated, changes);
            Apply(product, values);
            var wanted = values.Barcodes.Select(b => b.Code).ToHashSet(StringComparer.Ordinal);
            db.ProductBarcodes.RemoveRange(existing.Where(b => !wanted.Contains(b.Code)));
            foreach (var barcode in values.Barcodes.Where(b => existing.All(e => e.Code != b.Code)))
            {
                db.ProductBarcodes.Add(new ProductBarcode { ProductId = product.Id, Code = barcode.Code, Kind = KindName(barcode.Kind) });
            }
        }

        return await SaveAsync(db, product, request.Version, clock, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<Results<Ok<ProductResponse>, ValidationProblem, ProblemHttpResult>> SetActiveAsync(
        Guid productId, CatalogVersionRequest request, bool active, ICurrentStaff current, HelloPosDbContext db, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(db);
        var product = await db.Products.SingleOrDefaultAsync(p => p.Id == productId, cancellationToken).ConfigureAwait(false);
        if (product is null)
        {
            return CatalogSupport.NotFound();
        }

        if (product.Version != request.Version)
        {
            return CatalogSupport.CatalogChanged();
        }

        if (product.Active != active)
        {
            var actor = (await current.GetAsync(cancellationToken).ConfigureAwait(false))!;
            db.RecordChange(clock, actor.Id, CatalogEntityNames.Product, product.Id, active ? CatalogActions.Reactivated : CatalogActions.Deactivated);
            product.Active = active;
        }

        return await SaveAsync(db, product, request.Version, clock, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A validated product, in currency units, ready to store.</summary>
    internal sealed record ProductValues(
        string Name, string? Sku, Guid CategoryId, Guid? TaxProfileId, string Unit, decimal? UnitPrice, bool OpenPrice,
        decimal MinQuantity, decimal Step, short? MinAge, decimal DepositPerUnit, List<Barcode> Barcodes);

    /// <summary>
    /// Every field rule (FR-004, FR-005); links must be active records of this business (other businesses'
    /// ids are invisible under row-level security, so they read as unknown). Keeping an already-linked
    /// record that was deactivated later is allowed.
    /// </summary>
    internal static async Task<(ProductValues? Values, Dictionary<string, List<string>> Errors)> ValidateAsync(
        HelloPosDbContext db, ProductRequest request, string currency, Product? current, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (!Currencies.IsSupported(currency))
        {
            errors.Add("unitPrice", $"Prices in {currency} are not supported yet.");
        }

        errors.Add("name", CatalogRules.ValidateName(request.Name, CatalogRules.ProductNameMaxLength));
        var sku = string.IsNullOrWhiteSpace(request.Sku) ? null : request.Sku.Trim();
        errors.Add("sku", CatalogRules.ValidateSku(sku));
        if (sku is not null && !errors.ContainsKey("sku"))
        {
            var normalized = sku.ToLowerInvariant();
            if (await db.Products.AnyAsync(p => p.NormalizedSku == normalized && p.Id != (current == null ? Guid.Empty : current.Id), cancellationToken).ConfigureAwait(false))
            {
                errors.Add("sku", "Another product has this SKU.");
            }
        }

        if (request.CategoryId is not { } categoryId)
        {
            errors.Add("categoryId", "Choose a category.");
        }
        else if (categoryId != current?.CategoryId && !await db.Categories.AnyAsync(c => c.Id == categoryId && c.Active, cancellationToken).ConfigureAwait(false))
        {
            errors.Add("categoryId", "Choose an active category of this business.");
        }

        if (request.TaxProfileId is { } profileId && profileId != current?.TaxProfileId
            && !await db.TaxProfiles.AnyAsync(p => p.Id == profileId && p.Active, cancellationToken).ConfigureAwait(false))
        {
            errors.Add("taxProfileId", "Choose an active tax profile of this business, or follow the category.");
        }

        errors.Add("unit", CatalogRules.ValidateUnit(request.Unit));
        if (request.OpenPrice && request.UnitPrice is not null)
        {
            errors.Add("unitPrice", "An open-price product has no price; the cashier enters it.");
        }
        else if (!request.OpenPrice && request.UnitPrice is null)
        {
            errors.Add("unitPrice", "Enter a price, or mark the product as open price.");
        }
        else if (request.UnitPrice < 0)
        {
            errors.Add("unitPrice", "The price cannot be negative.");
        }

        var minQuantity = CatalogSupport.ParseDecimal(request.MinQuantity);
        var step = CatalogSupport.ParseDecimal(request.Step);
        if (minQuantity is null)
        {
            errors.Add("minQuantity", "Enter the minimum quantity as a number, for example 1 or 0.005.");
        }

        if (step is null)
        {
            errors.Add("step", "Enter the step as a number, for example 1 or 0.005.");
        }

        if (minQuantity is { } min && step is { } s)
        {
            var (minError, stepError) = CatalogRules.ValidateQuantities(min, s);
            errors.Add("minQuantity", minError);
            errors.Add("step", stepError);
        }

        errors.Add("minAge", CatalogRules.ValidateMinAge(request.MinAge));
        if (request.DepositPerUnit < 0)
        {
            errors.Add("depositPerUnit", "The deposit cannot be negative.");
        }

        var barcodes = await ValidateBarcodesAsync(db, request.Barcodes ?? [], current?.Id, errors, cancellationToken).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return (null, errors);
        }

        return (new ProductValues(
            request.Name!.Trim(), sku, request.CategoryId!.Value, request.TaxProfileId, request.Unit!,
            request.UnitPrice is { } price ? CatalogSupport.FromMinor(price, currency) : null, request.OpenPrice,
            minQuantity!.Value, step!.Value, (short?)request.MinAge, CatalogSupport.FromMinor(request.DepositPerUnit, currency), barcodes), errors);
    }

    private static async Task<List<Barcode>> ValidateBarcodesAsync(
        HelloPosDbContext db, List<string> input, Guid? productId, Dictionary<string, List<string>> errors, CancellationToken cancellationToken)
    {
        if (input.Count > BarcodeRules.MaxBarcodesPerProduct)
        {
            errors.Add("barcodes", $"Use at most {BarcodeRules.MaxBarcodesPerProduct} barcodes.");
            return [];
        }

        var barcodes = new List<Barcode>();
        foreach (var raw in input)
        {
            var (barcode, problem) = BarcodeRules.Parse(raw);
            if (problem is { } p)
            {
                errors.Add("barcodes", $"{raw.Trim()}: {BarcodeRules.Message(p)}");
            }
            else if (barcodes.Any(b => b.Code == barcode!.Code))
            {
                errors.Add("barcodes", $"{raw.Trim()} is listed twice.");
            }
            else
            {
                barcodes.Add(barcode!);
            }
        }

        var codes = barcodes.Select(b => b.Code).ToList();
        var taken = await db.ProductBarcodes.AsNoTracking()
            .Where(b => codes.Contains(b.Code) && b.ProductId != (productId ?? Guid.Empty))
            .Join(db.Products, b => b.ProductId, p => p.Id, (b, p) => new { b.Code, p.Name })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var conflict in taken)
        {
            errors.Add("barcodes", $"{conflict.Code} is already used by {conflict.Name}.");
        }

        return barcodes;
    }

    internal static void Apply(Product product, ProductValues values)
    {
        product.Name = values.Name;
        product.Sku = values.Sku;
        product.NormalizedSku = values.Sku?.ToLowerInvariant();
        product.CategoryId = values.CategoryId;
        product.TaxProfileId = values.TaxProfileId;
        product.Unit = values.Unit;
        product.UnitPrice = values.UnitPrice;
        product.OpenPrice = values.OpenPrice;
        product.MinQuantity = values.MinQuantity;
        product.Step = values.Step;
        product.MinAge = values.MinAge;
        product.DepositPerUnit = values.DepositPerUnit;
    }

    internal static ProductValues ValuesOf(Product product, List<ProductBarcode> barcodes, string currency) => new(
        product.Name, product.Sku, product.CategoryId, product.TaxProfileId, product.Unit, product.UnitPrice, product.OpenPrice,
        product.MinQuantity, product.Step, product.MinAge, product.DepositPerUnit,
        barcodes.Select(b => new Barcode(b.Code, b.Kind switch { "ean8" => BarcodeKind.Ean8, "plu" => BarcodeKind.Plu, _ => BarcodeKind.Ean13 })).ToList());

    /// <summary>History entry: every changed field as [old, new], money in minor units.</summary>
    internal static Dictionary<string, object?[]> Describe(ProductValues? before, ProductValues after, string currency)
    {
        static long? Minor(decimal? amount, string currency) => amount is { } a ? CatalogSupport.ToMinor(a, currency) : null;
        var changes = new Dictionary<string, object?[]>();
        changes.Diff("name", before?.Name, after.Name);
        changes.Diff("sku", before?.Sku, after.Sku);
        changes.Diff("categoryId", before?.CategoryId, after.CategoryId);
        changes.Diff("taxProfileId", before?.TaxProfileId, after.TaxProfileId);
        changes.Diff("unit", before?.Unit, after.Unit);
        changes.Diff("unitPrice", Minor(before?.UnitPrice, currency), Minor(after.UnitPrice, currency));
        changes.Diff("openPrice", before?.OpenPrice, after.OpenPrice);
        changes.Diff("minQuantity", before?.MinQuantity, after.MinQuantity);
        changes.Diff("step", before?.Step, after.Step);
        changes.Diff("minAge", before?.MinAge, after.MinAge);
        changes.Diff("depositPerUnit", Minor(before?.DepositPerUnit, currency), Minor(after.DepositPerUnit, currency));
        changes.Diff("barcodes", before is null ? null : string.Join(", ", before.Barcodes.Select(b => b.Code).Order(StringComparer.Ordinal)),
            string.Join(", ", after.Barcodes.Select(b => b.Code).Order(StringComparer.Ordinal)));
        return changes;
    }

    internal static string KindName(BarcodeKind kind) => kind switch
    {
        BarcodeKind.Ean8 => "ean8",
        BarcodeKind.Plu => "plu",
        _ => "ean13",
    };

    /// <summary>Escapes LIKE wildcards so a search for "50%" finds "50%" literally.</summary>
    private static string EscapeLike(string term) =>
        term.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);

    private static async Task<Results<Ok<ProductResponse>, ValidationProblem, ProblemHttpResult>> SaveAsync(
        HelloPosDbContext db, Product product, int version, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (db.ChangeTracker.HasChanges())
        {
            db.Entry(product).Property(p => p.Version).OriginalValue = version;
            product.Version = version + 1;
            product.UpdatedAt = clock.GetUtcNow();
            try
            {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                return CatalogSupport.CatalogChanged();
            }
            catch (DbUpdateException e) when (CatalogSupport.IsUniqueViolation(e))
            {
                return CatalogSupport.Invalid("barcodes", "Another product took this SKU or barcode just now. Check and try again.");
            }
        }

        return TypedResults.Ok((await ResponsesAsync(db, [product], cancellationToken).ConfigureAwait(false))[0]);
    }

    private static async Task<List<ProductResponse>> ResponsesAsync(HelloPosDbContext db, List<Product> products, CancellationToken cancellationToken)
    {
        var business = await CatalogSupport.BusinessAsync(db, cancellationToken).ConfigureAwait(false);
        var currency = business.CurrencyCode;
        var ids = products.Select(p => p.Id).ToList();
        var categoryIds = products.Select(p => p.CategoryId).Distinct().ToList();
        var barcodes = (await db.ProductBarcodes.AsNoTracking().Where(b => ids.Contains(b.ProductId)).OrderBy(b => b.Code)
                .ToListAsync(cancellationToken).ConfigureAwait(false))
            .GroupBy(b => b.ProductId).ToDictionary(g => g.Key, g => g.ToList());
        var defaults = await db.Categories.AsNoTracking().Where(c => categoryIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.DefaultTaxProfileId, cancellationToken).ConfigureAwait(false);
        return products.Select(p => new ProductResponse(
            p.Id,
            p.Name,
            p.Sku,
            p.CategoryId,
            p.TaxProfileId,
            p.TaxProfileId ?? defaults[p.CategoryId],
            p.Unit,
            p.UnitPrice is { } price ? CatalogSupport.ToMinor(price, currency) : null,
            p.OpenPrice,
            CatalogSupport.Format(p.MinQuantity),
            CatalogSupport.Format(p.Step),
            p.MinAge,
            CatalogSupport.ToMinor(p.DepositPerUnit, currency),
            barcodes.GetValueOrDefault(p.Id, []).Select(b => new BarcodeResponse(b.Code, b.Kind)).ToList(),
            currency,
            p.Active,
            p.Version)).ToList();
    }
}
