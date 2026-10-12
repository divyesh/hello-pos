namespace HelloPos.Kernel.Pricing;

// Input and output of the pricing engine (feature #10, data-model.md). Money is integer minor units
// (Constitution I); quantities and percentages are exact decimals.

public enum PricingMode
{
    TaxExclusive,
    TaxInclusive,
}

public enum TaxKind
{
    Taxable,
    ZeroRated,
    Exempt,
}

public enum DiscountKind
{
    Percent,
    Fixed,
}

public enum PaymentMethod
{
    Cash,
    Card,
    Other,
}

public sealed record TaxComponent(string Label, decimal RatePercent);

public sealed record TaxProfile(string Name, TaxKind Kind, IReadOnlyList<TaxComponent> Components);

/// <summary>Percent (0–100) of an amount, or a fixed amount in minor units (per unit for item discounts).</summary>
public sealed record Discount(DiscountKind Kind, decimal Value);

/// <summary>A fixed tip in minor units, or a percentage of the products' pre-tax subtotal.</summary>
public sealed record Tip(long? Amount, decimal? PercentOfSubtotal);

/// <summary>Card and other payments give <see cref="Amount"/>; cash gives <see cref="Tendered"/>.</summary>
public sealed record PaymentInput(PaymentMethod Method, long? Amount = null, long? Tendered = null);

public sealed record LineInput(string Id, long UnitPrice, decimal Quantity, TaxProfile TaxProfile)
{
    /// <summary>The price before a manager override; carried for the audit trail, not used in the arithmetic.</summary>
    public long? OriginalUnitPrice { get; init; }

    public bool OpenPrice { get; init; }

    public string Unit { get; init; } = "piece";

    public decimal MinQuantity { get; init; } = 1m;

    public decimal Step { get; init; } = 1m;

    public Discount? ItemDiscount { get; init; }

    public long DepositPerUnit { get; init; }
}

public sealed record SaleInput(string Currency, PricingMode Pricing, IReadOnlyList<LineInput> Lines)
{
    /// <summary>Cash rounding step in minor units; 0 means none.</summary>
    public long CashRoundingStep { get; init; }

    public bool DepositsTaxable { get; init; }

    public Discount? OrderDiscount { get; init; }

    public Tip? Tip { get; init; }

    public IReadOnlyList<PaymentInput> Payments { get; init; } = [];
}

public sealed record PricedLine(string Id, long LineAmount, long ItemDiscount, long OrderDiscountShare, long Taxable, long Deposit);

/// <summary>One tax row; <see cref="Profile"/> is set for tax-inclusive sales (rounded once per profile component).</summary>
public sealed record TaxRow(string Label, decimal RatePercent, string? Profile, long Base, long Amount);

public sealed record BaseByKind(long Taxable, long ZeroRated, long Exempt);

public sealed record PaymentResult(PaymentMethod Method, long Applied, long Rounding, long Change);

public sealed record PricedSale(
    IReadOnlyList<PricedLine> Lines,
    IReadOnlyList<TaxRow> Taxes,
    BaseByKind BaseByKind,
    long Subtotal,
    long TaxTotal,
    long Tip,
    long Total,
    long? CashDue,
    IReadOnlyList<PaymentResult> Payments,
    long CashRounding,
    long Change,
    long Remaining);

/// <summary>Why a sale cannot be priced; <see cref="Code"/> is one of <see cref="PricingProblemCodes"/>.</summary>
public sealed record PricingProblem(string Code, string? LineId = null, string? Field = null);

public static class PricingProblemCodes
{
    public const string EmptySale = "emptySale";
    public const string UnknownCurrency = "unknownCurrency";
    public const string DuplicateLineId = "duplicateLineId";
    public const string InvalidPrice = "invalidPrice";
    public const string InvalidQuantity = "invalidQuantity";
    public const string BelowMinimumQuantity = "belowMinimumQuantity";
    public const string NotMultipleOfStep = "notMultipleOfStep";
    public const string InvalidDiscount = "invalidDiscount";
    public const string InvalidTip = "invalidTip";
    public const string InvalidPayment = "invalidPayment";
    public const string PaymentExceedsBalance = "paymentExceedsBalance";
    public const string PaymentAfterSettled = "paymentAfterSettled";
    public const string AmountOutOfRange = "amountOutOfRange";
}

public abstract record PricingResult
{
    public sealed record Priced(PricedSale Sale) : PricingResult;

    public sealed record Refused(IReadOnlyList<PricingProblem> Problems) : PricingResult;
}
