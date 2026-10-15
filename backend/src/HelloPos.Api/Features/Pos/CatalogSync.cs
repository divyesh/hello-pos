using System.Data;
using System.IO.Compression;
using System.Text.Json.Serialization;
using HelloPos.Api.Features.Businesses;
using HelloPos.Api.Features.Catalog;
using HelloPos.Api.Features.Catalog.TaxProfiles;
using HelloPos.Api.Features.Settings;
using HelloPos.Api.Infrastructure;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Infrastructure.Pos;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;

namespace HelloPos.Api.Features.Pos;

public sealed record CatalogSyncResponse(
    string Mode,
    long Revision,
    DateTimeOffset ServerTime,
    CatalogSettingsDto Business,
    IReadOnlyList<SyncTaxProfileDto> TaxProfiles,
    IReadOnlyList<SyncCategoryDto> Categories,
    IReadOnlyList<SyncProductDto> Products,
    IReadOnlyList<SyncDiscountDto> Discounts,
    IReadOnlyList<Guid> RemovedDiscountIds,
    IReadOnlyList<SyncPaymentTypeDto> PaymentTypes,
    IReadOnlyList<SyncTableAreaDto> TableAreas,
    IReadOnlyList<SyncTableDto> Tables,
    SyncSellingDto Selling);

public sealed record CatalogSettingsDto(string Currency, string PricingMode, string TimeZone, long CashRoundingStep, bool DepositsTaxable);

public sealed record SyncTaxProfileDto(Guid Id, string Name, string Kind, bool Active, IReadOnlyList<SyncTaxProfileVersionDto> Versions);

public sealed record SyncTaxProfileVersionDto(Guid Id, DateOnly EffectiveFrom, IReadOnlyList<TaxComponentDto> Components);

public sealed record SyncCategoryDto(Guid Id, string Name, int SortOrder, Guid DefaultTaxProfileId, bool Active);

public sealed record SyncProductDto(
    Guid Id,
    string Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Sku,
    Guid CategoryId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? TaxProfileId,
    string Unit,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? UnitPrice,
    bool OpenPrice,
    string MinQuantity,
    string Step,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] short? MinAge,
    long DepositPerUnit,
    bool Active,
    IReadOnlyList<string> Barcodes);

public sealed record SyncDiscountDto(
    Guid Id,
    Guid ProductId,
    string Kind,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Percent,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? AmountOff,
    DateOnly FromDate,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateOnly? ToDate);

public sealed record SyncPaymentTypeDto(
    Guid Id, string Name, string Kind, bool BuiltIn, bool OpensDrawer, bool AsksReference, bool GivesChange, int Position, bool Active);

public sealed record SyncTableAreaDto(Guid Id, string Name, int Position, bool Active);

public sealed record SyncTableDto(Guid Id, Guid AreaId, string Name, int Seats, int Position, bool Active);

public sealed record SyncSellingDto(
    bool UsesTables,
    string DiscountLimitPercent,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? DiscountLimitAmount,
    bool TipsEnabled,
    IReadOnlyList<string> TipPresets);

/// <summary>
/// <c>GET /pos/catalog</c> (feature #13, research R2–R5): the paired till's business catalog, read in one
/// consistent snapshot. The business comes from the device credential only.
/// </summary>
public sealed class CatalogSync : IEndpointMapper
{
    public const string Path = "/pos/catalog";
    public const string Full = "full";
    public const string Changes = "changes";
    public const string RemovedDiscount = "item_discount";

    public void Map(IEndpointRouteBuilder app) =>
        app.MapGet(Path, HandleAsync)
            .WithName("posCatalog")
            .WithTags(PairDevice.Tag)
            .RequireAuthorization(PosAuthentication.DevicePolicy)
            .RequireRateLimiting(PosAuthentication.RateLimit);

