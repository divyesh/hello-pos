package com.recbook.hellopos.data.catalog

import com.recbook.hellopos.data.remote.generated.models.CatalogSettings as ApiSettings
import com.recbook.hellopos.data.remote.generated.models.CatalogSync
import com.recbook.hellopos.data.remote.generated.models.PaymentKind as ApiPaymentKind
import com.recbook.hellopos.data.remote.generated.models.SyncDiscount
import com.recbook.hellopos.data.remote.generated.models.SyncPaymentType
import com.recbook.hellopos.data.remote.generated.models.SyncProduct
import com.recbook.hellopos.data.remote.generated.models.SyncSelling
import com.recbook.hellopos.data.remote.generated.models.SyncTaxProfile
import com.recbook.hellopos.domain.catalog.CatalogCategory
import com.recbook.hellopos.domain.catalog.CatalogDiscount
import com.recbook.hellopos.domain.catalog.CatalogDownload
import com.recbook.hellopos.domain.catalog.CatalogProduct
import com.recbook.hellopos.domain.catalog.CatalogSettings
import com.recbook.hellopos.domain.catalog.CatalogStatus
import com.recbook.hellopos.domain.catalog.CatalogTaxProfile
import com.recbook.hellopos.domain.catalog.CatalogTaxVersion
import com.recbook.hellopos.domain.catalog.DiningTableInfo
import com.recbook.hellopos.domain.catalog.PaymentKind
import com.recbook.hellopos.domain.catalog.PaymentTypeInfo
import com.recbook.hellopos.domain.catalog.SaleUnit
import com.recbook.hellopos.domain.catalog.SearchText
import com.recbook.hellopos.domain.catalog.SellingRules
import com.recbook.hellopos.domain.catalog.TableAreaInfo
import com.recbook.hellopos.domain.pricing.DiscountKind
import com.recbook.hellopos.domain.pricing.PricingMode
import com.recbook.hellopos.domain.pricing.TaxComponent
import com.recbook.hellopos.domain.pricing.TaxKind
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import java.math.BigDecimal
import java.time.Instant
import java.time.LocalDate
import java.util.UUID

/** Wire (generated client) → domain → Room rows, and back (feature #13). */
internal object CatalogMapping {

    fun toDownload(sync: CatalogSync): CatalogDownload = CatalogDownload(
        full = sync.mode == CatalogSync.Mode.full,
        revision = sync.revision,
        serverTime = Instant.parse(sync.serverTime),
        settings = settings(sync.business),
        taxProfiles = sync.taxProfiles.map(::taxProfile),
        categories = sync.categories.map {
            CatalogCategory(it.id, it.name, it.sortOrder, it.defaultTaxProfileId, it.active)
        },
        products = sync.products.map(::product),
        discounts = sync.discounts.map(::discount),
        removedDiscountIds = sync.removedDiscountIds,
        paymentTypes = sync.paymentTypes.map(::paymentType),
        tableAreas = sync.tableAreas.map { TableAreaInfo(it.id, it.name, it.position, it.active) },
        tables = sync.tables.map { DiningTableInfo(it.id, it.areaId, it.name, it.seats, it.position, it.active) },
        selling = selling(sync.selling),
    )

    private fun paymentType(api: SyncPaymentType) = PaymentTypeInfo(
        id = api.id,
        name = api.name,
        kind = when (api.kind) {
            ApiPaymentKind.cash -> PaymentKind.Cash
            ApiPaymentKind.externalCard -> PaymentKind.ExternalCard
            ApiPaymentKind.digitalWallet -> PaymentKind.DigitalWallet
            ApiPaymentKind.cheque -> PaymentKind.Cheque
            ApiPaymentKind.voucher -> PaymentKind.Voucher
            ApiPaymentKind.other -> PaymentKind.Other
        },
        builtIn = api.builtIn,
        opensDrawer = api.opensDrawer,
        asksReference = api.asksReference,
        givesChange = api.givesChange,
        position = api.position,
        active = api.active,
    )

    /** Tips off means no presets, whatever the server sent. */
    private fun selling(api: SyncSelling) = SellingRules(
        usesTables = api.usesTables,
        discountLimitPercent = BigDecimal(api.discountLimitPercent),
        discountLimitAmount = api.discountLimitAmount,
        tipsEnabled = api.tipsEnabled,
        tipPresets = if (api.tipsEnabled) api.tipPresets.map(::BigDecimal).sorted() else emptyList(),
    )

