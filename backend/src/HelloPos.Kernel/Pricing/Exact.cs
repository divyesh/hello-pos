using System.Numerics;

namespace HelloPos.Kernel.Pricing;

/// <summary>
/// Exact arithmetic on minor units (research R2): values are integers or exact fractions, and the only
/// rounding is half-up at the points the pricing rules name. The Kotlin twin does the same operations.
/// </summary>
internal static class Exact
{
    /// <summary><paramref name="numerator"/> / <paramref name="denominator"/> rounded half-up (away from zero).</summary>
    public static BigInteger RoundHalfUp(BigInteger numerator, BigInteger denominator)
    {
        if (denominator.Sign <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(denominator));
        }

        return numerator.Sign >= 0
            ? (2 * numerator + denominator) / (2 * denominator)
            : -((2 * -numerator + denominator) / (2 * denominator));
    }

    /// <summary><paramref name="amount"/> × <paramref name="percent"/> / 100, rounded half-up.</summary>
    public static BigInteger Percent(BigInteger amount, decimal percent)
    {
        var (pn, pd) = PricingEngine.Fraction(percent);
        return RoundHalfUp(amount * pn, pd * 100);
    }

    /// <summary><paramref name="amount"/> x <paramref name="multiplier"/> / <paramref name="divisor"/>, rounded half-up.</summary>
    public static BigInteger Scale(BigInteger amount, decimal multiplier, decimal divisor)
    {
        var (mn, md) = PricingEngine.Fraction(multiplier);
        var (dn, dd) = PricingEngine.Fraction(divisor);
        return RoundHalfUp(amount * mn * dd, md * dn);
    }

    /// <summary>
    /// Spreads <paramref name="total"/> over parts in proportion to <paramref name="weights"/>: each share is
    /// rounded down, then the leftover units go one each to the largest remainders; equal remainders go to
    /// the earlier part. The shares add up exactly to <paramref name="total"/> (research R5).
    /// </summary>
    public static BigInteger[] SpreadByLargestRemainder(BigInteger total, IReadOnlyList<BigInteger> weights)
    {
        var shares = new BigInteger[weights.Count];
        var weightSum = weights.Aggregate(BigInteger.Zero, (a, b) => a + b);
        if (total.IsZero || weightSum.IsZero)
        {
            return shares;
        }

        var remainders = new BigInteger[weights.Count];
        var given = BigInteger.Zero;
        for (var i = 0; i < weights.Count; i++)
        {
            shares[i] = BigInteger.DivRem(total * weights[i], weightSum, out remainders[i]);
            given += shares[i];
        }

        var leftover = (int)(total - given);
        foreach (var i in Enumerable.Range(0, weights.Count).OrderByDescending(i => remainders[i]).ThenBy(i => i).Take(leftover))
        {
            shares[i] += 1;
        }

        return shares;
    }
}
