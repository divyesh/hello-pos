package com.recbook.hellopos.domain.pricing

import java.math.BigDecimal

// Input and output of the pricing engine (feature #10, data-model.md); the twin of the .NET kernel's
// PricingModel. Money is integer minor units; quantities and percentages are exact BigDecimals.

enum class PricingMode { TaxExclusive, TaxInclusive }

enum class TaxKind { Taxable, ZeroRated, Exempt }

enum class DiscountKind { Percent, Fixed }

enum class PaymentMethod { Cash, Card, Other }

data class TaxComponent(val label: String, val ratePercent: BigDecimal)

data class TaxProfile(val name: String, val kind: TaxKind, val components: List<TaxComponent>)

/** Percent (0–100) of an amount, or a fixed amount in minor units (per unit for item discounts). */
data class Discount(val kind: DiscountKind, val value: BigDecimal)

/** A fixed tip in minor units, or a percentage of the products' pre-tax subtotal. */
data class Tip(val amount: Long? = null, val percentOfSubtotal: BigDecimal? = null)

/** Card and other payments give [amount]; cash gives [tendered]. */
data class PaymentInput(val method: PaymentMethod, val amount: Long? = null, val tendered: Long? = null)

data class LineInput(
    val id: String,
    val unitPrice: Long,
    val quantity: BigDecimal,
    val taxProfile: TaxProfile,
    /** The price before a manager override; carried for the audit trail, not used in the arithmetic. */
    val originalUnitPrice: Long? = null,
    val openPrice: Boolean = false,
    val unit: String = "piece",
    val minQuantity: BigDecimal = BigDecimal.ONE,
    val step: BigDecimal = BigDecimal.ONE,
    val itemDiscount: Discount? = null,
    val depositPerUnit: Long = 0,
)

data class SaleInput(
    val currency: String,
    val pricing: PricingMode,
    val lines: List<LineInput>,
    /** Cash rounding step in minor units; 0 means none. */
    val cashRoundingStep: Long = 0,
    val depositsTaxable: Boolean = false,
    val orderDiscount: Discount? = null,
    val tip: Tip? = null,
    val payments: List<PaymentInput> = emptyList(),
)

data class PricedLine(
    val id: String,
    val lineAmount: Long,
    val itemDiscount: Long,
    val orderDiscountShare: Long,
    val taxable: Long,
    val deposit: Long,
)

/** One tax row; [profile] is set for tax-inclusive sales (rounded once per profile component). */
data class TaxRow(val label: String, val ratePercent: BigDecimal, val profile: String?, val base: Long, val amount: Long)

data class BaseByKind(val taxable: Long, val zeroRated: Long, val exempt: Long)

data class PaymentResult(val method: PaymentMethod, val applied: Long, val rounding: Long, val change: Long)

data class PricedSale(
    val lines: List<PricedLine>,
    val taxes: List<TaxRow>,
    val baseByKind: BaseByKind,
    val subtotal: Long,
    val taxTotal: Long,
    val tip: Long,
    val total: Long,
    val cashDue: Long?,
    val payments: List<PaymentResult>,
    val cashRounding: Long,
    val change: Long,
    val remaining: Long,
)

/** Why a sale cannot be priced; [code] is one of [PricingProblemCodes]. */
data class PricingProblem(val code: String, val lineId: String? = null, val field: String? = null)

object PricingProblemCodes {
    const val EMPTY_SALE = "emptySale"
    const val UNKNOWN_CURRENCY = "unknownCurrency"
    const val DUPLICATE_LINE_ID = "duplicateLineId"
    const val INVALID_PRICE = "invalidPrice"
    const val INVALID_QUANTITY = "invalidQuantity"
    const val BELOW_MINIMUM_QUANTITY = "belowMinimumQuantity"
    const val NOT_MULTIPLE_OF_STEP = "notMultipleOfStep"
    const val INVALID_DISCOUNT = "invalidDiscount"
    const val INVALID_TIP = "invalidTip"
    const val INVALID_PAYMENT = "invalidPayment"
    const val PAYMENT_EXCEEDS_BALANCE = "paymentExceedsBalance"
    const val PAYMENT_AFTER_SETTLED = "paymentAfterSettled"
    const val AMOUNT_OUT_OF_RANGE = "amountOutOfRange"
}

sealed interface PricingResult {
    data class Priced(val sale: PricedSale) : PricingResult

    data class Refused(val problems: List<PricingProblem>) : PricingResult
}
