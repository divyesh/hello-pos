package com.recbook.hellopos.domain.money

import java.math.BigDecimal

/**
 * The currencies the pricing engine prices in and their minor-unit digits (research R3). A fixed table,
 * identical to the .NET kernel's, so both sides agree regardless of platform currency data.
 */
object Currencies {
    private val minorDigits = mapOf(
        "USD" to 2, "CAD" to 2, "INR" to 2, "EUR" to 2, "GBP" to 2, "AUD" to 2,
        "JPY" to 0,
        "KWD" to 3, "BHD" to 3,
    )

    fun isSupported(code: String?): Boolean = code != null && code in minorDigits

    fun digitsOf(code: String): Int = minorDigits.getValue(code)
}

/**
 * An amount of money: integer minor units (the wire format, Constitution I) with its currency. Converts to
 * an exact [BigDecimal] for display; never a binary floating-point number.
 */
data class Money(val minor: Long, val currency: String) {
    init {
        require(Currencies.isSupported(currency)) { "Unsupported currency $currency" }
    }

    fun toBigDecimal(): BigDecimal = BigDecimal.valueOf(minor, Currencies.digitsOf(currency))

    operator fun plus(other: Money): Money {
        require(other.currency == currency) { "Currency mismatch" }
        return Money(Math.addExact(minor, other.minor), currency)
    }

    override fun toString(): String = "${toBigDecimal().toPlainString()} $currency"
}
