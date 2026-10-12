using HelloPos.Kernel.Businesses;

namespace HelloPos.Api.Features.Businesses;

/// <summary>
/// Business settings as sent by clients (contract <c>BusinessSettings</c>). Fields are loosely typed so
/// every invalid value becomes a field error from <see cref="BusinessSettingsRules"/>, never a binding
/// failure. The cash rounding step travels in minor units (Constitution, Principle I).
/// </summary>
public sealed record BusinessSettingsRequest(
    string? Name,
    string? CountryCode,
    string? CurrencyCode,
    string? PricingMode,
    int FinancialYearStartMonth,
    string? TimeZone,
    long CashRoundingStepMinor,
    bool? DepositsTaxable = null);

/// <summary>Contract <c>Business</c>.</summary>
public sealed record BusinessResponse(
    Guid Id,
    string Name,
    string CountryCode,
    string CurrencyCode,
    string PricingMode,
    int FinancialYearStartMonth,
    string TimeZone,
    long CashRoundingStepMinor,
    bool DepositsTaxable,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Contract <c>BusinessSummary</c>; <c>Role</c> is the signed-in account's role there (owner or manager).</summary>
public sealed record BusinessSummaryResponse(Guid Id, string Name, string CountryCode, string CurrencyCode, string Role);

public static class BusinessContracts
{
    public const string TaxInclusive = "tax_inclusive";
    public const string TaxExclusive = "tax_exclusive";

    /// <summary>Minor units per currency unit; INR, USD and CAD all have two decimals.</summary>
    private const decimal MinorPerUnit = 100m;

    /// <summary>Converts and validates; returns the settings, or the field errors (API field names).</summary>
    public static (BusinessSettings? Settings, Dictionary<string, string[]> Errors) Validate(BusinessSettingsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        PricingMode? pricing = request.PricingMode switch
        {
            TaxInclusive => Kernel.Businesses.PricingMode.TaxInclusive,
            TaxExclusive => Kernel.Businesses.PricingMode.TaxExclusive,
            _ => null,
        };
        if (pricing is null)
        {
            errors["pricingMode"] = ["Choose tax-inclusive or tax-exclusive pricing."];
        }

        var settings = new BusinessSettings(
            request.Name?.Trim() ?? string.Empty,
            request.CountryCode ?? string.Empty,
            request.CurrencyCode ?? string.Empty,
            pricing ?? Kernel.Businesses.PricingMode.TaxExclusive,
            request.FinancialYearStartMonth,
            request.TimeZone ?? string.Empty,
            request.CashRoundingStepMinor / MinorPerUnit);
        foreach (var (field, message) in BusinessSettingsRules.Validate(settings))
        {
            var key = field == "cashRoundingStep" ? "cashRoundingStepMinor" : field;
            errors.TryAdd(key, [message]);
        }

        return (errors.Count == 0 ? settings : null, errors);
    }

    public static void Apply(BusinessSettings settings, Business business)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(business);
        business.Name = settings.Name;
        business.CountryCode = settings.CountryCode;
        business.CurrencyCode = settings.CurrencyCode;
        business.PricingMode = settings.PricingMode;
        business.FinancialYearStartMonth = (short)settings.FinancialYearStartMonth;
        business.TimeZone = settings.TimeZone;
        business.CashRoundingStep = settings.CashRoundingStep;
    }

    public static BusinessResponse ToResponse(Business business)
    {
        ArgumentNullException.ThrowIfNull(business);
        return new BusinessResponse(
            business.Id,
            business.Name,
            business.CountryCode,
            business.CurrencyCode,
            business.PricingMode == Kernel.Businesses.PricingMode.TaxInclusive ? TaxInclusive : TaxExclusive,
            business.FinancialYearStartMonth,
            business.TimeZone,
            (long)(business.CashRoundingStep * MinorPerUnit),
            business.DepositsTaxable,
            business.CreatedAt,
            business.UpdatedAt);
    }
}
