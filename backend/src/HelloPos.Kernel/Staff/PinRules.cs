namespace HelloPos.Kernel.Staff;

/// <summary>
/// POS PINs (feature 006, FR-008): 4–6 ASCII digits, refusing the most guessable ones (one repeated
/// digit; ascending or descending runs). Brute-force protection is the POS sign-in's job (#7).
/// </summary>
public static class PinRules
{
    public const int MinLength = 4;
    public const int MaxLength = 6;

    /// <summary>Returns why the PIN is refused, or <c>null</c> when it is acceptable.</summary>
    public static string? Validate(string? pin)
    {
        if (pin is null || pin.Length is < MinLength or > MaxLength || !pin.All(char.IsAsciiDigit))
        {
            return $"Use {MinLength} to {MaxLength} digits.";
        }

        if (pin.All(c => c == pin[0]))
        {
            return "Choose a PIN that is not one repeated digit.";
        }

        if (IsRun(pin, +1) || IsRun(pin, -1))
        {
            return "Choose a PIN that is not a run of digits like 1234.";
        }

        return null;
    }

    private static bool IsRun(string pin, int step)
    {
        for (var i = 1; i < pin.Length; i++)
        {
            if (pin[i] - pin[i - 1] != step)
            {
                return false;
            }
        }

        return true;
    }
}