    public static async Task<Results<Ok<CatalogSyncResponse>, ValidationProblem>> HandleAsync(
        HelloPosDbContext db, TimeProvider clock, CancellationToken cancellationToken, long? since = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(clock);
        if (since < 0)
        {
            return CatalogSupport.Invalid("since", "Use the revision from the previous download.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken).ConfigureAwait(false);

        // The position is read in the same snapshot as the data: a till asking "since" it next time gets
        // everything committed after this read, and possibly a few rows again (research R1).
        var position = await db.Database.SqlQueryRaw<long>(CatalogRevisionSql.ReadPosition).SingleAsync(cancellationToken).ConfigureAwait(false);
        var limit = await db.Database.SqlQueryRaw<long>(CatalogRevisionSql.ReadLimit).SingleAsync(cancellationToken).ConfigureAwait(false);

        // A since this database cannot have issued (restored, or another server) gets everything.
        var full = since is null || since > limit;
        var from = since ?? 0;
        var business = await CatalogSupport.BusinessAsync(db, cancellationToken).ConfigureAwait(false);
        var currency = business.CurrencyCode;
        var yesterday = CatalogSupport.Today(business, clock).AddDays(-1);

        var profiles = await db.TaxProfiles.AsNoTracking()
            .Where(p => full || p.Revision >= from)
            .OrderBy(p => p.Name).ToListAsync(cancellationToken).ConfigureAwait(false);
        var profileIds = profiles.Select(p => p.Id).ToList();
        var versions = await db.TaxProfileVersions.AsNoTracking()
            .Where(v => full || profileIds.Contains(v.TaxProfileId))
            .OrderBy(v => v.EffectiveFrom).ToListAsync(cancellationToken).ConfigureAwait(false);
        var versionIds = versions.Select(v => v.Id).ToList();
        var components = (await db.TaxComponents.AsNoTracking()
                .Where(c => full || versionIds.Contains(c.TaxProfileVersionId))
                .OrderBy(c => c.Position).ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToLookup(c => c.TaxProfileVersionId);
        var versionsByProfile = versions.ToLookup(v => v.TaxProfileId);
        var categories = await db.Categories.AsNoTracking()
            .Where(c => full || c.Revision >= from)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(cancellationToken).ConfigureAwait(false);
        var products = await db.Products.AsNoTracking()
            .Where(p => full || p.Revision >= from)
            .OrderBy(p => p.Name).ToListAsync(cancellationToken).ConfigureAwait(false);
        var productIds = products.Select(p => p.Id).ToList();
        var barcodes = (await db.ProductBarcodes.AsNoTracking()
                .Where(b => full || productIds.Contains(b.ProductId))
                .OrderBy(b => b.Code).ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToLookup(b => b.ProductId, b => b.Code);

        // A full copy leaves out discounts that ended before yesterday: they never change a price again
        // (research R3). Changes carry every changed discount, whatever its dates.
        var discounts = await db.ItemDiscounts.AsNoTracking()
            .Where(d => full ? d.ToDate == null || d.ToDate >= yesterday : d.Revision >= from)
            .OrderBy(d => d.FromDate)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var removed = full
            ? []
            : await db.CatalogRemovals.AsNoTracking()
                .Where(r => r.Entity == RemovedDiscount && r.Revision >= from)
                .Select(r => r.EntityId).Distinct()
                .ToListAsync(cancellationToken).ConfigureAwait(false);

        // Business settings (feature #12, FR-018): the lists like catalog roots, the selling rules always.
        var paymentTypes = await db.PaymentTypes.AsNoTracking()
            .Where(t => full || t.Revision >= from)
            .OrderBy(t => t.Position).ThenBy(t => t.Name).ToListAsync(cancellationToken).ConfigureAwait(false);
        var areas = await db.TableAreas.AsNoTracking()
            .Where(a => full || a.Revision >= from)
            .OrderBy(a => a.Position).ThenBy(a => a.Name).ToListAsync(cancellationToken).ConfigureAwait(false);
        var tables = await db.DiningTables.AsNoTracking()
            .Where(t => full || t.Revision >= from)
            .OrderBy(t => t.AreaId).ThenBy(t => t.Position).ThenBy(t => t.Name).ToListAsync(cancellationToken).ConfigureAwait(false);
        var selling = await SettingsSupport.SellingAsync(db, cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(new CatalogSyncResponse(
            full ? Full : Changes,
            position,
            clock.GetUtcNow(),
            new CatalogSettingsDto(
                currency,
                business.PricingMode == Kernel.Businesses.PricingMode.TaxInclusive ? BusinessContracts.TaxInclusive : BusinessContracts.TaxExclusive,
                business.TimeZone,
                CatalogSupport.ToMinor(business.CashRoundingStep, currency),
                business.DepositsTaxable),
            profiles.Select(p => new SyncTaxProfileDto(
                p.Id,
                p.Name,
                TaxProfileEndpoints.KindToWire(p.Kind),
                p.Active,
                versionsByProfile[p.Id].Select(v => new SyncTaxProfileVersionDto(
                    v.Id,
                    v.EffectiveFrom,
                    components[v.Id].Select(c => new TaxComponentDto(c.Label, CatalogSupport.Format(c.RatePercent))).ToList())).ToList())).ToList(),
            categories.Select(c => new SyncCategoryDto(c.Id, c.Name, c.SortOrder, c.DefaultTaxProfileId, c.Active)).ToList(),
            products.Select(p => new SyncProductDto(
                p.Id,
                p.Name,
                p.Sku,
                p.CategoryId,
                p.TaxProfileId,
                p.Unit,
                p.UnitPrice is { } price ? CatalogSupport.ToMinor(price, currency) : null,
                p.OpenPrice,
                CatalogSupport.Format(p.MinQuantity),
                CatalogSupport.Format(p.Step),
                p.MinAge,
                CatalogSupport.ToMinor(p.DepositPerUnit, currency),
                p.Active,
                barcodes[p.Id].ToList())).ToList(),
            discounts.Select(d => ToDto(d, currency)).ToList(),
            removed,
            paymentTypes.Select(t => new SyncPaymentTypeDto(
                t.Id, t.Name, PaymentKinds.ToWire(t.Kind), t.BuiltIn, t.OpensDrawer, t.AsksReference, t.GivesChange, t.Position, t.Active)).ToList(),
            areas.Select(a => new SyncTableAreaDto(a.Id, a.Name, a.Position, a.Active)).ToList(),
            tables.Select(t => new SyncTableDto(t.Id, t.AreaId, t.Name, t.Seats, t.Position, t.Active)).ToList(),
            ToDto(selling, currency)));
    }

    /// <summary>Tips off sends no presets, even though the business keeps them for when tips come back on.</summary>
    private static SyncSellingDto ToDto(SellingSettings selling, string currency) => new(
        selling.UsesTables,
        CatalogSupport.Format(selling.DiscountLimitPercent),
        selling.DiscountLimitAmount is { } amount ? CatalogSupport.ToMinor(amount, currency) : null,
        selling.TipsEnabled,
        selling.TipsEnabled ? selling.TipPresets.Order().Select(CatalogSupport.Format).ToList() : []);

    /// <summary>Percent stays a decimal string; a fixed amount goes to minor units (as the pricing engine takes it).</summary>
    private static SyncDiscountDto ToDto(ItemDiscount discount, string currency) => discount.Kind == "percent"
        ? new SyncDiscountDto(discount.Id, discount.ProductId, discount.Kind, CatalogSupport.Format(discount.Value), null, discount.FromDate, discount.ToDate)
        : new SyncDiscountDto(discount.Id, discount.ProductId, discount.Kind, null, CatalogSupport.ToMinor(discount.Value, currency), discount.FromDate, discount.ToDate);
}

/// <summary>
/// Compression for the catalog download only (research R5): it reflects no secrets and no caller input,
/// so compression side channels do not apply; other responses stay uncompressed.
/// </summary>
public static class CatalogSyncCompression
{
    public static IServiceCollection AddCatalogSyncCompression(this IServiceCollection services)
    {
        services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
        });
        services.Configure<BrotliCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);
        services.Configure<GzipCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);
        return services;
    }

    public static IApplicationBuilder UseCatalogSyncCompression(this IApplicationBuilder app) =>
        app.UseWhen(
            context => context.Request.Path.Equals(CatalogSync.Path, StringComparison.OrdinalIgnoreCase),
            branch => branch.UseResponseCompression());
}
