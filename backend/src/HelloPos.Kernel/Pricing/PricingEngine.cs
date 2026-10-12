using System.Numerics;

namespace HelloPos.Kernel.Pricing;

/// <summary>
/// Prices a sale exactly as <c>specs/pricing-rules.md</c> says (feature #10). Pure: no clock, I/O or
/// culture. The Kotlin twin in the Android domain layer must give identical results; both are held to
/// <c>contracts/pricing-cases.json</c>.
/// </summary>
public static class PricingEngine
{
    public const int MaxQuantityDecimals = 3;

    public static PricingResult Price(SaleInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var problems = Validate(input);
        if (problems.Count > 0)
        {
            return new PricingResult.Refused(problems);
        }

        try
        {
            return Compute(input);
        }
        catch (OverflowException)
        {
            return new PricingResult.Refused([new(PricingProblemCodes.AmountOutOfRange)]);
        }
    }

    private static PricingResult Compute(SaleInput input)
    {
        var count = input.Lines.Count;
        var lineAmounts = new BigInteger[count];
        var itemDiscounts = new BigInteger[count];
        var nets = new BigInteger[count];
        var deposits = new BigInteger[count];

        // Steps 1-2: line amount, item discount (never below zero), deposit.
        for (var i = 0; i < count; i++)
        {
            var line = input.Lines[i];
            var (qn, qd) = Fraction(line.Quantity);
            lineAmounts[i] = Exact.RoundHalfUp(line.UnitPrice * qn, qd);
            itemDiscounts[i] = line.ItemDiscount is not { } discount
                ? BigInteger.Zero
                : BigInteger.Min(lineAmounts[i], discount.Kind == DiscountKind.Percent
                    ? Exact.Percent(lineAmounts[i], discount.Value)
                    : Exact.RoundHalfUp(new BigInteger(discount.Value) * qn, qd));
            nets[i] = lineAmounts[i] - itemDiscounts[i];
            deposits[i] = Exact.RoundHalfUp(line.DepositPerUnit * qn, qd);
        }

        // Step 3: order discount, spread by largest remainder (ties to the earlier line).
        var netTotal = Sum(nets);
        var orderDiscount = input.OrderDiscount is not { } order
            ? BigInteger.Zero
            : order.Kind == DiscountKind.Percent
                ? Exact.Percent(netTotal, order.Value)
                : BigInteger.Min(new BigInteger(order.Value), netTotal);
        var shares = Exact.SpreadByLargestRemainder(orderDiscount, nets);

        // Step 4: taxable amount per line (products only) and the tax base (plus taxable deposits).
        var taxables = new BigInteger[count];
        var taxBases = new BigInteger[count];
        for (var i = 0; i < count; i++)
        {
            taxables[i] = nets[i] - shares[i];
            taxBases[i] = taxables[i] + (input.DepositsTaxable ? deposits[i] : BigInteger.Zero);
        }

        // Step 5: tax, rounded once per component (exclusive) or per profile component (inclusive).
        var inclusive = input.Pricing == PricingMode.TaxInclusive;
        var taxes = inclusive ? InclusiveTaxes(input, taxBases) : ExclusiveTaxes(input, taxBases);
        var taxTotal = Sum(taxes.Select(t => (BigInteger)t.Amount));

        // Steps 6-7: tip (the products' pre-tax subtotal, deposits excluded) and total.
        var productSubtotal = Sum(taxables);
        var subtotal = productSubtotal + Sum(deposits);
        var tip = input.Tip switch
        {
            { Amount: { } amount } => new BigInteger(amount),
            { PercentOfSubtotal: { } percent } => Exact.Percent(inclusive ? PreTaxProducts(input, taxables) : productSubtotal, percent),
            _ => BigInteger.Zero,
        };
        var total = subtotal + (inclusive ? BigInteger.Zero : taxTotal) + tip;

        // Steps 8-9: payments in order; cash rounding only on the cash payment that settles the balance.
        var step = new BigInteger(input.CashRoundingStep);
        var payments = new List<PaymentResult>();
        var remaining = total;
        var cashRounding = BigInteger.Zero;
        var change = BigInteger.Zero;
        for (var p = 0; p < input.Payments.Count; p++)
        {
            var payment = input.Payments[p];
            var field = $"payments[{p}]";
            if (remaining.IsZero)
            {
                return new PricingResult.Refused([new(PricingProblemCodes.PaymentAfterSettled, Field: field)]);
            }

            if (payment.Method != PaymentMethod.Cash)
            {
                if (payment.Amount!.Value > remaining)
                {
                    return new PricingResult.Refused([new(PricingProblemCodes.PaymentExceedsBalance, Field: field)]);
                }

                remaining -= payment.Amount.Value;
                payments.Add(new PaymentResult(payment.Method, payment.Amount.Value, 0, 0));
                continue;
            }

            var tendered = new BigInteger(payment.Tendered!.Value);
            var due = CashRound(remaining, step);
            if (tendered >= due)
            {
                payments.Add(new PaymentResult(PaymentMethod.Cash, ToLong(due), ToLong(due - remaining), ToLong(tendered - due)));
                cashRounding += due - remaining;
                change += tendered - due;
                remaining = BigInteger.Zero;
            }
            else if (tendered >= remaining)
            {
                // Covers the exact balance but not the rounded cash due: neither settles nor fits as a part payment.
                return new PricingResult.Refused([new(PricingProblemCodes.InvalidPayment, Field: field)]);
            }
            else
            {
                remaining -= tendered;
                payments.Add(new PaymentResult(PaymentMethod.Cash, ToLong(tendered), 0, 0));
            }
        }

        var lines = input.Lines.Select((line, i) => new PricedLine(
            line.Id, ToLong(lineAmounts[i]), ToLong(itemDiscounts[i]), ToLong(shares[i]), ToLong(taxables[i]), ToLong(deposits[i]))).ToList();
        return new PricingResult.Priced(new PricedSale(
            lines,
            taxes,
            BaseByKind(input, taxBases),
            ToLong(subtotal),
            ToLong(taxTotal),
            ToLong(tip),
            ToLong(total),
            CashDue: step.IsZero ? null : ToLong(CashRound(total, step)),
            payments,
            ToLong(cashRounding),
            ToLong(change),
            ToLong(remaining)));
    }

