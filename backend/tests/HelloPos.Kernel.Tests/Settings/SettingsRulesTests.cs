using HelloPos.Kernel.Settings;

namespace HelloPos.Kernel.Tests.Settings;

/// <summary>Feature #12 field rules shared by the API and the web (research R8).</summary>
public sealed class SettingsRulesTests
{
    [Theory]
    [InlineData("externalCard", true)]
    [InlineData("digitalWallet", true)]
    [InlineData("cheque", true)]
    [InlineData("voucher", true)]
    [InlineData("other", true)]
    [InlineData("cash", false)]
    [InlineData("crypto", false)]
    [InlineData(null, false)]
    public void NewPaymentKinds_AreAnythingButCash(string? kind, bool valid) =>
        Assert.Equal(valid, SettingsRules.ValidateNewKind(kind) is null);

    [Theory]
    [InlineData("0", null)]
    [InlineData("10", null)]
    [InlineData("12.5", null)]
    [InlineData("100", null)]
    [InlineData("100.01", "Use a percentage from 0 to 100.")]
    [InlineData("-1", "Use a percentage from 0 to 100.")]
    [InlineData("10.125", "Use at most 2 decimals.")]
    public void TheDiscountLimit(string percent, string? error) =>
        Assert.Equal(error, SettingsRules.ValidateLimitPercent(decimal.Parse(percent, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void AmountsAndSeats()
    {
        Assert.Null(SettingsRules.ValidateLimitAmount(null));
        Assert.Null(SettingsRules.ValidateLimitAmount(0));
        Assert.NotNull(SettingsRules.ValidateLimitAmount(-1));
        Assert.Null(SettingsRules.ValidateSeats(1));
        Assert.Null(SettingsRules.ValidateSeats(50));
        Assert.NotNull(SettingsRules.ValidateSeats(0));
        Assert.NotNull(SettingsRules.ValidateSeats(51));
    }

    [Fact]
    public void TipPresets()
    {
        Assert.Null(SettingsRules.ValidateTipPresets([]));
        Assert.Null(SettingsRules.ValidateTipPresets([10m, 15m, 20m]));
        Assert.Null(SettingsRules.ValidateTipPresets([12.5m]));
        Assert.Equal("Use at most 3 tip percentages.", SettingsRules.ValidateTipPresets([5m, 10m, 15m, 20m]));
        Assert.Equal("Each tip percentage must be different.", SettingsRules.ValidateTipPresets([10m, 10.00m]));
        Assert.Equal("Use a percentage from 1 to 100.", SettingsRules.ValidateTipPresets([0m]));
        Assert.Equal("Use at most 2 decimals.", SettingsRules.ValidateTipPresets([12.555m]));
    }

    [Fact]
    public void Tips_On_NeedAtLeastOnePreset()
    {
        Assert.Null(SettingsRules.ValidateTips(false, []));
        Assert.Null(SettingsRules.ValidateTips(true, [10m]));
        Assert.Equal("Add at least one tip percentage.", SettingsRules.ValidateTips(true, []));
        Assert.Equal("Use at most 3 tip percentages.", SettingsRules.ValidateTips(false, [5m, 10m, 15m, 20m]));
    }

    [Fact]
    public void TableRanges_NameEachTable()
    {
        Assert.Equal(["1", "2", "3"], SettingsRules.TableRange(1, 3, null, null).Names);
        Assert.Equal(["T9", "T10"], SettingsRules.TableRange(9, 10, "T", null).Names);
        Assert.Equal(["5 Patio"], SettingsRules.TableRange(5, 5, null, " Patio").Names);
        Assert.Equal(200, SettingsRules.TableRange(1, 200, null, null).Names.Count);
    }

    [Theory]
    [InlineData(0, 5, "Start at 1 or more.")]
    [InlineData(5, 4, "The last number is before the first.")]
    [InlineData(1, 201, "Add at most 200 tables at once.")]
    public void BadRanges_AreRefused(int from, int to, string error)
    {
        var (names, message) = SettingsRules.TableRange(from, to, null, null);

        Assert.Empty(names);
        Assert.Equal(error, message);
    }

    [Fact]
    public void LongAffixesAndNames_AreRefused()
    {
        Assert.NotNull(SettingsRules.TableRange(1, 2, new string('x', 11), null).Error);
        Assert.Equal("Table names are at most 20 characters.", SettingsRules.TableRange(1000, 1001, "Terrace-", "-Upstairs").Error);
    }

    [Fact]
    public void Names_AreComparedIgnoringCaseAndSpaces()
    {
        Assert.Equal("upi", SettingsRules.Normalize("  UPI "));
        Assert.NotNull(SettingsRules.ValidateName(new string('x', 41), SettingsRules.PaymentTypeNameMaxLength));
        Assert.Null(SettingsRules.ValidateName("Card (terminal)", SettingsRules.PaymentTypeNameMaxLength));
    }
}
