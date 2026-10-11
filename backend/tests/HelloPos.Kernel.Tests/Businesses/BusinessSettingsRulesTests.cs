using HelloPos.Kernel.Businesses;

namespace HelloPos.Kernel.Tests.Businesses;

/// <summary>Feature 005 FR-009–FR-011, SC-005: one validation used by the API and the web form.</summary>
public sealed class BusinessSettingsRulesTests
{
    [Theory]
    [InlineData("IN", "INR", PricingMode.TaxInclusive, 4, "Asia/Kolkata", "1.00")]
    [InlineData("US", "USD", PricingMode.TaxExclusive, 1, "America/New_York", "0")]
    [InlineData("CA", "CAD", PricingMode.TaxExclusive, 1, "America/Toronto", "0.05")]
    public void CountryDefaults_MatchTheSpec_AndAreValid(string country, string currency, PricingMode pricing, int month, string zone, string rounding)
    {
        var defaults = Countries.Find(country)!;

        Assert.Equal(currency, defaults.CurrencyCode);
        Assert.Equal(pricing, defaults.PricingMode);
        Assert.Equal(month, defaults.FinancialYearStartMonth);
        Assert.Equal(zone, defaults.DefaultTimeZone);
        Assert.Equal(decimal.Parse(rounding, System.Globalization.CultureInfo.InvariantCulture), defaults.CashRoundingStep);
        Assert.Empty(BusinessSettingsRules.Validate(defaults.ToSettings("My shop")));
    }

    [Theory]
    [InlineData("name", "")]
    [InlineData("name", "201")]
    [InlineData("countryCode", "FR")]
    [InlineData("currencyCode", "rupee")]
    [InlineData("financialYearStartMonth", "13")]
    [InlineData("timeZone", "Europe/Paris")]
    [InlineData("cashRoundingStep", "0.25")]
    public void EachInvalidField_IsReportedByName(string field, string value)
    {
        var valid = Countries.Find("IN")!.ToSettings("My shop");
        var settings = field switch
        {
            "name" => valid with { Name = value == "201" ? new string('x', 201) : value },
            "countryCode" => valid with { CountryCode = value },
            "currencyCode" => valid with { CurrencyCode = value },
            "financialYearStartMonth" => valid with { FinancialYearStartMonth = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture) },
            "timeZone" => valid with { TimeZone = value },
            "cashRoundingStep" => valid with { CashRoundingStep = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture) },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };

        var errors = BusinessSettingsRules.Validate(settings);

        Assert.True(errors.ContainsKey(field), $"expected an error for {field}; got {string.Join(", ", errors.Keys)}");
    }

    [Fact]
    public void TimeZone_MustBelongToTheChosenCountry()
    {
        var settings = Countries.Find("IN")!.ToSettings("My shop") with { TimeZone = "America/Toronto" };

        Assert.True(BusinessSettingsRules.Validate(settings).ContainsKey("timeZone"));
    }
}
