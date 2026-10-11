namespace HelloPos.Kernel.Businesses;

/// <summary>A supported country, its defaults (feature 005, FR-010) and its allowed time zones.</summary>
public sealed record CountryDefaults(
    string CountryCode,
    string Name,
    string CurrencyCode,
    PricingMode PricingMode,
    int FinancialYearStartMonth,
    string DefaultTimeZone,
    decimal CashRoundingStep,
    IReadOnlyList<string> TimeZones)
{
    public BusinessSettings ToSettings(string name) =>
        new(name, CountryCode, CurrencyCode, PricingMode, FinancialYearStartMonth, DefaultTimeZone, CashRoundingStep);
}

/// <summary>
/// The countries Hello POS serves (Constitution, Technology: India, USA, Canada). Time zones are listed
/// explicitly so validation is identical in the API and in the browser (no dependency on the platform's
/// time-zone database).
/// </summary>
public static class Countries
{
    public static readonly IReadOnlyList<CountryDefaults> All =
    [
        new("IN", "India", "INR", PricingMode.TaxInclusive, 4, "Asia/Kolkata", 1.00m,
            ["Asia/Kolkata"]),
        new("US", "United States", "USD", PricingMode.TaxExclusive, 1, "America/New_York", 0m,
            ["America/New_York", "America/Chicago", "America/Denver", "America/Phoenix", "America/Los_Angeles",
             "America/Anchorage", "Pacific/Honolulu"]),
        new("CA", "Canada", "CAD", PricingMode.TaxExclusive, 1, "America/Toronto", 0.05m,
            ["America/St_Johns", "America/Halifax", "America/Toronto", "America/Winnipeg", "America/Regina",
             "America/Edmonton", "America/Vancouver", "America/Whitehorse"]),
    ];

    /// <summary>Cash rounding steps an owner may choose, in the currency's units (pricing-rules.md §5).</summary>
    public static readonly IReadOnlyList<decimal> CashRoundingSteps = [0m, 0.05m, 0.10m, 1.00m];

    public static CountryDefaults? Find(string? countryCode) =>
        All.FirstOrDefault(c => string.Equals(c.CountryCode, countryCode, StringComparison.Ordinal));
}
