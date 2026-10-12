package com.recbook.hellopos.domain.catalog

import com.recbook.hellopos.domain.pricing.Discount
import com.recbook.hellopos.domain.pricing.LineInput
import com.recbook.hellopos.domain.pricing.SaleInput
import com.recbook.hellopos.domain.pricing.TaxProfile
import java.math.BigDecimal
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneId

/**
 * The till's catalog turned into pricing-engine input for a moment (feature #13, research R9). Mirrors the
 * server's `CatalogPricing`: dates are taken in the business's time zone; the tax version in force is the
 * latest that started on or before that date, of the product's own profile or else its category's default;
 * the item discount is the one running that day. `contracts/catalog-pricing-cases.json` proves both agree.
 */
class CatalogPriceFeed(
    val settings: CatalogSettings,
    taxProfiles: List<CatalogTaxProfile>,
    categories: List<CatalogCategory>,
    discounts: List<CatalogDiscount>,
) {
    private val profiles = taxProfiles.associateBy { it.id }
    private val categories = categories.associateBy { it.id }
    private val discounts = discounts.groupBy { it.productId }
    private val zone = ZoneId.of(settings.timeZone)

    /** The calendar date of [at] in the business's time zone. */
    fun localDate(at: Instant): LocalDate = at.atZone(zone).toLocalDate()

    /** The profile that taxes [product]: its override, or its category's default. */
    fun taxProfileOf(product: CatalogProduct): CatalogTaxProfile {
        val id = product.taxProfileId ?: requireNotNull(categories[product.categoryId]) { "unknown category of ${product.name}" }.defaultTaxProfileId
        return requireNotNull(profiles[id]) { "unknown tax profile of ${product.name}" }
    }

    /** [product]'s tax at [at], as the engine takes it (components of the version in force). */
    fun taxAt(product: CatalogProduct, at: Instant): TaxProfile {
        val profile = taxProfileOf(product)
        val date = localDate(at)
        val version = profile.versions.filter { !it.effectiveFrom.isAfter(date) }.maxByOrNull { it.effectiveFrom }
        return TaxProfile(profile.name, profile.kind, version?.components.orEmpty())
    }

    /** The item discount running on [at]'s date, if any. */
    fun discountAt(product: CatalogProduct, at: Instant): CatalogDiscount? {
        val date = localDate(at)
        return discounts[product.id].orEmpty().firstOrNull { !it.fromDate.isAfter(date) && (it.toDate == null || !it.toDate.isBefore(date)) }
    }

    /** A sale line for [product] at [at]; open-price products need [enteredPrice]. */
    fun line(product: CatalogProduct, lineId: String, at: Instant, quantity: BigDecimal, enteredPrice: Long? = null): LineInput {
        val price = if (product.openPrice) {
            requireNotNull(enteredPrice) { "${product.name} is sold at an entered price." }
        } else {
            enteredPrice ?: requireNotNull(product.unitPrice)
        }
        return LineInput(
            id = lineId,
            unitPrice = price,
            quantity = quantity,
            taxProfile = taxAt(product, at),
            originalUnitPrice = if (!product.openPrice && enteredPrice != null && enteredPrice != product.unitPrice) product.unitPrice else null,
            openPrice = product.openPrice,
            unit = WIRE_UNITS.getValue(product.unit),
            minQuantity = product.minQuantity,
            step = product.step,
            itemDiscount = discountAt(product, at)?.let { Discount(it.kind, it.value) },
            depositPerUnit = product.depositPerUnit,
        )
    }

    fun sale(lines: List<LineInput>): SaleInput = SaleInput(
        currency = settings.currency,
        pricing = settings.pricingMode,
        lines = lines,
        cashRoundingStep = settings.cashRoundingStep,
        depositsTaxable = settings.depositsTaxable,
    )

    companion object {
        /** Units as the engine and the API name them. */
        val WIRE_UNITS = mapOf(
            SaleUnit.Piece to "piece", SaleUnit.Gram to "g", SaleUnit.Kilogram to "kg", SaleUnit.Millilitre to "ml", SaleUnit.Litre to "l",
        )

        fun of(download: CatalogDownload) = CatalogPriceFeed(download.settings, download.taxProfiles, download.categories, download.discounts)
    }
}