    /// <summary>Tax-exclusive: group by component (label, rate) across profiles, round once per component.</summary>
    private static List<TaxRow> ExclusiveTaxes(SaleInput input, BigInteger[] taxBases)
    {
        var rows = new List<(string Label, decimal Rate, BigInteger Base)>();
        for (var i = 0; i < input.Lines.Count; i++)
        {
            var profile = input.Lines[i].TaxProfile;
            if (profile.Kind != TaxKind.Taxable)
            {
                continue;
            }

            foreach (var component in profile.Components)
            {
                var at = rows.FindIndex(r => r.Label == component.Label && r.Rate == component.RatePercent);
                if (at < 0)
                {
                    rows.Add((component.Label, component.RatePercent, taxBases[i]));
                }
                else
                {
                    rows[at] = rows[at] with { Base = rows[at].Base + taxBases[i] };
                }
            }
        }

        return rows.Select(r => new TaxRow(r.Label, r.Rate, null, ToLong(r.Base), ToLong(Exact.Percent(r.Base, r.Rate)))).ToList();
    }

    /// <summary>
    /// Tax-inclusive: group lines by profile; each component tax = G x rate / (100 + R), rounded once per
    /// profile component (G the profile gross, R its total rate).
    /// </summary>
    private static List<TaxRow> InclusiveTaxes(SaleInput input, BigInteger[] taxBases)
    {
        var rows = new List<TaxRow>();
        foreach (var (profile, gross) in GroupByProfile(input, taxBases).Where(g => g.Profile.Kind == TaxKind.Taxable))
        {
            var totalRate = profile.Components.Sum(c => c.RatePercent);
            rows.AddRange(profile.Components.Select(c => new TaxRow(
                c.Label, c.RatePercent, profile.Name, ToLong(gross), ToLong(Exact.Scale(gross, c.RatePercent, 100 + totalRate)))));
        }

        return rows;
    }

    /// <summary>Tax-inclusive tip base: per profile, product gross x 100 / (100 + R), rounded once per profile.</summary>
    private static BigInteger PreTaxProducts(SaleInput input, BigInteger[] taxables) =>
        Sum(GroupByProfile(input, taxables).Select(g => g.Profile.Kind == TaxKind.Taxable
            ? Exact.Scale(g.Amount, 100, 100 + g.Profile.Components.Sum(c => c.RatePercent))
            : g.Amount));

    /// <summary>Sums per tax profile (by name), in order of first appearance.</summary>
    private static List<(TaxProfile Profile, BigInteger Amount)> GroupByProfile(SaleInput input, BigInteger[] amounts)
    {
        var groups = new List<(TaxProfile Profile, BigInteger Amount)>();
        for (var i = 0; i < input.Lines.Count; i++)
        {
            var profile = input.Lines[i].TaxProfile;
            var at = groups.FindIndex(g => g.Profile.Name == profile.Name);
            if (at < 0)
            {
                groups.Add((profile, amounts[i]));
            }
            else
            {
                groups[at] = groups[at] with { Amount = groups[at].Amount + amounts[i] };
            }
        }

        return groups;
    }

    /// <summary>Nearest multiple of <paramref name="step"/>, ties up; no step means unchanged.</summary>
    private static BigInteger CashRound(BigInteger amount, BigInteger step) =>
        step.IsZero ? amount : BigInteger.Divide(2 * amount + step, 2 * step) * step;

