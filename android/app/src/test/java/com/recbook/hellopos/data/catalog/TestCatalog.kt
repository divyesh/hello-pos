package com.recbook.hellopos.data.catalog

import com.recbook.hellopos.domain.catalog.CatalogCategory
import com.recbook.hellopos.domain.catalog.CatalogDiscount
import com.recbook.hellopos.domain.catalog.CatalogDownload
import com.recbook.hellopos.domain.catalog.CatalogProduct
import com.recbook.hellopos.domain.catalog.CatalogSettings
import com.recbook.hellopos.domain.catalog.CatalogTaxProfile
import com.recbook.hellopos.domain.catalog.CatalogTaxVersion
import com.recbook.hellopos.domain.catalog.DiningTableInfo
import com.recbook.hellopos.domain.catalog.PaymentKind
import com.recbook.hellopos.domain.catalog.PaymentTypeInfo
import com.recbook.hellopos.domain.catalog.SaleUnit
import com.recbook.hellopos.domain.catalog.SellingRules
import com.recbook.hellopos.domain.catalog.TableAreaInfo
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

    val cash: UUID = UUID.fromString("00000000-0000-4000-8000-000000000901")
    val card: UUID = UUID.fromString("00000000-0000-4000-8000-000000000902")
    val cheque: UUID = UUID.fromString("00000000-0000-4000-8000-000000000903")
    val hall: UUID = UUID.fromString("00000000-0000-4000-8000-000000000951")
    val patio: UUID = UUID.fromString("00000000-0000-4000-8000-000000000952")

    fun paymentType(id: UUID, name: String, kind: PaymentKind, position: Int, active: Boolean = true) = PaymentTypeInfo(
        id = id, name = name, kind = kind, builtIn = kind == PaymentKind.Cash, opensDrawer = kind == PaymentKind.Cash,
        asksReference = kind == PaymentKind.Cheque, givesChange = kind == PaymentKind.Cash, position = position, active = active,
    )

    fun table(index: Int, area: UUID = hall, name: String = index.toString(), seats: Int = 4, active: Boolean = true) =
        DiningTableInfo(UUID.fromString("00000000-0000-4000-8000-%012d".format(960 + index)), area, name, seats, index - 1, active)

    val paymentTypes = listOf(
        paymentType(card, "Card (terminal)", PaymentKind.ExternalCard, 1),
        paymentType(cash, "Cash", PaymentKind.Cash, 0),
        paymentType(cheque, "Cheque", PaymentKind.Cheque, 2, active = false),
    )

    val selling = SellingRules(
        usesTables = true, discountLimitPercent = BigDecimal("10"), discountLimitAmount = 2000, tipsEnabled = true,
        tipPresets = listOf(BigDecimal("10"), BigDecimal("15"), BigDecimal("20")),
    )

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
        paymentTypes: List<PaymentTypeInfo> = TestCatalog.paymentTypes,
        tableAreas: List<TableAreaInfo> = listOf(TableAreaInfo(hall, "Hall", 0, true), TableAreaInfo(patio, "Patio", 1, false)),
        tables: List<DiningTableInfo> = listOf(table(1), table(2, seats = 2)),
        selling: SellingRules = TestCatalog.selling,
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
        paymentTypes = paymentTypes,
        tableAreas = tableAreas,
        tables = tables,
        selling = selling,
    )
}
