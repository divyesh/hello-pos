using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HelloPos.Api.Infrastructure;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Infrastructure.Tenancy;
using HelloPos.Kernel.Pricing;
using HelloPos.Kernel.Staff;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace HelloPos.Api.Features.Catalog.Import;

public sealed record CatalogFieldChange(
    string Field,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Old,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? New);

public sealed record CatalogChangeResponse(DateTimeOffset At, string Actor, string Entity, Guid EntityId, string Action, List<CatalogFieldChange> Changes);

/// <summary>
/// The price list as CSV in the import format (feature #11, US5, FR-013), and the catalog history
/// (FR-015). Exported cells that a spreadsheet would run as a formula are guarded (CSV injection).
/// </summary>
public sealed class CatalogExportEndpoints : IEndpointMapper
{
    public const int HistoryPageSize = 50;

    public void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/catalog/products/export", ExportAsync).WithName("exportProducts").WithTags(CatalogSupport.Tag)
            .RequireRole(StaffRole.Owner, StaffRole.Manager).RequireSameBusiness();
        app.MapGet("/catalog/changes", ChangesAsync).WithName("listCatalogChanges").WithTags(CatalogSupport.Tag)
            .RequireRole(StaffRole.Owner, StaffRole.Manager).RequireSameBusiness();
    }

    public static async Task<FileContentHttpResult> ExportAsync(HelloPosDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var business = await CatalogSupport.BusinessAsync(db, cancellationToken).ConfigureAwait(false);
        var currency = business.CurrencyCode;
        var digits = Currencies.IsSupported(currency) ? Currencies.DigitsOf(currency) : 2;
        var profiles = await db.TaxProfiles.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken).ConfigureAwait(false);
        var categories = await db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, cancellationToken).ConfigureAwait(false);
        var products = await db.Products.AsNoTracking().Where(p => p.Active).OrderBy(p => p.Name.ToLower()).ThenBy(p => p.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var barcodes = (await db.ProductBarcodes.AsNoTracking().OrderBy(b => b.Code).ToListAsync(cancellationToken).ConfigureAwait(false))
            .GroupBy(b => b.ProductId).ToDictionary(g => g.Key, g => string.Join(CatalogCsv.BarcodeSeparator, g.Select(b => b.Code)));

        string Money(decimal amount) => amount.ToString("F" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

        var csv = new StringBuilder();
        csv.Append(string.Join(',', CatalogCsv.Columns)).Append('\n');
        foreach (var p in products)
        {
            var category = categories[p.CategoryId];
            string?[] cells =
            [
                p.Sku, p.Name, category.Name, profiles[category.DefaultTaxProfileId], p.TaxProfileId is { } t ? profiles[t] : null, p.Unit,
                p.UnitPrice is { } price ? Money(price) : null, p.OpenPrice ? "true" : "false", CatalogSupport.Format(p.MinQuantity),
                CatalogSupport.Format(p.Step), barcodes.GetValueOrDefault(p.Id), p.MinAge?.ToString(CultureInfo.InvariantCulture),
                Money(p.DepositPerUnit), "true",
            ];
            csv.Append(string.Join(',', cells.Select(CsvReader.Cell))).Append('\n');
        }

        return TypedResults.File(new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(),
            "text/csv; charset=utf-8", "price-list.csv");
    }

    public static async Task<Results<Ok<List<CatalogChangeResponse>>, ValidationProblem>> ChangesAsync(
        Guid? entityId, int? page, HelloPosDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var pageNumber = page ?? 1;
        if (pageNumber < 1)
        {
            return CatalogSupport.Invalid("page", "Use page 1 or more.");
        }

        var changes = await db.CatalogChanges.AsNoTracking()
            .Where(c => entityId == null || c.EntityId == entityId)
            .OrderByDescending(c => c.At).ThenByDescending(c => c.Id)
            .Skip((pageNumber - 1) * HistoryPageSize).Take(HistoryPageSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var names = await db.StaffMembers.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.DisplayName, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(changes.Select(c => new CatalogChangeResponse(
            c.At, names.GetValueOrDefault(c.ActorStaffId, string.Empty), c.Entity, c.EntityId, c.Action, Fields(c.Changes))).ToList());
    }

    /// <summary>The stored <c>{ field: [old, new] }</c> as text values (absent when there was none).</summary>
    internal static List<CatalogFieldChange> Fields(string json)
    {
        static string? Text(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => value.GetString(),
            _ => value.GetRawText(),
        };

        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject()
            .Select(p => new CatalogFieldChange(p.Name, Text(p.Value[0]), Text(p.Value[1])))
            .ToList();
    }
}
