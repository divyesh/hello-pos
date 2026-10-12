package com.recbook.hellopos.data.catalog

import com.recbook.hellopos.domain.catalog.CatalogCategory
import com.recbook.hellopos.domain.catalog.CatalogPriceFeed
import com.recbook.hellopos.domain.catalog.CatalogProduct
import com.recbook.hellopos.domain.catalog.CatalogSettings
import com.recbook.hellopos.domain.catalog.CatalogStatus
import com.recbook.hellopos.domain.catalog.ProductLookup
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.map
import java.util.UUID

/** In-memory catalog for view model and scheduler tests; [nextRefresh] decides what a refresh returns. */
class FakeCatalogRepository : CatalogRepository {
    val statusFlow = MutableStateFlow<CatalogStatus?>(null)
    val settingsFlow = MutableStateFlow<CatalogSettings?>(null)
    val categoriesFlow = MutableStateFlow<List<CatalogCategory>>(emptyList())
    val productsFlow = MutableStateFlow<List<CatalogProduct>>(emptyList())
    var nextRefresh: RefreshOutcome = RefreshOutcome.Updated
    var refreshes = 0
        private set

    /** Runs when a refresh succeeds (e.g. to fill the flows as a download would). */
    var onRefresh: () -> Unit = {}

    override val status: Flow<CatalogStatus?> = statusFlow

    override val settings: Flow<CatalogSettings?> = settingsFlow

    override fun categories(): Flow<List<CatalogCategory>> = categoriesFlow

    override fun products(categoryId: UUID): Flow<List<CatalogProduct>> =
        productsFlow.map { all -> all.filter { it.categoryId == categoryId } }

    /** Codes the fake knows; anything else is [ProductLookup.NotFound]. */
    val lookups = mutableMapOf<String, ProductLookup>()
    val queries = mutableListOf<String>()

    override suspend fun lookup(code: String): ProductLookup = lookups[code] ?: ProductLookup.NotFound

    override suspend fun search(query: String): List<CatalogProduct> {
        queries += query
        return productsFlow.value.filter { it.active && it.name.contains(query, ignoreCase = true) }
    }

    var feed: CatalogPriceFeed? = null

    override suspend fun priceFeed(): CatalogPriceFeed? = feed

    override suspend fun refresh(): RefreshOutcome {
        refreshes++
        if (nextRefresh == RefreshOutcome.Updated) onRefresh()
        return nextRefresh
    }
}
