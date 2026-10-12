namespace HelloPos.Kernel.Catalog;

/// <summary>
/// Catalog field rules shared by the API and the web back office (feature #11, data-model.md), so both
/// give the same messages. Each returns an error message, or <c>null</c> when valid.
/// </summary>
public static class CatalogRules
{
    public const int TaxProfileNameMaxLength = 60;
    public const int CategoryNameMaxLength = 60;
    public const int ProductNameMaxLength = 120;
    public const int SkuMaxLength = 40;
    public const int ComponentLabelMaxLength = 20;
    public const int MaxQuantityDecimals = 3;
    public const int MaxRateDecimals = 3;
    public const int MaxComponents = 5;

    public static readonly IReadOnlyList<string> Units = ["piece", "g", "kg", "ml", "l"];

    public static string? ValidateName(string? name, int maxLength) =>
        string.IsNullOrWhiteSpace(name) ? "Enter a name."
        : name.Trim().Length > maxLength ? $"Use at most {maxLength} characters."
        : null;

    public static string? ValidateSku(string? sku) =>
        sku is not null && sku.Trim().Length > SkuMaxLength ? $"Use at most {SkuMaxLength} characters." : null;

    public static string? ValidateUnit(string? unit) =>
        unit is not null && Units.Contains(unit) ? null : "Choose piece, g, kg, ml or l.";

    /// <summary>Minimum sale quantity and step: positive, at most 3 decimals, the minimum a multiple of the step.</summary>
    public static (string? MinQuantity, string? Step) ValidateQuantities(decimal minQuantity, decimal step)
    {
        string? Positive(decimal value) =>
            value <= 0 ? "Must be more than 0."
            : Decimals(value) > MaxQuantityDecimals ? $"Use at most {MaxQuantityDecimals} decimals."
            : value > 100_000 ? "Too large."
            : null;

        var stepError = Positive(step);
        var minError = Positive(minQuantity);
        if (minError is null && stepError is null && minQuantity % step != 0)
        {
            minError = "The minimum must be a multiple of the step.";
        }

        return (minError, stepError);
    }

    /// <summary>A tax rate: 0–100 percent with at most 3 decimals.</summary>
    public static string? ValidateRate(decimal rate) =>
        rate is < 0 or > 100 ? "Use a rate from 0 to 100."
        : Decimals(rate) > MaxRateDecimals ? $"Use at most {MaxRateDecimals} decimals."
        : null;

    public static string? ValidateComponentLabel(string? label) => ValidateName(label, ComponentLabelMaxLength);

    public static string? ValidateMinAge(int? minAge) => minAge is null or (>= 1 and <= 99) ? null : "Use an age from 1 to 99.";

    public static string? ValidateDiscount(bool percent, decimal value) =>
        value < 0 ? "Must be 0 or more."
        : percent && value > 100 ? "A percentage is at most 100."
        : percent && Decimals(value) > MaxRateDecimals ? $"Use at most {MaxRateDecimals} decimals."
        : !percent && decimal.Truncate(value) != value ? "Use a whole number of minor units."
        : null;

    public static string? ValidateDiscountPeriod(DateOnly from, DateOnly? to) =>
        to is { } end && end < from ? "The end date is before the start date." : null;

    /// <summary>Significant decimal places of <paramref name="value"/> (1.250 has 2).</summary>
    public static int Decimals(decimal value)
    {
        var digits = 0;
        while (value != decimal.Truncate(value) && digits < 29)
        {
            value *= 10;
            digits++;
        }

        return digits;
    }
}