    private fun settings(api: ApiSettings) = CatalogSettings(
        currency = api.currency,
        pricingMode = when (api.pricingMode) {
            ApiSettings.PricingMode.tax_inclusive -> PricingMode.TaxInclusive
            ApiSettings.PricingMode.tax_exclusive -> PricingMode.TaxExclusive
        },
        timeZone = api.timeZone,
        cashRoundingStep = api.cashRoundingStep,
        depositsTaxable = api.depositsTaxable,
    )

    private fun taxProfile(api: SyncTaxProfile) = CatalogTaxProfile(
        id = api.id,
        name = api.name,
        kind = when (api.kind) {
            SyncTaxProfile.Kind.taxable -> TaxKind.Taxable
            SyncTaxProfile.Kind.zeroRated -> TaxKind.ZeroRated
            SyncTaxProfile.Kind.exempt -> TaxKind.Exempt
        },
        active = api.active,
        versions = api.versions.map { version ->
            CatalogTaxVersion(
                version.id,
                LocalDate.parse(version.effectiveFrom),
                version.components.map { TaxComponent(it.label, BigDecimal(it.ratePercent)) },
            )
        },
    )

    private fun product(api: SyncProduct) = CatalogProduct(
        id = api.id,
        name = api.name,
        sku = api.sku,
        categoryId = api.categoryId,
        taxProfileId = api.taxProfileId,
        unit = when (api.unit) {
            SyncProduct.Unit.piece -> SaleUnit.Piece
            SyncProduct.Unit.g -> SaleUnit.Gram
            SyncProduct.Unit.kg -> SaleUnit.Kilogram
            SyncProduct.Unit.ml -> SaleUnit.Millilitre
            SyncProduct.Unit.l -> SaleUnit.Litre
        },
        unitPrice = api.unitPrice,
        openPrice = api.openPrice,
        minQuantity = BigDecimal(api.minQuantity),
        step = BigDecimal(api.step),
        minAge = api.minAge,
        depositPerUnit = api.depositPerUnit,
        active = api.active,
        barcodes = api.barcodes,
    )

    private fun discount(api: SyncDiscount) = when (api.kind) {
        SyncDiscount.Kind.percent -> CatalogDiscount(
            api.id, api.productId, DiscountKind.Percent, BigDecimal(requireNotNull(api.percent) { "percent discount without a percent" }),
            LocalDate.parse(api.fromDate), api.toDate?.let(LocalDate::parse),
        )
        SyncDiscount.Kind.fixed -> CatalogDiscount(
            api.id, api.productId, DiscountKind.Fixed, BigDecimal.valueOf(requireNotNull(api.amountOff) { "fixed discount without an amount" }),
            LocalDate.parse(api.fromDate), api.toDate?.let(LocalDate::parse),
        )
    }

    // ---- Room rows ----------------------------------------------------------------------------------

    @Serializable
    private data class ComponentJson(val label: String, val ratePercent: String)

    private val json = Json { ignoreUnknownKeys = true }

    fun meta(businessId: String, download: CatalogDownload, refreshedAt: Instant) = CatalogMetaEntity(
        businessId = businessId,
        revision = download.revision,
        refreshedAtMillis = refreshedAt.toEpochMilli(),
        serverOffsetMillis = download.serverTime.toEpochMilli() - refreshedAt.toEpochMilli(),
        currency = download.settings.currency,
        pricingMode = download.settings.pricingMode.name,
        timeZone = download.settings.timeZone,
        cashRoundingStep = download.settings.cashRoundingStep,
        depositsTaxable = download.settings.depositsTaxable,
        usesTables = download.selling.usesTables,
        discountLimitPercent = download.selling.discountLimitPercent.toPlainString(),
        discountLimitAmount = download.selling.discountLimitAmount,
        tipsEnabled = download.selling.tipsEnabled,
        tipPresets = download.selling.tipPresets.joinToString(",") { it.toPlainString() },
    )

    fun status(meta: CatalogMetaEntity) =
        CatalogStatus(meta.revision, Instant.ofEpochMilli(meta.refreshedAtMillis), meta.serverOffsetMillis)

    fun settings(meta: CatalogMetaEntity) = CatalogSettings(
        meta.currency, PricingMode.valueOf(meta.pricingMode), meta.timeZone, meta.cashRoundingStep, meta.depositsTaxable,
    )

    fun sellingRules(meta: CatalogMetaEntity) = SellingRules(
        usesTables = meta.usesTables,
        discountLimitPercent = BigDecimal(meta.discountLimitPercent),
        discountLimitAmount = meta.discountLimitAmount,
        tipsEnabled = meta.tipsEnabled,
        tipPresets = if (meta.tipPresets.isEmpty()) emptyList() else meta.tipPresets.split(',').map(::BigDecimal),
    )

