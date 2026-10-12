using System.Text;
using HelloPos.Api.Features.Catalog.Products;
using HelloPos.Api.Infrastructure;
using HelloPos.Api.Infrastructure.Identity;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Infrastructure.Tenancy;
using HelloPos.Kernel.Staff;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace HelloPos.Api.Features.Catalog.Import;

public sealed record ImportPreviewResponse(List<ImportRowResult> Rows, int Create, int Update, int Unchanged, int Errors);

public sealed record CatalogImportResponse(Guid Id, DateTimeOffset At, string FileName, int Rows, int Created, int Updated, int Unchanged);

/// <summary>
/// CSV import (feature #11, US4, FR-011): a preview that saves nothing, and an import that checks the
/// file again and applies it all or nothing. Owners and managers. The file never names a business: every
/// name in it is looked up in the open business only.
/// </summary>
public sealed class CatalogImportEndpoints : IEndpointMapper
{
    public void Map(IEndpointRouteBuilder app)
    {
        var imports = app.MapGroup("/catalog/imports")
            .WithTags(CatalogSupport.Tag)
            .RequireRole(StaffRole.Owner, StaffRole.Manager)
            .RequireSameBusiness();
        imports.MapGet("", ListAsync).WithName("listCatalogImports");

        // The X-HelloPos-Client header (required below) guards these cookie-authenticated uploads against
        // cross-site requests, as it does every other write; form antiforgery tokens are not used.
        imports.MapPost("/preview", PreviewAsync).WithName("previewCatalogImport").RequireClientHeader().DisableAntiforgery();
        imports.MapPost("", ImportAsync).WithName("importCatalog").RequireClientHeader().DisableAntiforgery();
    }

    public static async Task<Ok<List<CatalogImportResponse>>> ListAsync(HelloPosDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var imports = await db.CatalogImports.AsNoTracking().OrderByDescending(i => i.At).Take(100)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(imports.Select(ToResponse).ToList());
    }

    public static async Task<Results<Ok<ImportPreviewResponse>, ValidationProblem>> PreviewAsync(
        IFormFile file, HelloPosDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(db);
        var (text, fileError) = await ReadAsync(file, cancellationToken).ConfigureAwait(false);
        if (fileError is not null)
        {
            return fileError;
        }

        var importer = await CatalogImporter.LoadAsync(db, cancellationToken).ConfigureAwait(false);
        var (field, message, plan) = importer.Check(text!);
        return plan is null ? CatalogSupport.Invalid(field!, message!) : TypedResults.Ok(ToPreview(plan));
    }

    public static async Task<Results<Ok<CatalogImportResponse>, ValidationProblem, ProblemHttpResult>> ImportAsync(
        IFormFile file, ICurrentStaff current, HelloPosDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(clock);
        var (text, fileError) = await ReadAsync(file, cancellationToken).ConfigureAwait(false);
        if (fileError is not null)
        {
            return fileError;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var importer = await CatalogImporter.LoadAsync(db, cancellationToken).ConfigureAwait(false);
        var (field, message, plan) = importer.Check(text!);
        if (plan is null)
        {
            return CatalogSupport.Invalid(field!, message!);
        }

        var failing = plan.Rows.Where(r => r.Outcome == "error").ToList();
        if (failing.Count > 0)
        {
            return CatalogSupport.Invalid(new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["rows"] = failing.Take(100).SelectMany(r => r.Errors.Select(e => $"Row {r.Row}, {e.Column}: {e.Message}")).ToList(),
            });
        }

        var actor = (await current.GetAsync(cancellationToken).ConfigureAwait(false))!;
        var business = await CatalogSupport.BusinessAsync(db, cancellationToken).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        var import = new CatalogImport
        {
            At = now,
            ActorStaffId = actor.Id,
            FileName = Path.GetFileName(file.FileName) is { Length: > 0 and <= 200 } name ? name : "catalog.csv",
            Rows = plan.Rows.Count,
            Created = plan.Count("create"),
            Updated = plan.Count("update"),
            Unchanged = plan.Count("unchanged"),
        };
        db.CatalogImports.Add(import);

        var categoryIds = (await db.Categories.ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(c => c.NormalizedName, c => c.Id, StringComparer.Ordinal);
        foreach (var (key, newCategory) in plan.NewCategories)
        {
            var category = new Category
            {
                Name = newCategory.Name.Trim(),
                NormalizedName = key,
                DefaultTaxProfileId = newCategory.TaxProfileId,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Categories.Add(category);
            categoryIds[key] = category.Id;
            db.RecordChange(clock, actor.Id, CatalogEntityNames.Category, category.Id, CatalogActions.Created,
                new Dictionary<string, object?[]> { ["name"] = [null, category.Name], ["defaultTaxProfileId"] = [null, category.DefaultTaxProfileId] }, import.Id);
        }

        foreach (var planned in plan.Products)
        {
            var values = planned.Values with { CategoryId = categoryIds[planned.CategoryKey] };
            if (planned.Existing is null)
            {
                var product = new Product { Name = values.Name, Unit = values.Unit, Active = planned.Active, CreatedAt = now, UpdatedAt = now };
                ProductEndpoints.Apply(product, values);
                db.Products.Add(product);
                AddBarcodes(db, product.Id, values);
                db.RecordChange(clock, actor.Id, CatalogEntityNames.Product, product.Id, CatalogActions.Created,
                    ProductEndpoints.Describe(null, values, business.CurrencyCode), import.Id);
            }
            else
            {
                var product = planned.Existing;
                ProductEndpoints.Apply(product, values);
                product.Active = planned.Active;
                product.Version++;
                product.UpdatedAt = now;
                var existing = await db.ProductBarcodes.Where(b => b.ProductId == product.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
                var wanted = values.Barcodes.Select(b => b.Code).ToHashSet(StringComparer.Ordinal);
                db.ProductBarcodes.RemoveRange(existing.Where(b => !wanted.Contains(b.Code)));
                AddBarcodes(db, product.Id, values with { Barcodes = values.Barcodes.Where(b => existing.All(e => e.Code != b.Code)).ToList() });
                db.RecordChange(clock, actor.Id, CatalogEntityNames.Product, product.Id, CatalogActions.Updated, planned.Changes, import.Id);
            }
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException e) when (e is DbUpdateConcurrencyException || CatalogSupport.IsUniqueViolation(e))
        {
            return CatalogSupport.CatalogChanged();
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(ToResponse(import));
    }

    private static void AddBarcodes(HelloPosDbContext db, Guid productId, ProductEndpoints.ProductValues values)
    {
        foreach (var barcode in values.Barcodes)
        {
            db.ProductBarcodes.Add(new ProductBarcode { ProductId = productId, Code = barcode.Code, Kind = ProductEndpoints.KindName(barcode.Kind) });
        }
    }

    /// <summary>The file as UTF-8 text, or a field error (too large, empty).</summary>
    private static async Task<(string? Text, ValidationProblem? Error)> ReadAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length > CatalogCsv.MaxBytes)
        {
            return (null, CatalogSupport.Invalid("file", "The file is larger than 5 MB; split it."));
        }

        using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return (await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false), null);
    }

    private static ImportPreviewResponse ToPreview(CatalogImporter.Plan plan) =>
        new(plan.Rows, plan.Count("create"), plan.Count("update"), plan.Count("unchanged"), plan.Count("error"));

    private static CatalogImportResponse ToResponse(CatalogImport import) =>
        new(import.Id, import.At, import.FileName, import.Rows, import.Created, import.Updated, import.Unchanged);
}
