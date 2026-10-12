package com.recbook.hellopos.domain.pricing

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertInstanceOf
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import java.math.BigDecimal
import java.util.Locale

/** Engine properties the shared cases cannot express: purity, locale independence, speed (SC-004). */
class PricingEngineTest {

    private val hst = TaxProfile("HST", TaxKind.Taxable, listOf(TaxComponent("HST", BigDecimal(13))))

    @Test
    fun `same input gives equal results`() {
        val input = bigSale(50)

        assertEquals(PricingEngine.price(input), PricingEngine.price(input))
    }

    @Test
    fun `results do not depend on the default locale`() {
        val input = bigSale(20)
        val previous = Locale.getDefault()
        val reference = PricingEngine.price(input)
        try {
            for (tag in listOf("fr-CA", "de-DE", "hi-IN", "ar-KW")) {
                Locale.setDefault(Locale.forLanguageTag(tag))
                assertEquals(reference, PricingEngine.price(input), tag)
            }
        } finally {
            Locale.setDefault(previous)
        }
    }

    @Test
    fun `two hundred lines price quickly`() {
        val input = bigSale(200)
        PricingEngine.price(input) // warm up

        val started = System.nanoTime()
        val result = PricingEngine.price(input)
        val millis = (System.nanoTime() - started) / 1_000_000

        assertInstanceOf(PricingResult.Priced::class.java, result)
        assertTrue(millis < 50, "200 lines took $millis ms")
    }

    @Test
    fun `cash just short of the rounded due is refused, not taken`() {
        val exempt = TaxProfile("Exempt", TaxKind.Exempt, emptyList())
        val input = SaleInput(
            "CAD", PricingMode.TaxExclusive, listOf(LineInput("a", 1043, BigDecimal.ONE, exempt)),
            cashRoundingStep = 5, payments = listOf(PaymentInput(PaymentMethod.Cash, tendered = 1044)),
        )

        val refused = assertInstanceOf(PricingResult.Refused::class.java, PricingEngine.price(input))

        assertEquals(PricingProblemCodes.INVALID_PAYMENT, refused.problems.single().code)
    }

    /** Weighed and counted lines with item discounts, deposits, an order discount, a tip and cash. */
    private fun bigSale(lines: Int) = SaleInput(
        currency = "CAD",
        pricing = PricingMode.TaxExclusive,
        lines = (1..lines).map { i ->
            val weighed = i % 3 == 0
            LineInput(
                id = "l$i",
                unitPrice = 99L + (i * 37 % 900),
                quantity = if (weighed) BigDecimal("1.255") else BigDecimal(i % 5 + 1),
                taxProfile = hst,
                step = if (weighed) BigDecimal("0.005") else BigDecimal.ONE,
                minQuantity = if (weighed) BigDecimal("0.005") else BigDecimal.ONE,
                itemDiscount = if (i % 4 == 0) Discount(DiscountKind.Percent, BigDecimal("12.5")) else null,
                depositPerUnit = if (i % 7 == 0) 10 else 0,
            )
        },
        cashRoundingStep = 5,
        orderDiscount = Discount(DiscountKind.Fixed, BigDecimal(333)),
        tip = Tip(percentOfSubtotal = BigDecimal(15)),
    )
}
