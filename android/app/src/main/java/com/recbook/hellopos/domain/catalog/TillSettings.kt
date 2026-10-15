package com.recbook.hellopos.domain.catalog

import java.math.BigDecimal
import java.util.UUID

// Business settings the till keeps with its catalog (feature #12, data-model.md): how customers pay, the
// tables of a restaurant-style business, and the selling rules. Money is minor units of the business currency.

enum class PaymentKind { Cash, ExternalCard, DigitalWallet, Cheque, Voucher, Other }

data class PaymentTypeInfo(
    val id: UUID,
    val name: String,
    val kind: PaymentKind,
    /** The business's Cash: always there, never inactive. */
    val builtIn: Boolean,
    val opensDrawer: Boolean,
    /** The till asks for a reference (UPI transaction id, cheque number); optional at checkout. */
    val asksReference: Boolean,
    /** Only cash gives change. */
    val givesChange: Boolean,
    val position: Int,
    val active: Boolean,
)

data class TableAreaInfo(val id: UUID, val name: String, val position: Int, val active: Boolean)

data class DiningTableInfo(
    val id: UUID,
    val areaId: UUID,
    val name: String,
    val seats: Int,
    /** Position within the area. */
    val position: Int,
    val active: Boolean,
)

/** The tables switch, the discount approval limit and tips. */
data class SellingRules(
    /** When false the till offers no tables, whatever the copy holds. */
    val usesTables: Boolean,
    /** Cashier discounts on one sale above this percentage need a manager's or owner's approval. */
    val discountLimitPercent: BigDecimal,
    /** Optional second limit in minor units. */
    val discountLimitAmount: Long?,
    val tipsEnabled: Boolean,
    /** Ascending; empty when tips are off. */
    val tipPresets: List<BigDecimal>,
)
