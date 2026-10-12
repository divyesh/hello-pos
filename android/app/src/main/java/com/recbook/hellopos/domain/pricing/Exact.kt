package com.recbook.hellopos.domain.pricing

import java.math.BigDecimal
import java.math.BigInteger

/**
 * Exact arithmetic on minor units (research R2): integers or exact fractions, rounded half-up only at the
 * points the pricing rules name. The twin of the .NET kernel's `Exact`.
 */
internal object Exact {
    private val TWO = BigInteger.valueOf(2)
    private val HUNDRED = BigInteger.valueOf(100)

    /** [numerator] / [denominator] rounded half-up (away from zero). */
    fun roundHalfUp(numerator: BigInteger, denominator: BigInteger): BigInteger {
        require(denominator.signum() > 0)
        return if (numerator.signum() >= 0) {
            (TWO * numerator + denominator) / (TWO * denominator)
        } else {
            -((TWO * -numerator + denominator) / (TWO * denominator))
        }
    }

    /** An exact decimal as numerator / denominator (a power of ten). */
    fun fraction(value: BigDecimal): Pair<BigInteger, BigInteger> {
        val stripped = value.stripTrailingZeros()
        return if (stripped.scale() <= 0) {
            stripped.toBigIntegerExact() to BigInteger.ONE
        } else {
            stripped.unscaledValue() to BigInteger.TEN.pow(stripped.scale())
        }
    }

    /** [amount] × [percent] / 100, rounded half-up. */
    fun percent(amount: BigInteger, percent: BigDecimal): BigInteger {
        val (pn, pd) = fraction(percent)
        return roundHalfUp(amount * pn, pd * HUNDRED)
    }

    /** [amount] x [multiplier] / [divisor], rounded half-up. */
    fun scale(amount: BigInteger, multiplier: BigDecimal, divisor: BigDecimal): BigInteger {
        val (mn, md) = fraction(multiplier)
        val (dn, dd) = fraction(divisor)
        return roundHalfUp(amount * mn * dd, md * dn)
    }

    /**
     * Spreads [total] over parts in proportion to [weights]: each share rounded down, the leftover units one
     * each to the largest remainders, equal remainders to the earlier part (research R5).
     */
    fun spreadByLargestRemainder(total: BigInteger, weights: List<BigInteger>): List<BigInteger> {
        val weightSum = weights.fold(BigInteger.ZERO, BigInteger::add)
        if (total.signum() == 0 || weightSum.signum() == 0) return List(weights.size) { BigInteger.ZERO }
        val divided = weights.map { (total * it).divideAndRemainder(weightSum) }
        val shares = divided.map { it[0] }.toMutableList()
        val leftover = (total - shares.fold(BigInteger.ZERO, BigInteger::add)).toInt()
        weights.indices
            .sortedWith(compareByDescending<Int> { divided[it][1] }.thenBy { it })
            .take(leftover)
            .forEach { shares[it] = shares[it] + BigInteger.ONE }
        return shares
    }
}

/** The value as a Long, or [ArithmeticException] when it does not fit (BigInteger.longValueExact needs API 31). */
internal fun BigInteger.toLongExact(): Long {
    if (bitLength() > 63) throw ArithmeticException("Amount out of range")
    return toLong()
}
