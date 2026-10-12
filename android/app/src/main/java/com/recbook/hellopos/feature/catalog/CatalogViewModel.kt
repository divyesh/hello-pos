package com.recbook.hellopos.feature.catalog

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.recbook.hellopos.core.util.Ticker
import com.recbook.hellopos.data.catalog.CatalogRepository
import com.recbook.hellopos.data.catalog.RefreshOutcome
import com.recbook.hellopos.domain.catalog.CatalogPriceFeed
import com.recbook.hellopos.domain.catalog.CatalogProduct
import com.recbook.hellopos.domain.catalog.CatalogSettings
import com.recbook.hellopos.domain.catalog.CatalogStatus
import com.recbook.hellopos.domain.catalog.ProductLookup
import com.recbook.hellopos.domain.catalog.SaleUnit
import com.recbook.hellopos.domain.money.Money
import com.recbook.hellopos.domain.pricing.DiscountKind
import com.recbook.hellopos.domain.pricing.TaxKind
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.flatMapLatest
import kotlinx.coroutines.flow.flowOf
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch
import java.math.BigDecimal
import java.math.RoundingMode
import java.time.Clock
import java.time.Duration
import java.time.Instant
import java.time.format.DateTimeFormatter
import java.util.Locale
import java.util.UUID
import javax.inject.Inject
import kotlin.time.Duration.Companion.minutes

data class CategoryItem(val id: UUID, val name: String)

data class ProductItem(val id: UUID, val name: String, val price: String)

/** Everything the till knows about one product, as text (FR-018). */
data class ProductDetail(
    val id: UUID,
    val name: String,
    /** Regular price per unit, or "Open price". */
    val price: String,
    /** The price after today's item discount, when one is running. */
    val discountedPrice: String? = null,
    /** "10% off until 20 Oct 2026". */
    val discount: String? = null,
    /** "Sold per kg, from 0.005 in steps of 0.005". */
    val quantity: String,
    /** "HST 13%", "GST 5%: CGST 2.5% + SGST 2.5%", "Zero-rated (no tax)". */
    val tax: String,
    val ageRestriction: String? = null,
    val deposit: String? = null,
    val barcodes: List<String> = emptyList(),
)

data class CatalogUiState(
    /** False until a first download succeeded on this till. */
    val downloaded: Boolean = false,
    /** "Catalog updated 2 hours ago". */
    val updated: String? = null,
    val categories: List<CategoryItem> = emptyList(),
    val selectedCategory: UUID? = null,
    /** The selected category's products, or the search results while [searching]. */
    val products: List<ProductItem> = emptyList(),
    val query: String = "",
    val searching: Boolean = false,
    /** Why a code was not found or is not valid. */
    val lookupMessage: String? = null,
    val detail: ProductDetail? = null,
    val refreshing: Boolean = false,
    /** Why the last refresh failed; the copy shown is still the previous one. */
    val message: String? = null,
    /** Stale copy or a wrong device clock (FR-009, research R10); informational, nothing is blocked. */
    val warnings: List<String> = emptyList(),
)

/**
 * The read-only catalog on the till (feature #13, FR-018): categories in sort order with their active
 * products, search by name or code (typed, or from a scanner that types and presses Enter), product details
 * with the price in force now, how old the copy is, and a refresh.
 */
