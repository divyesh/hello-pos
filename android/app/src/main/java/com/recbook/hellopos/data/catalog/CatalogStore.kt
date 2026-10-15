package com.recbook.hellopos.data.catalog

import androidx.room.withTransaction
import com.recbook.hellopos.domain.catalog.BarcodeParse
import com.recbook.hellopos.domain.catalog.BarcodeRules
import com.recbook.hellopos.domain.catalog.CatalogCategory
import com.recbook.hellopos.domain.catalog.CatalogDownload
import com.recbook.hellopos.domain.catalog.CatalogPriceFeed
import com.recbook.hellopos.domain.catalog.CatalogProduct
import com.recbook.hellopos.domain.catalog.CatalogSettings
import com.recbook.hellopos.domain.catalog.CatalogStatus
import com.recbook.hellopos.domain.catalog.DiningTableInfo
import com.recbook.hellopos.domain.catalog.PaymentTypeInfo
import com.recbook.hellopos.domain.catalog.ProductLookup
import com.recbook.hellopos.domain.catalog.SearchText
import com.recbook.hellopos.domain.catalog.SellingRules
import com.recbook.hellopos.domain.catalog.TableAreaInfo
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.mapLatest
import java.time.Clock
import java.util.UUID

/**
 * The catalog copy in Room (research R6). A download is applied in one transaction, so readers (whose
 * flows emit after commits) never see part of one (FR-007).
 */
class CatalogStore(private val database: CatalogDatabase, private val clock: Clock) {

    private val dao = database.catalog()

    val status: Flow<CatalogStatus?> = dao.observeMeta().map { it?.let(CatalogMapping::status) }.distinctUntilChanged()

    val settings: Flow<CatalogSettings?> = dao.observeMeta().map { it?.let(CatalogMapping::settings) }.distinctUntilChanged()

    val sellingRules: Flow<SellingRules?> = dao.observeMeta().map { it?.let(CatalogMapping::sellingRules) }.distinctUntilChanged()

    /** Active payment types in the order the owner set (feature #12). */
    fun activePaymentTypes(): Flow<List<PaymentTypeInfo>> =
        dao.observeActivePaymentTypes().map { rows -> rows.map(CatalogMapping::paymentType) }

    /** Every payment type, active or not, in order. */
    suspend fun paymentTypes(): List<PaymentTypeInfo> = dao.paymentTypes().map(CatalogMapping::paymentType)

    /** Every area, active or not, in order. */
    suspend fun tableAreas(): List<TableAreaInfo> = dao.tableAreas().map(CatalogMapping::tableArea)

    /** Every table, active or not, by area and position. */
    suspend fun tables(): List<DiningTableInfo> = dao.diningTables().map(CatalogMapping::diningTable)

    fun activeCategories(): Flow<List<CatalogCategory>> =
        dao.observeActiveCategories().map { rows -> rows.map(CatalogMapping::category) }

    @OptIn(ExperimentalCoroutinesApi::class)
    fun activeProducts(categoryId: UUID): Flow<List<CatalogProduct>> =
        dao.observeActiveProducts(categoryId.toString()).mapLatest { rows ->
            val codes = rows.map { it.id }.chunked(MAX_VARIABLES).flatMap { dao.barcodesOf(it) }.groupBy({ it.productId }, { it.code })
            rows.map { CatalogMapping.product(it, codes[it.id].orEmpty().sorted()) }
        }

    /** The product with [input] as one of its codes, in any equivalent form (FR-010–FR-013, FR-015). */
    suspend fun lookup(input: String): ProductLookup = when (val parsed = BarcodeRules.parse(input)) {
        is BarcodeParse.Invalid -> ProductLookup.Invalid(BarcodeRules.message(parsed.problem))
        is BarcodeParse.Valid -> {
            val row = dao.productByCode(parsed.barcode.code)
            when {
                row == null -> ProductLookup.NotFound
                !row.active || dao.category(row.categoryId)?.active != true -> ProductLookup.NotOffered(row.name)
                else -> ProductLookup.Found(CatalogMapping.product(row, dao.barcodesOf(listOf(row.id)).map { it.code }.sorted()))
            }
        }
    }