    fun row(type: PaymentTypeInfo) = PaymentTypeEntity(
        type.id.toString(), type.name, type.kind.name, type.builtIn, type.opensDrawer, type.asksReference, type.givesChange, type.position, type.active,
    )

    fun paymentType(row: PaymentTypeEntity) = PaymentTypeInfo(
        UUID.fromString(row.id), row.name, PaymentKind.valueOf(row.kind), row.builtIn, row.opensDrawer, row.asksReference, row.givesChange,
        row.position, row.active,
    )

    fun row(area: TableAreaInfo) = TableAreaEntity(area.id.toString(), area.name, area.position, area.active)

    fun tableArea(row: TableAreaEntity) = TableAreaInfo(UUID.fromString(row.id), row.name, row.position, row.active)

    fun row(table: DiningTableInfo) =
        DiningTableEntity(table.id.toString(), table.areaId.toString(), table.name, table.seats, table.position, table.active)

    fun diningTable(row: DiningTableEntity) =
        DiningTableInfo(UUID.fromString(row.id), UUID.fromString(row.areaId), row.name, row.seats, row.position, row.active)

    fun row(profile: CatalogTaxProfile) =
        TaxProfileEntity(profile.id.toString(), profile.name, profile.kind.name, profile.active)

    fun rows(profile: CatalogTaxProfile) = profile.versions.map { version ->
        TaxVersionEntity(
            version.id.toString(),
            profile.id.toString(),
            version.effectiveFrom.toString(),
            json.encodeToString(version.components.map { ComponentJson(it.label, it.ratePercent.toPlainString()) }),
        )
    }

    fun taxProfile(row: TaxProfileEntity, versions: List<TaxVersionEntity>) = CatalogTaxProfile(
        id = UUID.fromString(row.id),
        name = row.name,
        kind = TaxKind.valueOf(row.kind),
        active = row.active,
        versions = versions.map { version ->
            CatalogTaxVersion(
                UUID.fromString(version.id),
                LocalDate.parse(version.effectiveFrom),
                json.decodeFromString<List<ComponentJson>>(version.componentsJson).map { TaxComponent(it.label, BigDecimal(it.ratePercent)) },
            )
        },
    )

    fun row(category: CatalogCategory) = CategoryEntity(
        category.id.toString(), category.name, category.sortOrder, category.defaultTaxProfileId.toString(), category.active,
    )

    fun category(row: CategoryEntity) = CatalogCategory(
        UUID.fromString(row.id), row.name, row.sortOrder, UUID.fromString(row.defaultTaxProfileId), row.active,
    )

    fun row(product: CatalogProduct) = ProductEntity(
        id = product.id.toString(),
        name = product.name,
        searchName = SearchText.normalize(product.name),
        sku = product.sku,
        categoryId = product.categoryId.toString(),
        taxProfileId = product.taxProfileId?.toString(),
        unit = product.unit.name,
        unitPrice = product.unitPrice,
        openPrice = product.openPrice,
        minQuantity = product.minQuantity.toPlainString(),
        step = product.step.toPlainString(),
        minAge = product.minAge,
        depositPerUnit = product.depositPerUnit,
        active = product.active,
    )

    fun barcodeRows(product: CatalogProduct) = product.barcodes.map { BarcodeEntity(it, product.id.toString()) }

    fun product(row: ProductEntity, barcodes: List<String>) = CatalogProduct(
        id = UUID.fromString(row.id),
        name = row.name,
        sku = row.sku,
        categoryId = UUID.fromString(row.categoryId),
        taxProfileId = row.taxProfileId?.let(UUID::fromString),
        unit = SaleUnit.valueOf(row.unit),
        unitPrice = row.unitPrice,
        openPrice = row.openPrice,
        minQuantity = BigDecimal(row.minQuantity),
        step = BigDecimal(row.step),
        minAge = row.minAge,
        depositPerUnit = row.depositPerUnit,
        active = row.active,
        barcodes = barcodes,
    )

    fun row(discount: CatalogDiscount) = DiscountEntity(
        discount.id.toString(), discount.productId.toString(), discount.kind.name, discount.value.toPlainString(),
        discount.fromDate.toString(), discount.toDate?.toString(),
    )

    fun discount(row: DiscountEntity) = CatalogDiscount(
        UUID.fromString(row.id), UUID.fromString(row.productId), DiscountKind.valueOf(row.kind), BigDecimal(row.value),
        LocalDate.parse(row.fromDate), row.toDate?.let(LocalDate::parse),
    )
}
