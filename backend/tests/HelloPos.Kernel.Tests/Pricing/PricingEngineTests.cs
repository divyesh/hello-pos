using System.Diagnostics;
using System.Globalization;
using HelloPos.Kernel.Pricing;

namespace HelloPos.Kernel.Tests.Pricing;

/// <summary>Engine properties the shared cases cannot express: purity, culture independence, speed (SC-004).</summary>
public sealed class PricingEngineTests
{
    private static readonly TaxProfile Hst = new("HST", TaxKind.Taxable, [new TaxComponent("HST", 13m)]);

    [Fact]
    public void SameInput_GivesEqualResults()
    {
        var input = BigSale(50);

        var first = Assert.IsType<PricingResult.Priced>(PricingEngine.Price(input)).Sale;
        var second = Assert.IsType<PricingResult.Priced>(PricingEngine.Price(input)).Sale;

        Assert.Equal(SharedPricingCasesTests.ToJson(first).ToJsonString(), SharedPricingCasesTests.ToJson(second).ToJsonString());
    }

    [Fact]
    public void Results_DoNotDependOnTheCurrentCulture()
    {
        var input = BigSale(20);
        var invariant = Price(input, CultureInfo.InvariantCulture);

        foreach (var culture in new[] { "fr-CA", "de-DE", "hi-IN", "ar-KW" })
        {
            Assert.Equal(invariant, Price(input, CultureInfo.GetCultureInfo(culture)));
        }
    }

    [Fact]
    public void TwoHundredLines_PriceQuickly()
    {
        var input = BigSale(200);
        PricingEngine.Price(input); // warm up

        var watch = Stopwatch.StartNew();
        var result = PricingEngine.Price(input);
        watch.Stop();

        Assert.IsType<PricingResult.Priced>(result);
        Assert.True(watch.ElapsedMilliseconds < 50, $"200 lines took {watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void CashJustShortOfTheRoundedDue_IsRefusedNotTaken()
    {
        var input = new SaleInput("CAD", PricingMode.TaxExclusive, [new LineInput("a", 1043, 1m, Hst with { Kind = TaxKind.Exempt, Components = [] })])
        {
            CashRoundingStep = 5,
            Payments = [new PaymentInput(PaymentMethod.Cash, Tendered: 1044)],
        };

        var refused = Assert.IsType<PricingResult.Refused>(PricingEngine.Price(input));

        Assert.Equal(PricingProblemCodes.InvalidPayment, Assert.Single(refused.Problems).Code);
    }

    private static string Price(SaleInput input, CultureInfo culture)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture;
        try
        {
            return SharedPricingCasesTests.ToJson(((PricingResult.Priced)PricingEngine.Price(input)).Sale).ToJsonString();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>Weighed and counted lines with item discounts, deposits, an order discount, a tip and cash.</summary>
    private static SaleInput BigSale(int lines) => new(
        "CAD",
        PricingMode.TaxExclusive,
        Enumerable.Range(1, lines).Select(i => new LineInput($"l{i}", 99 + (i * 37 % 900), i % 3 == 0 ? 1.255m : i % 5 + 1, Hst)
        {
            Step = i % 3 == 0 ? 0.005m : 1m,
            MinQuantity = i % 3 == 0 ? 0.005m : 1m,
            ItemDiscount = i % 4 == 0 ? new Discount(DiscountKind.Percent, 12.5m) : null,
            DepositPerUnit = i % 7 == 0 ? 10 : 0,
        }).ToList())
    {
        CashRoundingStep = 5,
        OrderDiscount = new Discount(DiscountKind.Fixed, 333),
        Tip = new Tip(null, 15m),
    };
}
