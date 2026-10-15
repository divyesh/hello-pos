package com.recbook.hellopos.data.catalog

import android.app.Application
import androidx.room.Room
import com.recbook.hellopos.data.catalog.TestCatalog.BUSINESS
import com.recbook.hellopos.data.catalog.TestCatalog.cola
import com.recbook.hellopos.data.catalog.TestCatalog.drinks
import com.recbook.hellopos.data.catalog.TestCatalog.water
import com.recbook.hellopos.domain.catalog.CatalogProduct
import com.recbook.hellopos.domain.catalog.CatalogTaxProfile
import com.recbook.hellopos.domain.catalog.CatalogTaxVersion
import com.recbook.hellopos.domain.pricing.TaxComponent
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.RuntimeEnvironment
import org.robolectric.annotation.Config
import java.math.BigDecimal
import java.time.Clock
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneOffset
import java.util.UUID

/** Feature #13 US1: the copy on the till (FR-003, FR-004, FR-007; research R6). */
@RunWith(RobolectricTestRunner::class)
@Config(sdk = [35], application = Application::class)
class CatalogStoreTest {

    private val now = Instant.parse("2026-10-12T16:00:05Z")
    private val clock = Clock.fixed(now, ZoneOffset.UTC)
    private val context = RuntimeEnvironment.getApplication()
    private var database = open()
    private var store = CatalogStore(database, clock)

    @After
    fun close() {
        database.close()
        context.deleteDatabase(NAME)
    }

    @Test
    fun aFullDownload_IsKept_WithItsStatus() = runBlocking {
        assertNull(store.status.first())

        store.apply(BUSINESS, TestCatalog.full())

        val status = assertNotNullAnd(store.status.first())
        assertEquals(100L, status.revision)
        assertEquals(now, status.refreshedAt)
        assertEquals(-5_000L, status.serverOffsetMillis)
        assertEquals(BUSINESS, store.businessId())
        assertEquals(listOf("Drinks", "Snacks"), store.activeCategories().first().map { it.name })
        val products = store.activeProducts(drinks).first()
        assertEquals(listOf("Cola", "Water"), products.map { it.name })
        assertEquals(listOf("0067000004568"), products.first { it.id == cola }.barcodes)
        assertEquals(99L, products.first { it.id == water }.unitPrice)
        assertEquals(BigDecimal.ONE, products.first().step)
    }

    @Test
    fun aFullDownload_ReplacesEverythingBefore() = runBlocking {
        store.apply(BUSINESS, TestCatalog.full())

        store.apply(BUSINESS, TestCatalog.full(revision = 200, products = listOf(TestCatalog.product(water, "Water", price = 109))))

        assertEquals(200L, store.status.first()?.revision)
        assertEquals(listOf(109L), store.activeProducts(drinks).first().map { it.unitPrice })
        assertTrue(database.catalog().barcodes().isEmpty())
    }

    @Test
    fun aDownloadThatFailsHalfway_LeavesThePreviousCopyUnchanged() = runBlocking {
        store.apply(BUSINESS, TestCatalog.full())
        // One code on two products: the barcode insert fails after products were already written.
        val broken = TestCatalog.full(
            revision = 300,
            products = listOf(
                TestCatalog.product(cola, "Cola renamed", barcodes = arrayOf("0067000004568")),
                TestCatalog.product(water, "Water renamed", barcodes = arrayOf("0067000004568")),
            ),
        )

        val failure = runCatching { store.apply(BUSINESS, broken) }

        assertTrue(failure.isFailure)
        assertEquals(100L, store.status.first()?.revision)
        assertEquals(listOf("Cola", "Water"), store.activeProducts(drinks).first().map { it.name })
        assertEquals(2, database.catalog().barcodes().size)
    }

    @Test
    fun theCopy_SurvivesReopeningTheDatabase() = runBlocking {
        store.apply(BUSINESS, TestCatalog.full())
        database.close()

        database = open()
        store = CatalogStore(database, clock)

        assertEquals(100L, store.status.first()?.revision)
        assertEquals(3, database.catalog().products().size)
    }

    // ---- US2: applying changes (FR-006, FR-007) ---------------------------------------------------------

