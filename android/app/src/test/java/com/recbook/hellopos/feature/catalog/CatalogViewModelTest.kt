package com.recbook.hellopos.feature.catalog

import com.recbook.hellopos.data.catalog.FakeCatalogRepository
import com.recbook.hellopos.data.catalog.RefreshOutcome
import com.recbook.hellopos.data.catalog.TestCatalog
import com.recbook.hellopos.domain.catalog.CatalogCategory
import com.recbook.hellopos.domain.catalog.CatalogPriceFeed
import com.recbook.hellopos.domain.catalog.CatalogTaxProfile
import com.recbook.hellopos.domain.catalog.CatalogTaxVersion
import com.recbook.hellopos.domain.catalog.ProductLookup
import com.recbook.hellopos.domain.catalog.CatalogStatus
import com.recbook.hellopos.domain.catalog.SaleUnit
import com.recbook.hellopos.domain.pricing.TaxKind
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.test.StandardTestDispatcher
import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.test.setMain
import org.junit.jupiter.api.AfterEach
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.BeforeEach
import org.junit.jupiter.api.Test
import java.math.BigDecimal
import java.time.Clock
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneOffset

/** Feature #13 US1: the catalog screen on the till (FR-004, FR-018). */
@OptIn(ExperimentalCoroutinesApi::class)
class CatalogViewModelTest {

    private val dispatcher = StandardTestDispatcher()
    private val now = Instant.parse("2026-10-12T18:00:00Z")
    private val clock = Clock.fixed(now, ZoneOffset.UTC)
    private val catalog = FakeCatalogRepository()

    /** Inside Cola's 10% discount (15–20 Oct, Toronto). */
    private val onDiscountDay = Clock.fixed(Instant.parse("2026-10-16T16:00:00Z"), ZoneOffset.UTC)

    @BeforeEach
    fun setUp() = Dispatchers.setMain(dispatcher)

    @AfterEach
    fun tearDown() = Dispatchers.resetMain()

    @Test
    fun withoutACopy_ItSaysSo_AndRetryDownloads() = runTest(dispatcher) {
        val viewModel = CatalogViewModel(catalog, clock, ONCE)
        runCurrent()
        assertFalse(viewModel.state.value.downloaded)
        catalog.onRefresh = { fill() }

        viewModel.refresh()
        runCurrent()

        assertEquals(1, catalog.refreshes)
        assertTrue(viewModel.state.value.downloaded)
        assertNull(viewModel.state.value.message)
    }

    @Test
    fun aFailedRefresh_SaysWhy_AndKeepsTheCopy() = runTest(dispatcher) {
        fill()
        catalog.nextRefresh = RefreshOutcome.Failed("No connection to the server.")
        val viewModel = CatalogViewModel(catalog, clock, ONCE)

        viewModel.refresh()
        runCurrent()

        assertEquals("No connection to the server.", viewModel.state.value.message)
        assertEquals(listOf("Cola", "Water"), viewModel.state.value.products.map { it.name })
        assertFalse(viewModel.state.value.refreshing)
    }

    @Test
    fun theFirstCategory_IsShown_WithPricesInTheCurrency() = runTest(dispatcher) {
        fill()
        val viewModel = CatalogViewModel(catalog, clock, ONCE)
        runCurrent()

        val state = viewModel.state.value
        assertEquals(listOf("Drinks", "Produce"), state.categories.map { it.name })
        assertEquals(TestCatalog.drinks, state.selectedCategory)
        assertEquals(listOf("1.99", "0.99"), state.products.map { it.price })
        assertEquals("Catalog updated 2 hours ago", state.updated)
    }

    @Test
    fun choosingACategory_ShowsItsProducts_PerUnitAndOpenPrice() = runTest(dispatcher) {
        fill()
        val viewModel = CatalogViewModel(catalog, clock, ONCE)
        runCurrent()

        viewModel.select(PRODUCE)
        runCurrent()

        assertEquals(listOf("Bananas" to "1.49 / kg", "Cake" to "Open price"), viewModel.state.value.products.map { it.name to it.price })
    }

