package com.recbook.hellopos.domain.pricing

import com.recbook.hellopos.domain.money.Currencies
import java.math.BigDecimal
import java.math.BigInteger

/**
 * Prices a sale exactly as `specs/pricing-rules.md` says (feature #10). Pure: no clock, I/O or locale.
 * The twin of the .NET kernel's `PricingEngine`; both are held to `contracts/pricing-cases.json`.
 */
object PricingEngine {
    const val MAX_QUANTITY_DECIMALS = 3

    fun price(input: SaleInput): PricingResult {
        val problems = validate(input)
        if (problems.isNotEmpty()) return PricingResult.Refused(problems)
        return try {
            compute(input)
        } catch (_: ArithmeticException) {
            PricingResult.Refused(listOf(PricingProblem(PricingProblemCodes.AMOUNT_OUT_OF_RANGE)))
        }
    }

    /** Refuses input that cannot be priced (FR-006), naming the line and field. */
    fun validate(input: SaleInput): List<PricingProblem> {
        val problems = mutableListOf<PricingProblem>()
        if (!Currencies.isSupported(input.currency)) {
            problems += PricingProblem(PricingProblemCodes.UNKNOWN_CURRENCY, field = "currency")
        }
        if (input.lines.isEmpty()) problems += PricingProblem(PricingProblemCodes.EMPTY_SALE, field = "lines")

        val ids = mutableSetOf<String>()
        for (line in input.lines) {
            if (!ids.add(line.id)) problems += PricingProblem(PricingProblemCodes.DUPLICATE_LINE_ID, line.id, "id")
            if (line.unitPrice < 0 || (line.originalUnitPrice ?: 0) < 0) {
                problems += PricingProblem(PricingProblemCodes.INVALID_PRICE, line.id, "unitPrice")
            }
            if (line.depositPerUnit < 0) problems += PricingProblem(PricingProblemCodes.INVALID_PRICE, line.id, "depositPerUnit")
            when {
                line.quantity.signum() <= 0 || decimals(line.quantity) > MAX_QUANTITY_DECIMALS ||
                    line.step.signum() <= 0 || line.minQuantity.signum() < 0 ->
                    problems += PricingProblem(PricingProblemCodes.INVALID_QUANTITY, line.id, "quantity")
                line.quantity < line.minQuantity ->
                    problems += PricingProblem(PricingProblemCodes.BELOW_MINIMUM_QUANTITY, line.id, "quantity")
                line.quantity.remainder(line.step).signum() != 0 ->
                    problems += PricingProblem(PricingProblemCodes.NOT_MULTIPLE_OF_STEP, line.id, "quantity")
            }
            if (line.itemDiscount != null && !isValid(line.itemDiscount)) {
                problems += PricingProblem(PricingProblemCodes.INVALID_DISCOUNT, line.id, "itemDiscount")
            }
            if (line.taxProfile.components.any { it.ratePercent.signum() < 0 }) {
                problems += PricingProblem(PricingProblemCodes.INVALID_PRICE, line.id, "taxProfile")
            }
        }

        if (input.orderDiscount != null && !isValid(input.orderDiscount)) {
            problems += PricingProblem(PricingProblemCodes.INVALID_DISCOUNT, field = "orderDiscount")
        }
        val tip = input.tip
        if (tip != null && ((tip.amount == null) == (tip.percentOfSubtotal == null) || (tip.amount ?: 0) < 0 ||
                (tip.percentOfSubtotal?.signum() ?: 0) < 0)
        ) {
            problems += PricingProblem(PricingProblemCodes.INVALID_TIP, field = "tip")
        }
        if (input.cashRoundingStep < 0) problems += PricingProblem(PricingProblemCodes.INVALID_PAYMENT, field = "cashRoundingStep")
        input.payments.forEachIndexed { i, payment ->
            val valid = if (payment.method == PaymentMethod.Cash) {
                (payment.tendered ?: 0) > 0 && payment.amount == null
            } else {
                (payment.amount ?: 0) > 0 && payment.tendered == null
            }
            if (!valid) problems += PricingProblem(PricingProblemCodes.INVALID_PAYMENT, field = "payments[$i]")
        }
        return problems
    }