@OptIn(ExperimentalCoroutinesApi::class)
@HiltViewModel
class CatalogViewModel @Inject constructor(
    private val catalog: CatalogRepository,
    private val clock: Clock,
    ticker: Ticker,
) : ViewModel() {

    private val chosen = MutableStateFlow<UUID?>(null)
    private val refreshing = MutableStateFlow(false)
    private val message = MutableStateFlow<String?>(null)
    private val search = MutableStateFlow(SearchState())
    private val detail = MutableStateFlow<ProductDetail?>(null)

    /** Re-renders the age while the screen is open. */
    private val ticks = ticker.every(1.minutes).map { clock.instant() }

    private val categories = catalog.categories()

    private val selected = combine(chosen, categories) { id, all -> id?.takeIf { wanted -> all.any { it.id == wanted } } ?: all.firstOrNull()?.id }
        .distinctUntilChanged()

    private val categoryProducts = selected.flatMapLatest { id -> if (id == null) flowOf(emptyList()) else catalog.products(id) }

    /** Products on screen, by id, so a tap can open their details. */
    private var shown: Map<UUID, CatalogProduct> = emptyMap()

    private val activity = combine(refreshing, message, ticks) { busy, text, now -> Activity(busy, text, now) }

    val state: StateFlow<CatalogUiState> =
        combine(
            combine(catalog.status, catalog.settings, ::Pair),
            categories,
            combine(selected, categoryProducts, search, ::Triple),
            detail,
            activity,
        ) { (status, settings), all, (id, items, searched), detailShown, (busy, text, now) ->
            val listed = searched.results ?: items
            shown = listed.associateBy { it.id }
            CatalogUiState(
                downloaded = status != null,
                updated = status?.let { "Catalog updated ${age(it, now)}" },
                categories = all.map { CategoryItem(it.id, it.name) },
                selectedCategory = id,
                products = if (settings == null) emptyList() else listed.map { ProductItem(it.id, it.name, priceOf(it, settings)) },
                query = searched.query,
                searching = searched.results != null,
                lookupMessage = searched.lookupMessage,
                detail = detailShown,
                refreshing = busy,
                message = text,
                warnings = status?.let { warningsOf(it, now) }.orEmpty(),
            )
        }.stateIn(viewModelScope, SharingStarted.Eagerly, CatalogUiState())

    fun select(categoryId: UUID) {
        chosen.value = categoryId
        search.value = SearchState()
    }

    fun onQueryChange(text: String) {
        search.value = if (text.isBlank()) SearchState() else search.value.copy(query = text, lookupMessage = null)
    }

    /**
     * Enter or the search button: a code (digits, spaces and dashes only) is looked up and opened; anything
     * else searches names.
     */
    fun submitQuery() {
        val query = search.value.query.trim()
        if (query.isEmpty()) return
        viewModelScope.launch {
            if (query.all { it.isDigit() || it == ' ' || it == '-' }) {
                when (val found = catalog.lookup(query)) {
                    is ProductLookup.Found -> {
                        search.value = SearchState()
                        detail.value = detailOf(found.product)
                    }
                    is ProductLookup.Invalid -> search.value = search.value.copy(lookupMessage = found.message)
                    ProductLookup.NotFound -> search.value = search.value.copy(lookupMessage = ProductLookup.NOT_FOUND)
                    is ProductLookup.NotOffered -> search.value = search.value.copy(lookupMessage = "${found.name} is no longer sold.")
                }
            } else {
                search.value = search.value.copy(results = catalog.search(query), lookupMessage = null)
            }
        }
    }

    fun open(productId: UUID) {
        val product = shown[productId] ?: return
        viewModelScope.launch { detail.value = detailOf(product) }
    }

    fun closeDetail() {
        detail.value = null
    }

    fun refresh() {
        if (refreshing.value) return
        refreshing.value = true
        viewModelScope.launch {
            message.value = when (val outcome = catalog.refresh()) {
                RefreshOutcome.Updated -> null
                is RefreshOutcome.Failed -> outcome.reason
            }
            refreshing.value = false
        }
    }

    private suspend fun detailOf(product: CatalogProduct): ProductDetail? {
        val feed = catalog.priceFeed() ?: return null
        return describe(product, feed, clock.instant())
    }

    private fun age(status: CatalogStatus, now: Instant): String {
        val minutes = Duration.between(status.refreshedAt, now).toMinutes().coerceAtLeast(0)
        return when {
            minutes < 1 -> "just now"
            minutes < 60 -> plural(minutes, "minute")
            minutes < 24 * 60 -> plural(minutes / 60, "hour")
            else -> plural(minutes / (24 * 60), "day")
        }
    }

    private fun warningsOf(status: CatalogStatus, now: Instant): List<String> = buildList {
        if (Duration.between(status.refreshedAt, now) > STALE_AFTER) {
            add("The catalog has not been updated for over 24 hours. Prices may be out of date; check the connection.")
        }
        val offMinutes = Math.abs(status.serverOffsetMillis) / 60_000
        if (Math.abs(status.serverOffsetMillis) > CLOCK_TOLERANCE.toMillis()) {
            add("This till's clock is off by about $offMinutes minutes. Check the date and time settings; discounts and tax changes start by date.")
        }
    }

    private fun plural(count: Long, unit: String) = "$count $unit${if (count == 1L) "" else "s"} ago"

    private data class SearchState(val query: String = "", val results: List<CatalogProduct>? = null, val lookupMessage: String? = null)

    private data class Activity(val busy: Boolean, val text: String?, val now: Instant)

    companion object {
        const val OPEN_PRICE = "Open price"
        val STALE_AFTER: Duration = Duration.ofHours(24)
        val CLOCK_TOLERANCE: Duration = Duration.ofMinutes(5)
        private val DAY = DateTimeFormatter.ofPattern("d MMM yyyy", Locale.ENGLISH)

        /** "1.99", "1.49 / kg", or [OPEN_PRICE]. */
        fun priceOf(product: CatalogProduct, settings: CatalogSettings): String {
            val price = product.unitPrice ?: return OPEN_PRICE
            return money(price, settings) + perUnit(product.unit)
        }

        /** The product as the till prices it at [at]: tax version and discount of that local date. */
        fun describe(product: CatalogProduct, feed: CatalogPriceFeed, at: Instant): ProductDetail {
            val settings = feed.settings
            val profile = feed.taxProfileOf(product)
            val tax = feed.taxAt(product, at)
            val discount = feed.discountAt(product, at)
            val unitPrice = product.unitPrice
            val discounted = if (discount == null || unitPrice == null) {
                null
            } else {
                val off = when (discount.kind) {
                    DiscountKind.Percent -> BigDecimal.valueOf(unitPrice).multiply(discount.value).divide(BigDecimal(100), 0, RoundingMode.HALF_UP).toLong()
                    DiscountKind.Fixed -> discount.value.toLong()
                }
                (unitPrice - off).coerceAtLeast(0)
            }
            return ProductDetail(
                id = product.id,
                name = product.name,
                price = priceOf(product, settings),
                discountedPrice = discounted?.let { money(it, settings) + perUnit(product.unit) },
                discount = discount?.let {
                    val amount = when (it.kind) {
                        DiscountKind.Percent -> "${it.value.stripTrailingZeros().toPlainString()}% off"
                        DiscountKind.Fixed -> "${money(it.value.toLong(), settings)} off each"
                    }
                    if (it.toDate == null) amount else "$amount until ${DAY.format(it.toDate)}"
                },
                quantity = "Sold per ${unitName(product.unit)}, from ${product.minQuantity.stripTrailingZeros().toPlainString()} " +
                    "in steps of ${product.step.stripTrailingZeros().toPlainString()}",
                tax = when (tax.kind) {
                    TaxKind.ZeroRated, TaxKind.Exempt -> "${profile.name} (no tax)"
                    TaxKind.Taxable -> {
                        val rates = tax.components.joinToString(" + ") { "${it.label} ${it.ratePercent.stripTrailingZeros().toPlainString()}%" }
                        if (tax.components.size == 1 && tax.components[0].label == profile.name) rates else "${profile.name}: $rates"
                    }
                },
                ageRestriction = product.minAge?.let { "Check ID: buyer must be $it or older" },
                deposit = product.depositPerUnit.takeIf { it > 0 }?.let { "Container deposit ${money(it, settings)} per ${unitName(product.unit)}" },
                barcodes = product.barcodes,
            )
        }

        private fun money(minor: Long, settings: CatalogSettings) = Money(minor, settings.currency).toBigDecimal().toPlainString()

        private fun perUnit(unit: SaleUnit) = if (unit == SaleUnit.Piece) "" else " / ${CatalogPriceFeed.WIRE_UNITS.getValue(unit)}"

        private fun unitName(unit: SaleUnit) = if (unit == SaleUnit.Piece) "piece" else CatalogPriceFeed.WIRE_UNITS.getValue(unit)
    }
}
