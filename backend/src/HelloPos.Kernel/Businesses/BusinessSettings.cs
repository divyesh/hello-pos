namespace HelloPos.Kernel.Businesses;

/// <summary>How prices are entered (specs/pricing-rules.md §3).</summary>
public enum PricingMode
{
    TaxExclusive,
    TaxInclusive,
}

/// <summary>The settings an owner chooses for a business (feature 005, FR-009).</summary>
public sealed record BusinessSettings(
    string Name,
    string CountryCode,
    string CurrencyCode,
    PricingMode PricingMode,
    int FinancialYearStartMonth,
    string TimeZone,
    decimal CashRoundingStep);
