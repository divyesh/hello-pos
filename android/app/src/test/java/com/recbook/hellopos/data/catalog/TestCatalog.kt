package com.recbook.hellopos.data.catalog

import com.recbook.hellopos.domain.catalog.CatalogCategory
import com.recbook.hellopos.domain.catalog.CatalogDiscount
import com.recbook.hellopos.domain.catalog.CatalogDownload
import com.recbook.hellopos.domain.catalog.CatalogProduct
import com.recbook.hellopos.domain.catalog.CatalogSettings
import com.recbook.hellopos.domain.catalog.CatalogTaxProfile
import com.recbook.hellopos.domain.catalog.CatalogTaxVersion
import com.recbook.hellopos.domain.catalog.SaleUnit
import com.recbook.hellopos.domain.pricing.DiscountKind
import com.recbook.hellopos.domain.pricing.PricingMode
import com.recbook.hellopos.domain.pricing.TaxComponent
import com.recbook.hellopos.domain.pricing.TaxKind
import java.math.BigDecimal
import java.time.Instant
import java.time.LocalDate
import java.util.UUID

/** Small catalogs for the store and sync tests. */
object TestCatalog {
    const val BUSINESS = "0199d1c4-8000-7000-8000-0000000000aa"

    val hst: UUID = UUID.fromString("00000000-0000-4000-8000-000000000101")
    val drinks: UUID = UUID.fromString("00000000-0000-4000-8000-000000000201")
    val snacks: UUID = UUID.fromString("00000000-0000-4000-8000-000000000202")
    val cola: UUID = UUID.fromString("00000000-0000-4000-8000-000000000301")
    val water: UUID = UUID.fromString("00000000-0000-4000-8000-000000000302")
    val oldSoda: UUID = UUID.fromString("00000000-0000-4000-8000-000000000303")

    val settings = CatalogSettings("CAD", PricingMode.TaxExclusive, "America/Toronto", 5, false)

    val hstProfile = CatalogTaxProfile(
        hst, "HST", TaxKind.Taxable, true,
        listOf(
            CatalogTaxVersion(UUID.fromString("00000000-0000-4000-8000-000000000111"), LocalDate.of(2026, 1, 1), listOf(TaxComponent("HST", BigDecimal("13")))),
            CatalogTaxVersion(UUID.fromString("00000000-0000-4000-8000-000000000112"), LocalDate.of(2026, 11, 1), listOf(TaxComponent("HST", BigDecimal("15")))),
        ),
    )

    fun product(id: UUID, name: String, category: UUID = drinks, price: Long? = 199, active: Boolean = true, vararg barcodes: String) =
        CatalogProduct(
            id = id, name = name, sku = null, categoryId = category, taxProfileId = null, unit = SaleUnit.Piece,
            unitPrice = price, openPrice = price == null, minQuantity = BigDecimal.ONE, step = BigDecimal.ONE,
            minAge = null, depositPerUnit = 0, active = active, barcodes = barcodes.toList(),
        )

    fun full(
        revision: Long = 100,
        products: List<CatalogProduct> = listOf(
            product(cola, "Cola", barcodes = arrayOf("0067000004568")),
            product(water, "Water", price = 99, barcodes = arrayOf("4011")),
            product(oldSoda, "Old soda", active = false),
        ),
        categories: List<CatalogCategory> = listOf(
            CatalogCategory(snacks, "Snacks", 2, hst, true),
            CatalogCategory(drinks, "Drinks", 1, hst, true),
        ),
        discounts: List<CatalogDiscount> = listOf(
            CatalogDiscount(UUID.fromString("00000000-0000-4000-8000-000000000401"), cola, DiscountKind.Percent, BigDecimal("10"), LocalDate.of(2026, 10, 15), LocalDate.of(2026, 10, 20)),
        ),
    ) = CatalogDownload(
        full = true,
        revision = revision,
        serverTime = Instant.parse("2026-10-12T16:00:00Z"),
        settings = settings,
        taxProfiles = listOf(hstProfile),
        categories = categories,
        products = products,
        discounts = discounts,
        removedDiscountIds = emptyList(),
    )
}