    @Test
    fun theAge_ReadsNaturally() = runTest(dispatcher) {
        fill(refreshedAt = now.minusSeconds(20))
        val viewModel = CatalogViewModel(catalog, clock, ONCE)
        runCurrent()
        assertEquals("Catalog updated just now", viewModel.state.value.updated)

        catalog.statusFlow.value = CatalogStatus(1, now.minusSeconds(60), 0)
        runCurrent()
        assertEquals("Catalog updated 1 minute ago", viewModel.state.value.updated)

        catalog.statusFlow.value = CatalogStatus(1, now.minusSeconds(3 * 86_400), 0)
        runCurrent()
        assertEquals("Catalog updated 3 days ago", viewModel.state.value.updated)
    }

    @Test
    fun aCopyOlderThanADay_IsFlagged_ButStillShown() = runTest(dispatcher) {
        fill(refreshedAt = now.minusSeconds(25 * 3600))
        val viewModel = CatalogViewModel(catalog, clock, ONCE)
        runCurrent()

        val state = viewModel.state.value
        assertEquals(1, state.warnings.size)
        assertTrue(state.warnings.single().startsWith("The catalog has not been updated for over 24 hours"))
        assertEquals(listOf("Cola", "Water"), state.products.map { it.name })
    }

    @Test
    fun aDeviceClockOffByMoreThanFiveMinutes_IsFlagged() = runTest(dispatcher) {
        fill()
        val viewModel = CatalogViewModel(catalog, clock, ONCE)
        runCurrent()
        assertTrue(viewModel.state.value.warnings.isEmpty())

        catalog.statusFlow.value = CatalogStatus(42, now.minusSeconds(60), -12 * 60_000L)
        runCurrent()

        assertEquals(listOf("This till's clock is off by about 12 minutes. Check the date and time settings; discounts and tax changes start by date."), viewModel.state.value.warnings)
    }

    // ---- US3: search, code lookup and product details (FR-010–FR-018) ---------------------------------

    @Test
    fun aNameSearch_ListsMatchingProducts_InsteadOfTheCategory() = runTest(dispatcher) {
        fill()
        val viewModel = CatalogViewModel(catalog, clock, ONCE)
        runCurrent()

        viewModel.onQueryChange("wat")
        viewModel.submitQuery()
        runCurrent()

        assertTrue(viewModel.state.value.searching)
        assertEquals(listOf("Water"), viewModel.state.value.products.map { it.name })
        assertEquals(listOf("wat"), catalog.queries)

        viewModel.onQueryChange("")
        runCurrent()
        assertFalse(viewModel.state.value.searching)
        assertEquals(listOf("Cola", "Water"), viewModel.state.value.products.map { it.name })
    }

    @Test
    fun aScannedCode_OpensTheProduct_WithTodaysDiscountAndTax() = runTest(dispatcher) {
        fill()
        val cola = catalog.productsFlow.value.first { it.id == TestCatalog.cola }.copy(depositPerUnit = 10, minAge = 19, barcodes = listOf("0067000004568"))
        catalog.lookups["0067000004568"] = ProductLookup.Found(cola)
        val viewModel = CatalogViewModel(catalog, onDiscountDay, ONCE)
        runCurrent()

        viewModel.onQueryChange("0067000004568")
        viewModel.submitQuery()
        runCurrent()

        val detail = viewModel.state.value.detail!!
        assertEquals(("Cola" to "1.99"), detail.name to detail.price)
        assertEquals("1.79", detail.discountedPrice)
        assertEquals("10% off until 20 Oct 2026", detail.discount)
        assertEquals("HST 13%", detail.tax)
        assertEquals("Check ID: buyer must be 19 or older", detail.ageRestriction)
        assertEquals("Container deposit 0.10 per piece", detail.deposit)
        assertEquals("", viewModel.state.value.query)

        viewModel.closeDetail()
        runCurrent()
        assertNull(viewModel.state.value.detail)
    }

