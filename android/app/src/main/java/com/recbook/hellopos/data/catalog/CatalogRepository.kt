package com.recbook.hellopos.data.catalog

import com.recbook.hellopos.domain.catalog.CatalogCategory
import com.recbook.hellopos.domain.catalog.CatalogPriceFeed
import com.recbook.hellopos.domain.catalog.CatalogProduct
import com.recbook.hellopos.domain.catalog.CatalogSettings
import com.recbook.hellopos.domain.catalog.CatalogStatus
import com.recbook.hellopos.domain.catalog.ProductLookup
import com.recbook.hellopos.core.session.DeviceStore
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.flatMapLatest
import kotlinx.coroutines.flow.flowOf
import kotlinx.coroutines.flow.map
import java.util.UUID
import javax.inject.Inject
import javax.inject.Singleton

/** The till's catalog for screens (feature #13): what is offered, how fresh it is, and refreshing it. */
interface CatalogRepository {
    /** Null until a first download succeeded. */
    val status: Flow<CatalogStatus?>

    /** The pricing settings of the copy (currency, pricing mode, …); null without a copy. */
    val settings: Flow<CatalogSettings?>

    /** Active categories in sort order. */
    fun categories(): Flow<List<CatalogCategory>>

    /** Active products of an active category, by name. */
    fun products(categoryId: UUID): Flow<List<CatalogProduct>>

    /** The product with this barcode or PLU code, or why there is none. */
    suspend fun lookup(code: String): ProductLookup

    /** Offered products whose name contains [query]. */
    suspend fun search(query: String): List<CatalogProduct>

    /** Prices from the current copy; null without a copy. */
    suspend fun priceFeed(): CatalogPriceFeed?

    suspend fun refresh(): RefreshOutcome
}

/**
 * Every read first checks that the copy belongs to the business this till is paired to and erases it
 * otherwise (FR-019); unpairing therefore also empties every catalog screen.
 */
@OptIn(ExperimentalCoroutinesApi::class)
@Singleton
class DefaultCatalogRepository @Inject constructor(
    private val store: CatalogStore,
    private val sync: CatalogSync,
    private val devices: DeviceStore,
) : CatalogRepository {

    private val owner: Flow<String?> = devices.registration.map { it?.businessId }.distinctUntilChanged()

    override val status: Flow<CatalogStatus?> = guarded { store.status }

    override val settings: Flow<CatalogSettings?> = guarded { store.settings }

    override fun categories(): Flow<List<CatalogCategory>> = guarded(emptyList()) { store.activeCategories() }

    override fun products(categoryId: UUID): Flow<List<CatalogProduct>> = guarded(emptyList()) { store.activeProducts(categoryId) }

    override suspend fun lookup(code: String): ProductLookup {
        guard()
        return store.lookup(code)
    }

    override suspend fun search(query: String): List<CatalogProduct> {
        guard()
        return store.search(query)
    }

    override suspend fun priceFeed(): CatalogPriceFeed? {
        guard()
        return store.priceFeed()
    }

    override suspend fun refresh(): RefreshOutcome = sync.refresh()

    private suspend fun guard() = store.keepOnlyFor(devices.registration.value?.businessId)

    private fun <T> guarded(unpaired: T, source: () -> Flow<T>): Flow<T> = owner.flatMapLatest { business ->
        store.keepOnlyFor(business)
        if (business == null) flowOf(unpaired) else source()
    }

    private fun <T> guarded(source: () -> Flow<T?>): Flow<T?> = guarded(null, source)
}
