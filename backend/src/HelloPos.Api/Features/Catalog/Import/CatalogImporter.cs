using System.Globalization;
using HelloPos.Api.Features.Catalog.Products;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Kernel.Catalog;
using HelloPos.Kernel.Pricing;
using Microsoft.EntityFrameworkCore;

namespace HelloPos.Api.Features.Catalog.Import;

/// <summary>The CSV columns, shared by import and export (research R8).</summary>
public static class CatalogCsv
{
    public static readonly IReadOnlyList<string> Columns =
    [
        "sku", "name", "category", "category_tax_profile", "tax_profile", "unit", "price", "open_price",
        "min_quantity", "step", "barcodes", "min_age", "deposit", "active",
    ];

    public static readonly IReadOnlyList<string> Required = ["sku", "name", "category", "unit", "price"];

    public const int MaxRows = 10_000;
    public const long MaxBytes = 5 * 1024 * 1024;
    public const char BarcodeSeparator = '|';
}

public sealed record ImportError(string Column, string Message);

public sealed record ImportRowResult(int Row, string Outcome, string? Sku, string? Name, List<ImportError> Errors);

/// <summary>
/// Checks a CSV file against the current catalog in memory (one snapshot, no per-row queries) and plans
/// what an import would do (feature #11, US4, FR-011). Matching: SKU, then barcode. The same rules and
/// messages as the product form (kernel <see cref="CatalogRules"/> and <see cref="BarcodeRules"/>).
/// </summary>
internal sealed class CatalogImporter
{
    private readonly string currency;
    private readonly Dictionary<string, TaxProfile> profiles;
    private readonly Dictionary<string, Category> categories;
    private readonly Dictionary<string, Product> productsBySku;
    private readonly Dictionary<Guid, Product> products;
    private readonly Dictionary<string, Guid> barcodeOwners;
    private readonly Dictionary<Guid, List<ProductBarcode>> barcodesByProduct;

    private CatalogImporter(
        string currency, List<TaxProfile> profiles, List<Category> categories, List<Product> products, List<ProductBarcode> barcodes)
    {
        this.currency = currency;
        this.profiles = profiles.ToDictionary(p => p.NormalizedName, StringComparer.Ordinal);
        this.categories = categories.ToDictionary(c => c.NormalizedName, StringComparer.Ordinal);
        this.products = products.ToDictionary(p => p.Id);
        productsBySku = products.Where(p => p.NormalizedSku is not null).ToDictionary(p => p.NormalizedSku!, StringComparer.Ordinal);
        barcodeOwners = barcodes.ToDictionary(b => b.Code, b => b.ProductId, StringComparer.Ordinal);
        barcodesByProduct = barcodes.GroupBy(b => b.ProductId).ToDictionary(g => g.Key, g => g.ToList());
    }

    /// <summary>A planned new category (name as first written, its tax profile).</summary>
    public sealed record NewCategory(string Name, Guid TaxProfileId);

    /// <summary>A row that creates or updates a product.</summary>
    public sealed record PlannedProduct(int Row, Product? Existing, string CategoryKey, ProductEndpoints.ProductValues Values, Dictionary<string, object?[]> Changes, bool Active);

    public sealed record Plan(List<ImportRowResult> Rows, Dictionary<string, NewCategory> NewCategories, List<PlannedProduct> Products)
    {
        public int Count(string outcome) => Rows.Count(r => r.Outcome == outcome);
    }

