using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Kernel.Pricing;
using Microsoft.EntityFrameworkCore;
using EngineTaxProfile = HelloPos.Kernel.Pricing.TaxProfile;

namespace HelloPos.Api.Features.Catalog;

/// <summary>Everything the pricing engine needs about one product at one moment (feature #11, FR-008).</summary>
public sealed record ProductPricing(
    Guid ProductId,
    string Name,
    long? UnitPrice,
    bool OpenPrice,
    string Unit,
    decimal MinQuantity,
    decimal Step,
    long DepositPerUnit,
    Guid TaxProfileId,
    EngineTaxProfile TaxProfile,
    Discount? ItemDiscount,
    short? MinAge,
    bool Active);

/// <summary>
/// Catalog data turned into pricing-engine input for one moment (research R5). Selling (#14) and the
/// server's recalculation of a sale (#15) both build their <see cref="SaleInput"/> from here, so they
/// price with identical values.
/// </summary>
public sealed record CatalogPriceFeed(
    string Currency,
    PricingMode Pricing,
    long CashRoundingStep,
    bool DepositsTaxable,
    IReadOnlyDictionary<Guid, ProductPricing> Products)
{
    /// <summary>A sale line for <paramref name="productId"/>; open-price products need <paramref name="enteredPrice"/>.</summary>
    public LineInput Line(Guid productId, string lineId, decimal quantity, long? enteredPrice = null)
    {
        var product = Products[productId];
        var price = product.OpenPrice
            ? enteredPrice ?? throw new InvalidOperationException($"{product.Name} is sold at an entered price.")
            : enteredPrice ?? product.UnitPrice!.Value;
        return new LineInput(lineId, price, quantity, product.TaxProfile)
        {
            OriginalUnitPrice = !product.OpenPrice && enteredPrice is not null && enteredPrice != product.UnitPrice ? product.UnitPrice : null,
            OpenPrice = product.OpenPrice,
            Unit = product.Unit,
            MinQuantity = product.MinQuantity,
            Step = product.Step,
            ItemDiscount = product.ItemDiscount,
            DepositPerUnit = product.DepositPerUnit,
        };
    }

    public SaleInput Sale(IReadOnlyList<LineInput> lines) => new(Currency, Pricing, lines)
    {
        CashRoundingStep = CashRoundingStep,
        DepositsTaxable = DepositsTaxable,
    };
}

public static class CatalogPricing
{
    /// <summary>The feed for <paramref name="productIds"/> of the current business at <paramref name="at"/>; unknown ids are left out.</summary>
    public static async Task<CatalogPriceFeed> ForAsync(HelloPosDbContext db, IReadOnlyCollection<Guid> productIds, DateTimeOffset at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(productIds);
        var business = await CatalogSupport.BusinessAsync(db, cancellationToken).ConfigureAwait(false);
        var date = CatalogSupport.LocalDate(business, at);
        var currency = business.CurrencyCode;

        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
        var categoryIds = products.Select(p => p.CategoryId).Distinct().ToList();
        var defaults = await db.Categories.AsNoTracking().Where(c => categoryIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.DefaultTaxProfileId, cancellationToken).ConfigureAwait(false);
        var profileIds = products.Select(p => p.TaxProfileId ?? defaults[p.CategoryId]).Distinct().ToList();
        var profiles = await TaxProfilesInForceAsync(db, profileIds, date, cancellationToken).ConfigureAwait(false);
        var ids = products.Select(p => p.Id).ToList();
        var discounts = await db.ItemDiscounts.AsNoTracking()
            .Where(d => ids.Contains(d.ProductId) && d.FromDate <= date && (d.ToDate == null || d.ToDate >= date))
            .ToDictionaryAsync(d => d.ProductId, cancellationToken).ConfigureAwait(false);

        var feed = products.ToDictionary(p => p.Id, p =>
        {
            var profileId = p.TaxProfileId ?? defaults[p.CategoryId];
            return new ProductPricing(
                p.Id,
                p.Name,
                p.UnitPrice is { } price ? CatalogSupport.ToMinor(price, currency) : null,
                p.OpenPrice,
                p.Unit,
                p.MinQuantity,
                p.Step,
                CatalogSupport.ToMinor(p.DepositPerUnit, currency),
                profileId,
                profiles[profileId],
                discounts.TryGetValue(p.Id, out var d) ? ToDiscount(d, currency) : null,
                p.MinAge,
                p.Active);
        });
        return new CatalogPriceFeed(
            currency,
            business.PricingMode == Kernel.Businesses.PricingMode.TaxInclusive ? PricingMode.TaxInclusive : PricingMode.TaxExclusive,
            CatalogSupport.ToMinor(business.CashRoundingStep, currency),
            business.DepositsTaxable,
            feed);
    }

    /// <summary>Each profile with the components of the version in force on <paramref name="date"/>.</summary>
    private static async Task<Dictionary<Guid, EngineTaxProfile>> TaxProfilesInForceAsync(
        HelloPosDbContext db, List<Guid> profileIds, DateOnly date, CancellationToken cancellationToken)
    {
        var profiles = await db.TaxProfiles.AsNoTracking().Where(p => profileIds.Contains(p.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
        var versions = (await db.TaxProfileVersions.AsNoTracking()
                .Where(v => profileIds.Contains(v.TaxProfileId) && v.EffectiveFrom <= date)
                .ToListAsync(cancellationToken).ConfigureAwait(false))
            .GroupBy(v => v.TaxProfileId)
            .ToDictionary(g => g.Key, g => g.MaxBy(v => v.EffectiveFrom)!.Id);
        var versionIds = versions.Values.ToList();
        var components = (await db.TaxComponents.AsNoTracking()
                .Where(c => versionIds.Contains(c.TaxProfileVersionId)).OrderBy(c => c.Position)
                .ToListAsync(cancellationToken).ConfigureAwait(false))
            .GroupBy(c => c.TaxProfileVersionId).ToDictionary(g => g.Key, g => g.ToList());

        return profiles.ToDictionary(p => p.Id, p => new EngineTaxProfile(
            p.Name,
            p.Kind switch { TaxKinds.ZeroRated => TaxKind.ZeroRated, TaxKinds.Exempt => TaxKind.Exempt, _ => TaxKind.Taxable },
            versions.TryGetValue(p.Id, out var versionId)
                ? components.GetValueOrDefault(versionId, []).Select(c => new Kernel.Pricing.TaxComponent(c.Label, c.RatePercent)).ToList()
                : []));
    }

    /// <summary>Percent stays a percent; a fixed amount per unit goes to minor units, as the engine expects.</summary>
    private static Discount ToDiscount(ItemDiscount discount, string currency) => discount.Kind == "percent"
        ? new Discount(DiscountKind.Percent, discount.Value)
        : new Discount(DiscountKind.Fixed, CatalogSupport.ToMinor(discount.Value, currency));
}