    @Test
    fun changes_ReplaceEachChangedRootWhole_AndLeaveTheRest() = runBlocking {
        store.apply(BUSINESS, TestCatalog.full())
        val newVersion = CatalogTaxVersion(UUID.fromString("00000000-0000-4000-8000-000000000113"), LocalDate.of(2027, 1, 1), listOf(TaxComponent("HST", BigDecimal("14"))))

        store.apply(
            BUSINESS,
            changes(
                revision = 150,
                taxProfiles = listOf(TestCatalog.hstProfile.copy(versions = listOf(TestCatalog.hstProfile.versions.first(), newVersion))),
                // The code moves from Water to Cola: the server sends both (removing it stamped Water).
                products = listOf(TestCatalog.product(cola, "Cola", price = 219, barcodes = arrayOf("4011")), TestCatalog.product(water, "Water", price = 99)),
                removedDiscountIds = listOf(UUID.fromString("00000000-0000-4000-8000-000000000401")),
            ),
        )

        assertEquals(150L, store.status.first()?.revision)
        val products = store.activeProducts(drinks).first()
        assertEquals(219L, products.first { it.id == cola }.unitPrice)
        assertEquals(listOf("4011"), products.first { it.id == cola }.barcodes)
        assertEquals(listOf<String>(), products.first { it.id == water }.barcodes)
        assertEquals(listOf("2026-01-01", "2027-01-01"), database.catalog().taxVersions().map { it.effectiveFrom })
        assertTrue(database.catalog().discounts().isEmpty())
        assertEquals(3, database.catalog().products().size)
    }

    @Test
    fun aFailingChangeSet_KeepsTheOldRevisionAndData() = runBlocking {
        store.apply(BUSINESS, TestCatalog.full())

        val failure = runCatching {
            store.apply(
                BUSINESS,
                changes(
                    revision = 160,
                    products = listOf(
                        TestCatalog.product(cola, "Cola", price = 249, barcodes = arrayOf("96385074")),
                        TestCatalog.product(TestCatalog.oldSoda, "Old soda", barcodes = arrayOf("96385074")),
                    ),
                ),
            )
        }

        assertTrue(failure.isFailure)
        assertEquals(100L, store.status.first()?.revision)
        assertEquals(199L, store.activeProducts(drinks).first().first { it.id == cola }.unitPrice)
        assertEquals(listOf("0067000004568", "4011"), database.catalog().barcodes().map { it.code }.sorted())
    }

    @Test
    fun aReader_NeverSeesHalfAChangeSet() = runBlocking {
        store.apply(BUSINESS, TestCatalog.full())
        val seen = mutableListOf<List<Long?>>()
        val watcher = launch(Dispatchers.IO) {
            store.activeProducts(drinks).collect { products -> synchronized(seen) { seen += products.sortedBy { it.name }.map { it.unitPrice } } }
        }
        while (synchronized(seen) { seen.isEmpty() }) delay(5)

        repeat(20) { round ->
            val price = 300L + round
            store.apply(
                BUSINESS,
                changes(revision = 200L + round, products = listOf(TestCatalog.product(cola, "Cola", price = price), TestCatalog.product(water, "Water", price = price))),
            )
        }
        delay(200)
        watcher.cancel()

        val snapshots = synchronized(seen) { seen.toList() }
        assertTrue("a reader saw a half-applied change: $snapshots", snapshots.drop(1).all { it.toSet().size == 1 })
        assertEquals(listOf(319L, 319L), snapshots.last())
    }

    private fun changes(
        revision: Long,
        taxProfiles: List<CatalogTaxProfile> = emptyList(),
        products: List<CatalogProduct> = emptyList(),
        removedDiscountIds: List<UUID> = emptyList(),
    ) = TestCatalog.full(revision = revision).copy(
        full = false,
        taxProfiles = taxProfiles,
        categories = emptyList(),
        products = products,
        discounts = emptyList(),
        removedDiscountIds = removedDiscountIds,
        paymentTypes = emptyList(),
        tableAreas = emptyList(),
        tables = emptyList(),
    )

    private fun open() = Room.databaseBuilder(context, CatalogDatabase::class.java, NAME).allowMainThreadQueries().build()

    private fun <T : Any> assertNotNullAnd(value: T?): T {
        assertNotNull(value)
        return value!!
    }

    private companion object {
        const val NAME = "catalog-test.db"
    }
}
