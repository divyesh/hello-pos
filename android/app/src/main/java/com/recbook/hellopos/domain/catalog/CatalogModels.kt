package com.recbook.hellopos.domain.catalog

import com.recbook.hellopos.domain.pricing.DiscountKind
import com.recbook.hellopos.domain.pricing.PricingMode
import com.recbook.hellopos.domain.pricing.TaxComponent
import com.recbook.hellopos.domain.pricing.TaxKind
import java.math.BigDecimal
import java.text.Normalizer
import java.time.Instant
import java.time.LocalDate
import java.util.Locale
import java.util.UUID

// The catalog as the till keeps it (feature #13, data-model.md). Money is minor units of the business
// currency; quantities and rates are exact decimals (Constitution I).

/** The business settings prices depend on. */
data class CatalogSettings(
    val currency: String,
    val pricingMode: PricingMode,
    val timeZone: String,
    val cashRoundingStep: Long,
    val depositsTaxable: Boolean,
)

data class CatalogTaxVersion(val id: UUID, val effectiveFrom: LocalDate, val components: List<TaxComponent>)

data class CatalogTaxProfile(
    val id: UUID,
    val name: String,
    val kind: TaxKind,
    val active: Boolean,
    val versions: List<CatalogTaxVersion>,
)

data class CatalogCategory(
    val id: UUID,
    val name: String,
    val sortOrder: Int,
    val defaultTaxProfileId: UUID,
    val active: Boolean,
)

enum class SaleUnit { Piece, Gram, Kilogram, Millilitre, Litre }

data class CatalogProduct(
    val id: UUID,
    val name: String,
    val sku: String?,
    val categoryId: UUID,
    /** Present only when the product overrides its category's tax profile. */
    val taxProfileId: UUID?,
    val unit: SaleUnit,
    /** Minor units; null for open-price products. */
    val unitPrice: Long?,
    val openPrice: Boolean,
    val minQuantity: BigDecimal,
    val step: BigDecimal,
    val minAge: Int?,
    val depositPerUnit: Long,
    val active: Boolean,
    /** Normalized codes (UPC-A in its 13-digit EAN form). */
    val barcodes: List<String>,
)

data class CatalogDiscount(
    val id: UUID,
    val productId: UUID,
    val kind: DiscountKind,
    /** Percent for [DiscountKind.Percent], minor units off each unit for [DiscountKind.Fixed]. */
    val value: BigDecimal,
    val fromDate: LocalDate,
    /** Inclusive; null is open-ended. */
    val toDate: LocalDate?,
)

/** One download: everything (full) or what changed after the till's revision (changes). */
data class CatalogDownload(
    val full: Boolean,
    val revision: Long,
    val serverTime: Instant,
    val settings: CatalogSettings,
    val taxProfiles: List<CatalogTaxProfile>,
    val categories: List<CatalogCategory>,
    val products: List<CatalogProduct>,
    val discounts: List<CatalogDiscount>,
    val removedDiscountIds: List<UUID>,
)

/** Where the till's copy stands. */
data class CatalogStatus(
    val revision: Long,
    val refreshedAt: Instant,
    /** Server time minus device time at the last refresh. */
    val serverOffsetMillis: Long,
)

/** Search text: lower case, accents removed, so "creme" finds "Crème brûlée". */
object SearchText {
    private val marks = Regex("\\p{M}+")

    fun normalize(text: String): String =
        marks.replace(Normalizer.normalize(text, Normalizer.Form.NFD), "").lowercase(Locale.ROOT).trim()
}
