using System.Globalization;
using HelloPos.Kernel.Pricing;

namespace HelloPos.Web.Services;

/// <summary>
/// Shows and reads amounts in the business currency (Constitution I): the API speaks minor units, people
/// type and read currency units with the currency's number of decimals (0 for JPY, 3 for KWD).
/// </summary>
public static class MoneyText
{
    public static string Format(long minor, string currency)
    {
        var digits = Currencies.IsSupported(currency) ? Currencies.DigitsOf(currency) : 2;
        return (minor / Pow10(digits)).ToString("F" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    /// <summary>Parses "1.99" (dot decimal) into minor units; returns an error message instead when it cannot.</summary>
    public static (long? Minor, string? Error) Parse(string? text, string currency)
    {
        var digits = Currencies.IsSupported(currency) ? Currencies.DigitsOf(currency) : 2;
        if (string.IsNullOrWhiteSpace(text))
        {
            return (null, "Enter an amount.");
        }

        if (!decimal.TryParse(text.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
        {
            return (null, "Use digits and a dot, for example 1.99.");
        }

        var minor = amount * Pow10(digits);
        return minor != decimal.Truncate(minor)
            ? (null, digits == 0 ? "This currency has no decimals." : $"Use at most {digits} decimals.")
            : (decimal.ToInt64(minor), null);
    }

    private static decimal Pow10(int digits) => digits switch { 0 => 1m, 1 => 10m, 2 => 100m, _ => 1000m };
}