    private static BaseByKind BaseByKind(SaleInput input, BigInteger[] taxBases)
    {
        BigInteger SumOf(TaxKind kind) => input.Lines.Select((line, i) => line.TaxProfile.Kind == kind ? taxBases[i] : BigInteger.Zero)
            .Aggregate(BigInteger.Zero, (a, b) => a + b);
        return new BaseByKind(ToLong(SumOf(TaxKind.Taxable)), ToLong(SumOf(TaxKind.ZeroRated)), ToLong(SumOf(TaxKind.Exempt)));
    }

    private static BigInteger Sum(IEnumerable<BigInteger> values) => values.Aggregate(BigInteger.Zero, (a, b) => a + b);

    /// <summary>Minor units back to the wire type; beyond int64 the sale is refused (amountOutOfRange).</summary>
    private static long ToLong(BigInteger value) => (long)value;

    /// <summary>Refuses input that cannot be priced (FR-006), naming the line and field.</summary>
    public static List<PricingProblem> Validate(SaleInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var problems = new List<PricingProblem>();
        if (!Currencies.IsSupported(input.Currency))
        {
            problems.Add(new(PricingProblemCodes.UnknownCurrency, Field: "currency"));
        }

        if (input.Lines.Count == 0)
        {
            problems.Add(new(PricingProblemCodes.EmptySale, Field: "lines"));
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in input.Lines)
        {
            if (!ids.Add(line.Id))
            {
                problems.Add(new(PricingProblemCodes.DuplicateLineId, line.Id, "id"));
            }

            if (line.UnitPrice < 0 || line.OriginalUnitPrice < 0)
            {
                problems.Add(new(PricingProblemCodes.InvalidPrice, line.Id, "unitPrice"));
            }

            if (line.DepositPerUnit < 0)
            {
                problems.Add(new(PricingProblemCodes.InvalidPrice, line.Id, "depositPerUnit"));
            }

            if (line.Quantity <= 0 || Decimals(line.Quantity) > MaxQuantityDecimals || line.Step <= 0 || line.MinQuantity < 0)
            {
                problems.Add(new(PricingProblemCodes.InvalidQuantity, line.Id, "quantity"));
            }
            else if (line.Quantity < line.MinQuantity)
            {
                problems.Add(new(PricingProblemCodes.BelowMinimumQuantity, line.Id, "quantity"));
            }
            else if (line.Quantity % line.Step != 0)
            {
                problems.Add(new(PricingProblemCodes.NotMultipleOfStep, line.Id, "quantity"));
            }

            if (line.ItemDiscount is { } discount && !IsValid(discount))
            {
                problems.Add(new(PricingProblemCodes.InvalidDiscount, line.Id, "itemDiscount"));
            }

            if (line.TaxProfile.Components.Any(c => c.RatePercent < 0))
            {
                problems.Add(new(PricingProblemCodes.InvalidPrice, line.Id, "taxProfile"));
            }
        }

        if (input.OrderDiscount is { } order && !IsValid(order))
        {
            problems.Add(new(PricingProblemCodes.InvalidDiscount, Field: "orderDiscount"));
        }

        if (input.Tip is { } tip && (tip.Amount is null == tip.PercentOfSubtotal is null || tip.Amount < 0 || tip.PercentOfSubtotal < 0))
        {
            problems.Add(new(PricingProblemCodes.InvalidTip, Field: "tip"));
        }

        if (input.CashRoundingStep < 0)
        {
            problems.Add(new(PricingProblemCodes.InvalidPayment, Field: "cashRoundingStep"));
        }

        for (var i = 0; i < input.Payments.Count; i++)
        {
            var payment = input.Payments[i];
            var valid = payment.Method == PaymentMethod.Cash
                ? payment.Tendered is > 0 && payment.Amount is null
                : payment.Amount is > 0 && payment.Tendered is null;
            if (!valid)
            {
                problems.Add(new(PricingProblemCodes.InvalidPayment, Field: $"payments[{i}]"));
            }
        }

        return problems;
    }

    private static bool IsValid(Discount discount) => discount.Kind switch
    {
        DiscountKind.Percent => discount.Value is >= 0 and <= 100,
        _ => discount.Value >= 0 && decimal.Truncate(discount.Value) == discount.Value,
    };

    /// <summary>Significant decimal places of <paramref name="value"/> (1.250 has 2).</summary>
    internal static int Decimals(decimal value)
    {
        var digits = 0;
        while (value != decimal.Truncate(value))
        {
            value *= 10;
            digits++;
        }

        return digits;
    }

    /// <summary>An exact decimal as numerator/denominator (denominator a power of ten).</summary>
    internal static (BigInteger Numerator, BigInteger Denominator) Fraction(decimal value)
    {
        var digits = Decimals(value);
        var denominator = BigInteger.Pow(10, digits);
        return (new BigInteger(value * (decimal)denominator), denominator);
    }
}