    private fun compute(input: SaleInput): PricingResult {
        val zero = BigInteger.ZERO

        // Steps 1-2: line amount, item discount (never below zero), deposit.
        val quantities = input.lines.map { Exact.fraction(it.quantity) }
        val lineAmounts = input.lines.mapIndexed { i, line ->
            val (qn, qd) = quantities[i]
            Exact.roundHalfUp(BigInteger.valueOf(line.unitPrice) * qn, qd)
        }
        val itemDiscounts = input.lines.mapIndexed { i, line ->
            val (qn, qd) = quantities[i]
            val discount = line.itemDiscount ?: return@mapIndexed zero
            lineAmounts[i].min(
                if (discount.kind == DiscountKind.Percent) {
                    Exact.percent(lineAmounts[i], discount.value)
                } else {
                    Exact.roundHalfUp(discount.value.toBigIntegerExact() * qn, qd)
                },
            )
        }
        val nets = lineAmounts.indices.map { lineAmounts[it] - itemDiscounts[it] }
        val deposits = input.lines.mapIndexed { i, line ->
            val (qn, qd) = quantities[i]
            Exact.roundHalfUp(BigInteger.valueOf(line.depositPerUnit) * qn, qd)
        }

        // Step 3: order discount, spread by largest remainder (ties to the earlier line).
        val netTotal = sum(nets)
        val order = input.orderDiscount
        val orderDiscount = when {
            order == null -> zero
            order.kind == DiscountKind.Percent -> Exact.percent(netTotal, order.value)
            else -> order.value.toBigIntegerExact().min(netTotal)
        }
        val shares = Exact.spreadByLargestRemainder(orderDiscount, nets)

        // Step 4: taxable amount per line (products only) and the tax base (plus taxable deposits).
        val taxables = nets.indices.map { nets[it] - shares[it] }
        val taxBases = taxables.indices.map { taxables[it] + if (input.depositsTaxable) deposits[it] else zero }

        // Step 5: tax, rounded once per component (exclusive) or per profile component (inclusive).
        val inclusive = input.pricing == PricingMode.TaxInclusive
        val taxes = if (inclusive) inclusiveTaxes(input, taxBases) else exclusiveTaxes(input, taxBases)
        val taxTotal = sum(taxes.map { BigInteger.valueOf(it.amount) })

        // Steps 6-7: tip (the products' pre-tax subtotal, deposits excluded) and total.
        val productSubtotal = sum(taxables)
        val subtotal = productSubtotal + sum(deposits)
        val tip = when {
            input.tip?.amount != null -> BigInteger.valueOf(input.tip.amount)
            input.tip?.percentOfSubtotal != null ->
                Exact.percent(if (inclusive) preTaxProducts(input, taxables) else productSubtotal, input.tip.percentOfSubtotal)
            else -> zero
        }
        val total = subtotal + (if (inclusive) zero else taxTotal) + tip

        // Steps 8-9: payments in order; cash rounding only on the cash payment that settles the balance.
        val step = BigInteger.valueOf(input.cashRoundingStep)
        val payments = mutableListOf<PaymentResult>()
        var remaining = total
        var cashRounding = zero
        var change = zero
        for ((p, payment) in input.payments.withIndex()) {
            val field = "payments[$p]"
            if (remaining.signum() == 0) {
                return PricingResult.Refused(listOf(PricingProblem(PricingProblemCodes.PAYMENT_AFTER_SETTLED, field = field)))
            }
            if (payment.method != PaymentMethod.Cash) {
                val amount = BigInteger.valueOf(payment.amount!!)
                if (amount > remaining) {
                    return PricingResult.Refused(listOf(PricingProblem(PricingProblemCodes.PAYMENT_EXCEEDS_BALANCE, field = field)))
                }
                remaining -= amount
                payments += PaymentResult(payment.method, payment.amount, 0, 0)
                continue
            }
            val tendered = BigInteger.valueOf(payment.tendered!!)
            val due = cashRound(remaining, step)
            when {
                tendered >= due -> {
                    payments += PaymentResult(
                        PaymentMethod.Cash, due.toLongExact(), (due - remaining).toLongExact(), (tendered - due).toLongExact(),
                    )
                    cashRounding += due - remaining
                    change += tendered - due
                    remaining = zero
                }
                // Covers the exact balance but not the rounded cash due: neither settles nor fits as a part payment.
                tendered >= remaining ->
                    return PricingResult.Refused(listOf(PricingProblem(PricingProblemCodes.INVALID_PAYMENT, field = field)))
                else -> {
                    remaining -= tendered
                    payments += PaymentResult(PaymentMethod.Cash, tendered.toLongExact(), 0, 0)
                }
            }
        }

        fun sumOf(kind: TaxKind) = sum(input.lines.indices.map { if (input.lines[it].taxProfile.kind == kind) taxBases[it] else zero })
        return PricingResult.Priced(
            PricedSale(
                lines = input.lines.mapIndexed { i, line ->
                    PricedLine(
                        line.id, lineAmounts[i].toLongExact(), itemDiscounts[i].toLongExact(),
                        shares[i].toLongExact(), taxables[i].toLongExact(), deposits[i].toLongExact(),
                    )
                },
                taxes = taxes,
                baseByKind = BaseByKind(
                    sumOf(TaxKind.Taxable).toLongExact(),
                    sumOf(TaxKind.ZeroRated).toLongExact(),
                    sumOf(TaxKind.Exempt).toLongExact(),
                ),
                subtotal = subtotal.toLongExact(),
                taxTotal = taxTotal.toLongExact(),
                tip = tip.toLongExact(),
                total = total.toLongExact(),
                cashDue = if (step.signum() == 0) null else cashRound(total, step).toLongExact(),
                payments = payments,
                cashRounding = cashRounding.toLongExact(),
                change = change.toLongExact(),
                remaining = remaining.toLongExact(),
            ),
        )
    }

