using System.Text.RegularExpressions;

namespace HelloPos.Kernel.Businesses;

/// <summary>
/// The single validation of business settings (feature 005, FR-011), used by the API (authoritative)
/// and the web form (immediate feedback). Keys are the API's camelCase field names.
/// </summary>
public static partial class BusinessSettingsRules
{
    public const int NameMaxLength = 200;

    public static IReadOnlyDictionary<string, string> Validate(BusinessSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(settings.Name) || settings.Name.Trim().Length > NameMaxLength)
        {
            errors["name"] = $"Enter a business name of 1 to {NameMaxLength} characters.";
        }

        var country = Countries.Find(settings.CountryCode);
        if (country is null)
        {
            errors["countryCode"] = "Choose India, the United States or Canada.";
        }

        if (settings.CurrencyCode is null || !CurrencyCode().IsMatch(settings.CurrencyCode))
        {
            errors["currencyCode"] = "Enter a three-letter ISO 4217 currency code, for example INR.";
        }

        if (!Enum.IsDefined(settings.PricingMode))
        {
            errors["pricingMode"] = "Choose tax-inclusive or tax-exclusive pricing.";
        }

        if (settings.FinancialYearStartMonth is < 1 or > 12)
        {
            errors["financialYearStartMonth"] = "Choose a month from January to December.";
        }

        if (country is not null && !country.TimeZones.Contains(settings.TimeZone, StringComparer.Ordinal))
        {
            errors["timeZone"] = $"Choose a time zone in {country.Name}.";
        }
        else if (country is null && string.IsNullOrWhiteSpace(settings.TimeZone))
        {
            errors["timeZone"] = "Choose a time zone.";
        }

        if (!Countries.CashRoundingSteps.Contains(settings.CashRoundingStep))
        {
            errors["cashRoundingStep"] = "Choose no rounding, 0.05, 0.10 or 1.00.";
        }

        return errors;
    }

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CurrencyCode();
}
