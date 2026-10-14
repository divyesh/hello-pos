using System.Globalization;
using HelloPos.Kernel.Catalog;

namespace HelloPos.Kernel.Settings;

/// <summary>
/// Business-settings field rules shared by the API and the web back office (feature #12, research R8), so
/// both give the same messages. Each returns an error message, or <c>null</c> when valid.
/// </summary>
public static class SettingsRules
{
    public const int PaymentTypeNameMaxLength = 40;
    public const int AreaNameMaxLength = 40;
    public const int TableNameMaxLength = 20;
    public const int RangeAffixMaxLength = 10;
    public const int MaxActivePaymentTypes = 20;
    public const int MaxTablesPerBulk = 200;
    public const int MaxTipPresets = 3;
    public const int MinSeats = 1;
    public const int MaxSeats = 50;
    public const int DefaultSeats = 4;
    public const int MaxPercentDecimals = 2;

    /// <summary>Payment kinds on the wire; <c>cash</c> belongs to the built-in Cash type only.</summary>
    public const string Cash = "cash";

    public static readonly IReadOnlyList<string> PaymentKinds = [Cash, "externalCard", "digitalWallet", "cheque", "voucher", "other"];

    /// <summary>Kinds whose payments usually carry a reference (a card approval, a UPI id, a cheque number).</summary>
    public static readonly IReadOnlyList<string> ReferenceKinds = ["externalCard", "digitalWallet", "cheque"];

    public static string? ValidateName(string? name, int maxLength) => CatalogRules.ValidateName(name, maxLength);

    /// <summary>A kind an owner may choose for a new type (anything but cash).</summary>
    public static string? ValidateNewKind(string? kind) =>
        kind is not null && kind != Cash && PaymentKinds.Contains(kind) ? null
        : "Choose card on an external terminal, UPI or digital wallet, cheque, voucher or other.";

    public static string? ValidateSeats(int seats) =>
        seats is >= MinSeats and <= MaxSeats ? null : $"Use {MinSeats} to {MaxSeats} seats.";

    /// <summary>The discount approval limit: 0–100 percent with at most 2 decimals.</summary>
    public static string? ValidateLimitPercent(decimal percent) => ValidatePercent(percent, 0);

    public static string? ValidateLimitAmount(long? amount) => amount is null or >= 0 ? null : "Must be 0 or more.";

    /// <summary>Tip presets: at most 3, each 1–100 with at most 2 decimals, all different.</summary>
    public static string? ValidateTipPresets(IReadOnlyList<decimal> presets)
    {
        ArgumentNullException.ThrowIfNull(presets);
        if (presets.Count > MaxTipPresets)
        {
            return $"Use at most {MaxTipPresets} tip percentages.";
        }

        return presets.Select(p => ValidatePercent(p, 1)).FirstOrDefault(e => e is not null)
            ?? (presets.Distinct().Count() != presets.Count ? "Each tip percentage must be different." : null);
    }

    /// <summary>The names of a bulk range, or why the range is refused.</summary>
    public static (IReadOnlyList<string> Names, string? Error) TableRange(int from, int to, string? prefix, string? suffix)
    {
        prefix ??= string.Empty;
        suffix ??= string.Empty;
        string? error =
            from < 1 ? "Start at 1 or more."
            : to < from ? "The last number is before the first."
            : to - from >= MaxTablesPerBulk ? $"Add at most {MaxTablesPerBulk} tables at once."
            : prefix.Length > RangeAffixMaxLength || suffix.Length > RangeAffixMaxLength ? $"Use at most {RangeAffixMaxLength} characters before or after the number."
            : null;
        if (error is not null)
        {
            return ([], error);
        }

        var names = Enumerable.Range(from, to - from + 1).Select(n => (prefix + n.ToString(CultureInfo.InvariantCulture) + suffix).Trim()).ToList();
        return names.Any(n => n.Length > TableNameMaxLength)
            ? ([], $"Table names are at most {TableNameMaxLength} characters.")
            : (names, null);
    }

    /// <summary>Lower-cased trimmed form used for "unique ignoring case".</summary>
    public static string Normalize(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Trim().ToLowerInvariant();
    }

    private static string? ValidatePercent(decimal percent, decimal minimum) =>
        percent < minimum || percent > 100 ? $"Use a percentage from {minimum.ToString(CultureInfo.InvariantCulture)} to 100."
        : CatalogRules.Decimals(percent) > MaxPercentDecimals ? $"Use at most {MaxPercentDecimals} decimals."
        : null;
}
