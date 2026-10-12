using HelloPos.Kernel.Catalog;

namespace HelloPos.Kernel.Tests.Catalog;

/// <summary>Feature #11 field rules shared by the API and the web.</summary>
public sealed class CatalogRulesTests
{
    [Theory]
    [InlineData("1", "1", null, null)]
    [InlineData("0.005", "0.005", null, null)]
    [InlineData("0.25", "0.005", null, null)]
    [InlineData("0.3", "0.25", "The minimum must be a multiple of the step.", null)]
    [InlineData("0", "1", "Must be more than 0.", null)]
    [InlineData("1", "0.0005", null, "Use at most 3 decimals.")]
    public void Quantities(string min, string step, string? minError, string? stepError)
    {
        var (minMessage, stepMessage) = CatalogRules.ValidateQuantities(decimal.Parse(min, System.Globalization.CultureInfo.InvariantCulture), decimal.Parse(step, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(minError, minMessage);
        Assert.Equal(stepError, stepMessage);
    }

    [Theory]
    [InlineData("13", true)]
    [InlineData("2.5", true)]
    [InlineData("8.875", true)]
    [InlineData("0", true)]
    [InlineData("100", true)]
    [InlineData("100.001", false)]
    [InlineData("-1", false)]
    [InlineData("8.8755", false)]
    public void Rates(string rate, bool valid) =>
        Assert.Equal(valid, CatalogRules.ValidateRate(decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture)) is null);

    [Fact]
    public void NamesUnitsAgesDiscounts()
    {
        Assert.NotNull(CatalogRules.ValidateName(" ", 60));
        Assert.NotNull(CatalogRules.ValidateName(new string('x', 61), 60));
        Assert.Null(CatalogRules.ValidateName(" Beverages ", 60));
        Assert.Null(CatalogRules.ValidateUnit("kg"));
        Assert.NotNull(CatalogRules.ValidateUnit("lb"));
        Assert.Null(CatalogRules.ValidateMinAge(null));
        Assert.Null(CatalogRules.ValidateMinAge(19));
        Assert.NotNull(CatalogRules.ValidateMinAge(0));
        Assert.NotNull(CatalogRules.ValidateMinAge(100));
        Assert.Null(CatalogRules.ValidateDiscount(percent: true, 12.5m));
        Assert.NotNull(CatalogRules.ValidateDiscount(percent: true, 101m));
        Assert.Null(CatalogRules.ValidateDiscount(percent: false, 50m));
        Assert.NotNull(CatalogRules.ValidateDiscount(percent: false, 0.5m));
        Assert.NotNull(CatalogRules.ValidateDiscountPeriod(new DateOnly(2026, 11, 7), new DateOnly(2026, 11, 1)));
        Assert.Null(CatalogRules.ValidateDiscountPeriod(new DateOnly(2026, 11, 1), null));
    }
}
