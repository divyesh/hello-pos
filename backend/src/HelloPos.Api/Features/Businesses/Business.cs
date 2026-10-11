using HelloPos.Kernel.Businesses;

namespace HelloPos.Api.Features.Businesses;

/// <summary>
/// The tenant root (spec FR-005). Holds only what later features need for pricing and reporting;
/// owners create and manage businesses (feature 005), staff arrive with roadmap #7.
/// </summary>
public sealed class Business
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    /// <summary><c>IN</c>, <c>US</c> or <c>CA</c>.</summary>
    public required string CountryCode { get; set; }

    /// <summary>ISO 4217, for example <c>INR</c>.</summary>
    public required string CurrencyCode { get; set; }

    public PricingMode PricingMode { get; set; }

    /// <summary>1–12; India uses April (4).</summary>
    public short FinancialYearStartMonth { get; set; } = 1;

    /// <summary>IANA time zone, for example <c>Asia/Kolkata</c>.</summary>
    public required string TimeZone { get; set; }

    /// <summary>Cash rounding step in currency units: 0 (none), 0.05, 0.10 or 1.00 (pricing-rules.md §5).</summary>
    public decimal CashRoundingStep { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
