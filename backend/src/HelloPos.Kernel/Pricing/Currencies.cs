namespace HelloPos.Kernel.Pricing;

/// <summary>
/// The ISO 4217 currencies the engine prices in and their minor-unit digits (research R3). Kept as a fixed
/// table, identical to the Kotlin one, so both sides agree regardless of platform currency data.
/// </summary>
public static class Currencies
{
    private static readonly Dictionary<string, int> MinorDigits = new(StringComparer.Ordinal)
    {
        ["USD"] = 2,
        ["CAD"] = 2,
        ["INR"] = 2,
        ["EUR"] = 2,
        ["GBP"] = 2,
        ["AUD"] = 2,
        ["JPY"] = 0,
        ["KWD"] = 3,
        ["BHD"] = 3,
    };

    public static bool IsSupported(string? code) => code is not null && MinorDigits.ContainsKey(code);

    /// <summary>Digits after the decimal point of the currency's minor unit (2 for cents).</summary>
    public static int DigitsOf(string code) => MinorDigits[code];
}