    /** Tax-exclusive: group by component (label, rate) across profiles, round once per component. */
    private fun exclusiveTaxes(input: SaleInput, taxBases: List<BigInteger>): List<TaxRow> {
        val rows = mutableListOf<Triple<String, BigDecimal, BigInteger>>()
        input.lines.forEachIndexed { i, line ->
            if (line.taxProfile.kind != TaxKind.Taxable) return@forEachIndexed
            for (component in line.taxProfile.components) {
                val at = rows.indexOfFirst { it.first == component.label && it.second.compareTo(component.ratePercent) == 0 }
                if (at < 0) {
                    rows += Triple(component.label, component.ratePercent, taxBases[i])
                } else {
                    rows[at] = rows[at].copy(third = rows[at].third + taxBases[i])
                }
            }
        }
        return rows.map { (label, rate, base) -> TaxRow(label, rate, null, base.toLongExact(), Exact.percent(base, rate).toLongExact()) }
    }

    /**
     * Tax-inclusive: group lines by profile; each component tax = G x rate / (100 + R), rounded once per
     * profile component (G the profile gross, R its total rate).
     */
    private fun inclusiveTaxes(input: SaleInput, taxBases: List<BigInteger>): List<TaxRow> =
        groupByProfile(input, taxBases).filter { it.first.kind == TaxKind.Taxable }.flatMap { (profile, gross) ->
            val totalRate = profile.components.fold(BigDecimal.ZERO) { acc, c -> acc + c.ratePercent }
            profile.components.map {
                TaxRow(
                    it.label, it.ratePercent, profile.name, gross.toLongExact(),
                    Exact.scale(gross, it.ratePercent, HUNDRED + totalRate).toLongExact(),
                )
            }
        }

    /** Tax-inclusive tip base: per profile, product gross x 100 / (100 + R), rounded once per profile. */
    private fun preTaxProducts(input: SaleInput, taxables: List<BigInteger>): BigInteger =
        sum(groupByProfile(input, taxables).map { (profile, amount) ->
            if (profile.kind == TaxKind.Taxable) {
                Exact.scale(amount, HUNDRED, HUNDRED + profile.components.fold(BigDecimal.ZERO) { acc, c -> acc + c.ratePercent })
            } else {
                amount
            }
        })

    /** Sums per tax profile (by name), in order of first appearance. */
    private fun groupByProfile(input: SaleInput, amounts: List<BigInteger>): List<Pair<TaxProfile, BigInteger>> {
        val groups = mutableListOf<Pair<TaxProfile, BigInteger>>()
        input.lines.forEachIndexed { i, line ->
            val at = groups.indexOfFirst { it.first.name == line.taxProfile.name }
            if (at < 0) groups += line.taxProfile to amounts[i] else groups[at] = groups[at].copy(second = groups[at].second + amounts[i])
        }
        return groups
    }

    /** Nearest multiple of [step], ties up; no step means unchanged. */
    private fun cashRound(amount: BigInteger, step: BigInteger): BigInteger =
        if (step.signum() == 0) amount else (TWO * amount + step) / (TWO * step) * step

    private val HUNDRED = BigDecimal(100)
    private val TWO = BigInteger.valueOf(2)

    private fun isValid(discount: Discount): Boolean = when (discount.kind) {
        DiscountKind.Percent -> discount.value.signum() >= 0 && discount.value <= BigDecimal(100)
        DiscountKind.Fixed -> discount.value.signum() >= 0 && decimals(discount.value) == 0
    }

    /** Significant decimal places of [value] (1.250 has 2). */
    internal fun decimals(value: BigDecimal): Int = value.stripTrailingZeros().scale().coerceAtLeast(0)

    internal fun sum(values: Iterable<BigInteger>): BigInteger = values.fold(BigInteger.ZERO, BigInteger::add)
}