    @Test
    fun aWrongOrUnknownCode_SaysWhy() = runTest(dispatcher) {
        fill()
        catalog.lookups["067000004569"] = ProductLookup.Invalid("The last digit (check digit) does not match; check the number.")
        catalog.lookups["96385074"] = ProductLookup.NotOffered("Old soda")
        val viewModel = CatalogViewModel(catalog, clock, ONCE)
        runCurrent()

        viewModel.onQueryChange("067000004569")
        viewModel.submitQuery()
        runCurrent()
        assertEquals("The last digit (check digit) does not match; check the number.", viewModel.state.value.lookupMessage)

        viewModel.onQueryChange("4060000000017")
        viewModel.submitQuery()
        runCurrent()
        assertEquals("No product with this code.", viewModel.state.value.lookupMessage)

        viewModel.onQueryChange("96385074")
        viewModel.submitQuery()
        runCurrent()
        assertEquals("Old soda is no longer sold.", viewModel.state.value.lookupMessage)
        assertNull(viewModel.state.value.detail)
    }

    @Test
    fun tappingAProduct_ShowsTheTaxInForceThatDay() = runTest(dispatcher) {
        fill()
        val viewModel = CatalogViewModel(catalog, Clock.fixed(Instant.parse("2026-11-01T05:00:00Z"), ZoneOffset.UTC), ONCE)
        runCurrent()

        viewModel.open(TestCatalog.water)
        runCurrent()

        val detail = viewModel.state.value.detail!!
        assertEquals("HST 15%", detail.tax)
        assertNull(detail.discountedPrice)
        assertEquals("Sold per piece, from 1 in steps of 1", detail.quantity)
    }

    @Test
    fun weighedAndZeroRatedProducts_ReadNaturally() {
        val produce = CatalogCategory(PRODUCE, "Produce", 2, ZERO, true)
        val zeroRated = CatalogTaxProfile(ZERO, "Zero-rated", TaxKind.ZeroRated, true, listOf(CatalogTaxVersion(ZERO, LocalDate.of(2026, 1, 1), emptyList())))
        val feed = CatalogPriceFeed(TestCatalog.settings, listOf(TestCatalog.hstProfile, zeroRated), listOf(produce), emptyList())
        val bananas = TestCatalog.product(TestCatalog.oldSoda, "Bananas", category = PRODUCE, price = 149)
            .copy(unit = SaleUnit.Kilogram, minQuantity = BigDecimal("0.005"), step = BigDecimal("0.005"))

        val detail = CatalogViewModel.describe(bananas, feed, now)

        assertEquals("1.49 / kg", detail.price)
        assertEquals("Sold per kg, from 0.005 in steps of 0.005", detail.quantity)
        assertEquals("Zero-rated (no tax)", detail.tax)
    }

    private fun TestScope.fill(refreshedAt: Instant = now.minusSeconds(2 * 3600 + 120)) {
        catalog.statusFlow.value = CatalogStatus(42, refreshedAt, 0)
        catalog.settingsFlow.value = TestCatalog.settings
        catalog.feed = CatalogPriceFeed(
            TestCatalog.settings,
            listOf(TestCatalog.hstProfile),
            listOf(CatalogCategory(TestCatalog.drinks, "Drinks", 1, TestCatalog.hst, true), CatalogCategory(PRODUCE, "Produce", 2, TestCatalog.hst, true)),
            TestCatalog.full().discounts,
        )
        catalog.categoriesFlow.value = listOf(
            CatalogCategory(TestCatalog.drinks, "Drinks", 1, TestCatalog.hst, true),
            CatalogCategory(PRODUCE, "Produce", 2, TestCatalog.hst, true),
        )
        catalog.productsFlow.value = listOf(
            TestCatalog.product(TestCatalog.cola, "Cola"),
            TestCatalog.product(TestCatalog.water, "Water", price = 99),
            TestCatalog.product(TestCatalog.oldSoda, "Bananas", category = PRODUCE, price = 149)
                .copy(unit = SaleUnit.Kilogram, minQuantity = BigDecimal("0.005"), step = BigDecimal("0.005")),
            TestCatalog.product(java.util.UUID.randomUUID(), "Cake", category = PRODUCE, price = null),
        )
    }

    private companion object {
        val ONCE = com.recbook.hellopos.core.util.Ticker { kotlinx.coroutines.flow.flowOf(Unit) }

        val PRODUCE: java.util.UUID = java.util.UUID.fromString("00000000-0000-4000-8000-000000000203")
        val ZERO: java.util.UUID = java.util.UUID.fromString("00000000-0000-4000-8000-000000000102")
    }
}