    /** Active products of active categories whose name contains [query], ignoring case and accents (FR-014). */
    suspend fun search(query: String, limit: Int = SEARCH_LIMIT): List<CatalogProduct> {
        val text = SearchText.normalize(query)
        if (text.isEmpty()) return emptyList()
        val pattern = "%" + text.replace("!", "!!").replace("%", "!%").replace("_", "!_") + "%"
        val rows = dao.searchProducts(pattern, limit)
        val codes = rows.map { it.id }.chunked(MAX_VARIABLES).flatMap { dao.barcodesOf(it) }.groupBy({ it.productId }, { it.code })
        return rows.map { CatalogMapping.product(it, codes[it.id].orEmpty().sorted()) }
    }

    /** Prices from the copy (tax profiles, categories, discounts, settings); null without a copy. */
    suspend fun priceFeed(): CatalogPriceFeed? {
        val meta = dao.meta() ?: return null
        val versions = dao.taxVersions().groupBy { it.taxProfileId }
        return CatalogPriceFeed(
            CatalogMapping.settings(meta),
            dao.taxProfiles().map { CatalogMapping.taxProfile(it, versions[it.id].orEmpty()) },
            dao.categories().map(CatalogMapping::category),
            dao.discounts().map(CatalogMapping::discount),
        )
    }

    /** Removes the whole copy, leaving nothing in the database file (FR-019, SC-007). */
    suspend fun erase() {
        database.withTransaction { clear() }
        // Android builds SQLite with secure delete (removed rows are overwritten); rebuilding also shrinks the
        // file and covers a build without it.
        database.openHelper.writableDatabase.execSQL("VACUUM")
    }

    /**
     * Erases the copy unless it belongs to [businessId] (null: the till is not paired), so no screen can
     * ever show another business's catalog (Constitution III).
     */
    suspend fun keepOnlyFor(businessId: String?) {
        val owner = businessId()
        if (owner != null && owner != businessId) erase()
    }

    /** The business the copy belongs to, or null when there is none. */
    suspend fun businessId(): String? = dao.meta()?.businessId

    /** The revision to ask changes after, when the copy belongs to [businessId]; otherwise null (download everything). */
    suspend fun revisionFor(businessId: String): Long? = dao.meta()?.takeIf { it.businessId == businessId }?.revision

    /**
     * Applies [download] for [businessId]: a full one replaces everything; changes replace each changed
     * root whole (a profile with its versions, a product with its barcodes, a payment type, an area, a
     * table) and the selling rules. All or nothing.
     */
    suspend fun apply(businessId: String, download: CatalogDownload) {
        database.withTransaction {
            if (download.full) {
                clear()
            } else {
                download.taxProfiles.map { it.id.toString() }.chunked(MAX_VARIABLES).forEach { dao.deleteTaxVersionsOf(it) }
                download.products.map { it.id.toString() }.chunked(MAX_VARIABLES).forEach { dao.deleteBarcodesOf(it) }
                download.removedDiscountIds.map(UUID::toString).chunked(MAX_VARIABLES).forEach { dao.deleteDiscounts(it) }
            }

            dao.upsertTaxProfiles(download.taxProfiles.map(CatalogMapping::row))
            dao.upsertTaxVersions(download.taxProfiles.flatMap(CatalogMapping::rows))
            dao.upsertCategories(download.categories.map(CatalogMapping::row))
            dao.upsertProducts(download.products.map(CatalogMapping::row))
            dao.insertBarcodes(download.products.flatMap(CatalogMapping::barcodeRows))
            dao.upsertDiscounts(download.discounts.map(CatalogMapping::row))
            dao.upsertPaymentTypes(download.paymentTypes.map(CatalogMapping::row))
            dao.upsertTableAreas(download.tableAreas.map(CatalogMapping::row))
            dao.upsertDiningTables(download.tables.map(CatalogMapping::row))
            dao.upsertMeta(CatalogMapping.meta(businessId, download, clock.instant()))
        }
    }

    private companion object {
        /** SQLite before 3.32 (Android 8–10) allows 999 parameters per statement. */
        const val MAX_VARIABLES = 500
        const val SEARCH_LIMIT = 50
    }

    private suspend fun clear() {
        dao.clearMeta()
        dao.clearTaxProfiles()
        dao.clearTaxVersions()
        dao.clearCategories()
        dao.clearProducts()
        dao.clearBarcodes()
        dao.clearDiscounts()
        dao.clearPaymentTypes()
        dao.clearTableAreas()
        dao.clearDiningTables()
    }
}