    /// <summary>Loads the snapshot of the current business's catalog.</summary>
    public static async Task<CatalogImporter> LoadAsync(HelloPosDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var business = await CatalogSupport.BusinessAsync(db, cancellationToken).ConfigureAwait(false);
        return new CatalogImporter(
            business.CurrencyCode,
            await db.TaxProfiles.ToListAsync(cancellationToken).ConfigureAwait(false),
            await db.Categories.ToListAsync(cancellationToken).ConfigureAwait(false),
            await db.Products.ToListAsync(cancellationToken).ConfigureAwait(false),
            await db.ProductBarcodes.ToListAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <summary>The file-level problem (bad header, too many rows), or the plan.</summary>
    public (string? Field, string? Message, Plan? Plan) Check(string text)
    {
        if (!Currencies.IsSupported(currency))
        {
            return ("file", $"Prices in {currency} are not supported yet.", null);
        }

        var (rows, unterminated) = CsvReader.Parse(text);
        if (unterminated is { } line)
        {
            return ("file", $"A quoted field starting on line {line} is never closed.", null);
        }

        if (rows.Count == 0)
        {
            return ("header", $"The first line must name the columns: {string.Join(", ", CatalogCsv.Columns)}.", null);
        }

        var header = rows[0].Fields.Select(f => f.Trim().ToLowerInvariant()).ToList();
        var missing = CatalogCsv.Required.Where(c => !header.Contains(c)).ToList();
        if (missing.Count > 0)
        {
            return ("header", $"Missing columns: {string.Join(", ", missing)}. Download the template for the full list.", null);
        }

        if (rows.Count - 1 > CatalogCsv.MaxRows)
        {
            return ("file", $"Import at most {CatalogCsv.MaxRows:N0} rows at a time; split the file.", null);
        }

        var columns = header.Select((name, index) => (name, index)).GroupBy(c => c.name).ToDictionary(g => g.Key, g => g.First().index, StringComparer.Ordinal);
        var plan = new Plan([], new Dictionary<string, NewCategory>(StringComparer.Ordinal), []);
        var skusInFile = new Dictionary<string, int>(StringComparer.Ordinal);
        var barcodesInFile = new Dictionary<string, int>(StringComparer.Ordinal);
        var matchedInFile = new Dictionary<Guid, int>();
        foreach (var row in rows.Skip(1))
        {
            plan.Rows.Add(CheckRow(row, columns, plan, skusInFile, barcodesInFile, matchedInFile));
        }

        return (null, null, plan);
    }

    private ImportRowResult CheckRow(
        CsvReader.Row row, Dictionary<string, int> columns, Plan plan, Dictionary<string, int> skusInFile,
        Dictionary<string, int> barcodesInFile, Dictionary<Guid, int> matchedInFile)
    {
        string Get(string column) =>
            columns.TryGetValue(column, out var i) && i < row.Fields.Count ? CsvReader.Uncell(row.Fields[i].Trim()) : string.Empty;

        var errors = new List<ImportError>();
        void Error(string column, string? message)
        {
            if (message is not null)
            {
                errors.Add(new ImportError(column, message));
            }
        }

        var name = Get("name");
        Error("name", CatalogRules.ValidateName(name, CatalogRules.ProductNameMaxLength));
        var sku = Get("sku") is { Length: > 0 } s ? s : null;
        Error("sku", CatalogRules.ValidateSku(sku));
        if (sku is not null && skusInFile.TryGetValue(sku.ToLowerInvariant(), out var skuRow))
        {
            Error("sku", $"SKU {sku} is already on row {skuRow}.");
        }
        else if (sku is not null)
        {
            skusInFile[sku.ToLowerInvariant()] = row.Line;
        }

        // Category: existing (must be active), or new (needs its tax profile).
        var categoryName = Get("category");
        var categoryKey = CatalogSupport.Normalize(categoryName);
        if (CatalogRules.ValidateName(categoryName, CatalogRules.CategoryNameMaxLength) is { } categoryError)
        {
            Error("category", categoryError);
        }
        else if (categories.TryGetValue(categoryKey, out var existingCategory))
        {
            if (!existingCategory.Active)
            {
                Error("category", $"The category {existingCategory.Name} is inactive.");
            }
        }
        else if (!plan.NewCategories.ContainsKey(categoryKey))
        {
            var categoryProfile = Get("category_tax_profile");
            if (categoryProfile.Length == 0)
            {
                Error("category_tax_profile", $"{categoryName} is a new category: name its tax profile.");
            }
            else if (ProfileId(categoryProfile, out var message) is { } profileId)
            {
                plan.NewCategories[categoryKey] = new NewCategory(categoryName, profileId);
            }
            else
            {
                Error("category_tax_profile", message);
            }
        }

        Guid? taxProfileId = null;
        if (Get("tax_profile") is { Length: > 0 } overrideName)
        {
            taxProfileId = ProfileId(overrideName, out var message);
            Error("tax_profile", message);
        }

        var unit = Get("unit") is { Length: > 0 } u ? u.ToLowerInvariant() : "piece";
        Error("unit", CatalogRules.ValidateUnit(unit));
        var openPrice = Flag(Get("open_price"), false, "open_price", Error);
        var active = Flag(Get("active"), true, "active", Error);
        decimal? price = null;
        if (openPrice)
        {
            if (Get("price").Length > 0)
            {
                Error("price", "An open-price product has no price; leave it empty.");
            }
        }
        else
        {
            price = Money(Get("price"), "price", required: true, Error);
        }

        var deposit = Money(Get("deposit"), "deposit", required: false, Error) ?? 0m;
        var minQuantity = Quantity(Get("min_quantity"), "min_quantity", Error);
        var step = Quantity(Get("step"), "step", Error);
        if (minQuantity is { } min && step is { } st)
        {
            var (minError, stepError) = CatalogRules.ValidateQuantities(min, st);
            Error("min_quantity", minError);
            Error("step", stepError);
        }

        short? minAge = null;
        if (Get("min_age") is { Length: > 0 } ageText)
        {
            if (int.TryParse(ageText, NumberStyles.None, CultureInfo.InvariantCulture, out var age) && CatalogRules.ValidateMinAge(age) is null)
            {
                minAge = (short)age;
            }
            else
            {
                Error("min_age", "Use an age from 1 to 99, or leave it empty.");
            }
        }

        var barcodes = new List<Barcode>();
        foreach (var raw in Get("barcodes").Split(CatalogCsv.BarcodeSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var (barcode, problem) = BarcodeRules.Parse(raw);
            if (problem is { } p)
            {
                Error("barcodes", $"{raw}: {BarcodeRules.Message(p)}");
            }
            else if (barcodesInFile.TryGetValue(barcode!.Code, out var otherRow))
            {
                Error("barcodes", $"{raw} is already on row {otherRow}.");
            }
            else
            {
                barcodesInFile[barcode.Code] = row.Line;
                barcodes.Add(barcode);
            }
        }

        // Match: SKU, then barcode. A barcode of a product with a different SKU is a conflict, not a merge.
        Product? existing = sku is not null && productsBySku.TryGetValue(sku.ToLowerInvariant(), out var bySku) ? bySku : null;
        foreach (var barcode in barcodes)
        {
            if (!barcodeOwners.TryGetValue(barcode.Code, out var ownerId) || ownerId == existing?.Id)
            {
                continue;
            }

            var owner = products[ownerId];
            if (existing is null && (sku is null || owner.NormalizedSku is null))
            {
                existing = owner;
            }
            else
            {
                Error("barcodes", $"{barcode.Code} belongs to {owner.Name}{(owner.Sku is null ? string.Empty : $" (SKU {owner.Sku})")}.");
            }
        }

        // Without a SKU or barcode, the name is the only key: it must be unambiguous.
        if (existing is null && sku is null && barcodes.Count == 0 && name.Trim().Length > 0)
        {
            var byName = products.Values.Where(p => p.NormalizedSku is null && string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            if (byName.Count == 1)
            {
                existing = byName[0];
            }
            else if (byName.Count > 1)
            {
                Error("sku", $"{byName.Count} products are named {name.Trim()}; add a SKU or barcode to tell them apart.");
            }
        }

        if (existing is not null && matchedInFile.TryGetValue(existing.Id, out var firstRow))
        {
            Error("sku", $"This is the same product as row {firstRow}.");
        }
        else if (existing is not null)
        {
            matchedInFile[existing.Id] = row.Line;
        }

        if (errors.Count > 0)
        {
            return new ImportRowResult(row.Line, "error", sku, name, errors);
        }

        var values = new ProductEndpoints.ProductValues(
            name.Trim(), sku, Guid.Empty, taxProfileId, unit, price, openPrice, minQuantity!.Value, step!.Value, minAge, deposit, barcodes);
        if (existing is null)
        {
            plan.Products.Add(new PlannedProduct(row.Line, null, categoryKey, values, ProductEndpoints.Describe(null, values, currency), active));
            return new ImportRowResult(row.Line, "create", sku, name, []);
        }

        var categoryId = categories.TryGetValue(categoryKey, out var category) ? category.Id : Guid.Empty;
        var before = ProductEndpoints.ValuesOf(existing, barcodesByProduct.GetValueOrDefault(existing.Id, []), currency);
        var changes = ProductEndpoints.Describe(before, values with { CategoryId = categoryId == Guid.Empty ? before.CategoryId : categoryId }, currency);
        if (categoryId == Guid.Empty)
        {
            changes["category"] = [existing.CategoryId, categoryName];
        }

        if (active != existing.Active)
        {
            changes["active"] = [existing.Active, active];
        }

        if (changes.Count == 0)
        {
            return new ImportRowResult(row.Line, "unchanged", sku, name, []);
        }

        plan.Products.Add(new PlannedProduct(row.Line, existing, categoryKey, values with { CategoryId = categoryId }, changes, active));
        return new ImportRowResult(row.Line, "update", sku, name, []);
    }

    private Guid? ProfileId(string name, out string? message)
    {
        if (profiles.TryGetValue(CatalogSupport.Normalize(name), out var profile))
        {
            message = profile.Active ? null : $"The tax profile {profile.Name} is inactive.";
            return profile.Active ? profile.Id : null;
        }

        message = $"There is no tax profile named {name}.";
        return null;
    }

    private static bool Flag(string text, bool empty, string column, Action<string, string?> error)
    {
        switch (text.ToLowerInvariant())
        {
            case "":
                return empty;
            case "true" or "yes" or "1":
                return true;
            case "false" or "no" or "0":
                return false;
            default:
                error(column, "Use true or false.");
                return empty;
        }
    }

    private decimal? Money(string text, string column, bool required, Action<string, string?> error)
    {
        if (text.Length == 0)
        {
            if (required)
            {
                error(column, "Enter a price, or set open_price to true.");
            }

            return null;
        }

        if (text.Contains(',', StringComparison.Ordinal))
        {
            error(column, "Use a dot as the decimal separator, for example 1.99.");
            return null;
        }

        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
        {
            error(column, "Use a number such as 1.99 (no currency signs).");
            return null;
        }

        var digits = Currencies.DigitsOf(currency);
        if (CatalogRules.Decimals(amount) > digits)
        {
            error(column, digits == 0 ? $"{currency} has no decimals." : $"Use at most {digits} decimals.");
            return null;
        }

        return amount;
    }

    private static decimal? Quantity(string text, string column, Action<string, string?> error)
    {
        if (text.Length == 0)
        {
            return 1m;
        }

        if (decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        error(column, "Use a number such as 1 or 0.005.");
        return null;
    }
}
