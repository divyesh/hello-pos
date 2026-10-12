package com.recbook.hellopos.data.catalog

import android.app.Application
import androidx.room.Room
import com.recbook.hellopos.data.catalog.TestCatalog.BUSINESS
import com.recbook.hellopos.data.catalog.TestCatalog.cola
import com.recbook.hellopos.data.catalog.TestCatalog.drinks
import com.recbook.hellopos.data.catalog.TestCatalog.oldSoda
import com.recbook.hellopos.data.catalog.TestCatalog.snacks
import com.recbook.hellopos.data.catalog.TestCatalog.water
import com.recbook.hellopos.domain.catalog.CatalogCategory
import com.recbook.hellopos.domain.catalog.ProductLookup
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.RuntimeEnvironment
import org.robolectric.annotation.Config
import java.time.Clock
import java.time.Instant
import java.time.ZoneOffset
import java.util.UUID

/** Feature #13 US3: finding products on the till with no network (FR-010–FR-015, SC-002). */
@RunWith(RobolectricTestRunner::class)
@Config(sdk = [35], application = Application::class)
class CatalogLookupTest {

    private val database = Room.inMemoryDatabaseBuilder(RuntimeEnvironment.getApplication(), CatalogDatabase::class.java)
        .allowMainThreadQueries().build()
    private val store = CatalogStore(database, Clock.fixed(Instant.parse("2026-10-12T16:00:00Z"), ZoneOffset.UTC))

    @After
    fun close() = database.close()

    @Test
    fun aUpcA_IsFoundAsTypedOrInItsEan13Form() = runBlocking {
        store.apply(BUSINESS, TestCatalog.full())

        val typed = store.lookup("067000004568")
        val ean = store.lookup("0067000004568")

        assertEquals(cola, (typed as ProductLookup.Found).product.id)
        assertEquals(typed, ean)
    }

    @Test
    fun aPlu_IsFound() = runBlocking {
        store.apply(BUSINESS, TestCatalog.full())

        assertEquals(water, (store.lookup("4011") as ProductLookup.Found).product.id)
    }

    @Test
    fun aWrongCode_SaysWhy_AndAnUnknownOne_IsNotFound() = runBlocking {
        store.apply(BUSINESS, TestCatalog.full())

        assertEquals(ProductLookup.Invalid("The last digit (check digit) does not match; check the number."), store.lookup("067000004569"))
        assertEquals(ProductLookup.NotFound, store.lookup("4060000000017"))
    }

    @Test
    fun inactiveProductsAndCategories_AreNotOffered() = runBlocking {
        val hidden = UUID.fromString("00000000-0000-4000-8000-000000000205")
        store.apply(
            BUSINESS,
            TestCatalog.full(
                products = listOf(
                    TestCatalog.product(oldSoda, "Old soda", active = false, barcodes = arrayOf("96385074")),
                    TestCatalog.product(cola, "Cola", category = hidden, barcodes = arrayOf("0067000004568")),
                    TestCatalog.product(water, "Water"),
                ),
                categories = listOf(CatalogCategory(drinks, "Drinks", 1, TestCatalog.hst, true), CatalogCategory(hidden, "Old", 2, TestCatalog.hst, false)),
            ),
        )

        assertEquals(ProductLookup.NotOffered("Old soda"), store.lookup("96385074"))
        assertEquals(ProductLookup.NotOffered("Cola"), store.lookup("067000004568"))
        assertEquals(listOf("Water"), store.search("a").map { it.name })
    }

    @Test
    fun nameSearch_MatchesAnyPart_IgnoringCaseAndAccents() = runBlocking {
        store.apply(
            BUSINESS,
            TestCatalog.full(
                products = listOf(
                    TestCatalog.product(cola, "Crème brûlée", category = snacks),
                    TestCatalog.product(water, "100% Juice"),
                    TestCatalog.product(oldSoda, "Juice_box"),
                ),
            ),
        )

        assertEquals(listOf("Crème brûlée"), store.search("CREME").map { it.name })
        assertEquals(listOf("Crème brûlée"), store.search("brul").map { it.name })
        assertEquals(listOf("100% Juice"), store.search("0%").map { it.name })
        assertEquals(listOf("Juice_box"), store.search("e_b").map { it.name })
        assertTrue(store.search("  ").isEmpty())
    }

    @Test
    fun tenThousandProducts_AreSavedQuickly_AndFoundInUnder200Milliseconds() = runBlocking {
        val products = (1..10_000).map { i ->
            TestCatalog.product(
                UUID(0, i.toLong()), "Product $i", barcodes = arrayOf(ean13(i)),
            )
        }
        val applied = measure { store.apply(BUSINESS, TestCatalog.full(products = products, discounts = emptyList())) }
        store.lookup(ean13(1)) // warm up
        store.search("Product 1")

        val code = measure { store.lookup(ean13(7_777)) }
        val name = measure { store.search("product 9876") }

        assertEquals("Product 7777", (code.first as ProductLookup.Found).product.name)
        assertEquals(listOf("Product 9876"), name.first.map { it.name })
        assertTrue("code lookup took ${code.second} ms", code.second < 200)
        assertTrue("name search took ${name.second} ms", name.second < 200)
        println("10,000 products: saved in ${applied.second} ms, code lookup ${code.second} ms, name search ${name.second} ms")
        // SC-001 on a real till also includes the network; this bounds the local part.
        assertTrue("saving 10,000 products took ${applied.second} ms", applied.second < 20_000)
    }

    private suspend fun <T> measure(block: suspend () -> T): Pair<T, Long> {
        val start = System.nanoTime()
        val result = block()
        return result to (System.nanoTime() - start) / 1_000_000
    }

    /** A valid EAN-13 from a number (prefix 400, outside the in-store range). */
    private fun ean13(n: Int): String {
        val body = "400" + n.toString().padStart(9, '0')
        return body + com.recbook.hellopos.domain.catalog.BarcodeRules.checkDigit(body)
    }
}
