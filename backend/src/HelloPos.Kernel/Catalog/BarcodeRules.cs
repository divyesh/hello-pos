namespace HelloPos.Kernel.Catalog;

public enum BarcodeKind
{
    Ean13,
    Ean8,
    Plu,
}

/// <summary>Why a barcode is refused; the web and the API show the same message.</summary>
public enum BarcodeProblem
{
    NotDigits,
    Length,
    CheckDigit,
    PluRange,
    InStore,
}

public sealed record Barcode(string Code, BarcodeKind Kind);

/// <summary>
/// Normalizes and validates product barcodes (feature #11, research R4; shared cases in
/// <c>contracts/barcode-cases.json</c>). UPC-A is stored in its 13-digit EAN form, so the same code typed
/// either way is one barcode. In-store codes (scale labels, restricted ranges) are refused for now.
/// </summary>
public static class BarcodeRules
{
    public const int MaxBarcodesPerProduct = 20;

    /// <summary>The normalized barcode, or the reason it is refused.</summary>
    public static (Barcode? Barcode, BarcodeProblem? Problem) Parse(string? input)
    {
        var digits = new string((input ?? string.Empty).Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());
        if (digits.Length == 0 || !digits.All(char.IsAsciiDigit))
        {
            return (null, BarcodeProblem.NotDigits);
        }

        switch (digits.Length)
        {
            case 4:
                return IsPlu(digits) ? (new Barcode(digits, BarcodeKind.Plu), null) : (null, BarcodeProblem.PluRange);
            case 5:
                return digits[0] == '9' && IsPlu(digits[1..]) ? (new Barcode(digits, BarcodeKind.Plu), null) : (null, BarcodeProblem.PluRange);
            case 8:
                return Gtin(digits, BarcodeKind.Ean8, inStore: digits[0] == '2');
            case 12:
                return Gtin("0" + digits, BarcodeKind.Ean13, inStore: digits[0] == '2');
            case 13:
                return Gtin(digits, BarcodeKind.Ean13, inStore: digits[0] == '2' || digits.StartsWith("02", StringComparison.Ordinal));
            default:
                return (null, BarcodeProblem.Length);
        }
    }

    public static string Message(BarcodeProblem problem) => problem switch
    {
        BarcodeProblem.NotDigits => "A barcode has only digits (spaces and dashes are ignored).",
        BarcodeProblem.Length => "Use a 12-digit UPC-A, 13-digit EAN-13, 8-digit EAN-8, or a 4–5 digit PLU.",
        BarcodeProblem.CheckDigit => "The last digit (check digit) does not match; check the number.",
        BarcodeProblem.PluRange => "PLU codes are 3000–4999, or 9 followed by one of those for organic produce.",
        BarcodeProblem.InStore => "Codes starting with 2 are for in-store scale labels, which are not supported yet.",
        _ => "This barcode is not valid.",
    };

    /// <summary>The GS1 mod-10 check digit for <paramref name="body"/> (all digits but the last).</summary>
    public static int CheckDigit(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var total = 0;
        for (var i = 0; i < body.Length; i++)
        {
            var digit = body[body.Length - 1 - i] - '0';
            total += i % 2 == 0 ? digit * 3 : digit;
        }

        return (10 - total % 10) % 10;
    }

    private static (Barcode?, BarcodeProblem?) Gtin(string code, BarcodeKind kind, bool inStore)
    {
        if (CheckDigit(code[..^1]) != code[^1] - '0')
        {
            return (null, BarcodeProblem.CheckDigit);
        }

        return inStore ? (null, BarcodeProblem.InStore) : (new Barcode(code, kind), null);
    }

    private static bool IsPlu(string fourDigits) => int.Parse(fourDigits, System.Globalization.CultureInfo.InvariantCulture) is >= 3000 and <= 4999;
}
