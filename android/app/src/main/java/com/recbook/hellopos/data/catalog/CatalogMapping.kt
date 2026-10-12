package com.recbook.hellopos.data.catalog

import com.recbook.hellopos.data.remote.generated.models.CatalogSettings as ApiSettings
import com.recbook.hellopos.data.remote.generated.models.CatalogSync
import com.recbook.hellopos.data.remote.generated.models.SyncDiscount
import com.recbook.hellopos.data.remote.generated.models.SyncProduct
import com.recbook.hellopos.data.remote.generated.models.SyncTaxProfile
import com.recbook.hellopos.domain.catalog.CatalogCategory
import com.recbook.hellopos.domain.catalog.CatalogDiscount
import com.recbook.hellopos.domain.catalog.CatalogDownload
import com.recbook.hellopos.domain.catalog.CatalogProduct
import com.recbook.hellopos.domain.catalog.CatalogSettings
import com.recbook.hellopos.domain.catalog.CatalogStatus
import com.recbook.hellopos.domain.catalog.CatalogTaxProfile
import com.recbook.hellopos.domain.catalog.CatalogTaxVersion
import com.recbook.hellopos.domain.catalog.SaleUnit
import com.recbook.hellopos.domain.catalog.SearchText
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
    )

    fun status(meta: CatalogMetaEntity) =
        CatalogStatus(meta.revision, Instant.ofEpochMilli(meta.refreshedAtMillis), meta.serverOffsetMillis)

    fun settings(meta: CatalogMetaEntity) = CatalogSettings(
        meta.currency, PricingMode.valueOf(meta.pricingMode), meta.timeZone, meta.cashRoundingStep, meta.depositsTaxable,
    )

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
