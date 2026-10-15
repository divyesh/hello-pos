package com.recbook.hellopos.data.catalog

import android.app.Application
import android.database.sqlite.SQLiteDatabase
import com.recbook.hellopos.data.catalog.TestCatalog.BUSINESS
import com.recbook.hellopos.data.catalog.TestCatalog.card
import com.recbook.hellopos.data.catalog.TestCatalog.cash
import com.recbook.hellopos.data.catalog.TestCatalog.hall
import com.recbook.hellopos.domain.catalog.PaymentKind
import com.recbook.hellopos.domain.catalog.SellingRules
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.RuntimeEnvironment
import org.robolectric.annotation.Config
import java.io.File
import java.math.BigDecimal
import java.time.Clock
import java.time.Instant
import java.time.ZoneOffset

/** Feature #12 US5: the business settings in the till's copy (FR-018, data-model.md "Till"). */
@RunWith(RobolectricTestRunner::class)
@Config(sdk = [35], application = Application::class)
class TillSettingsStoreTest {

    private val clock = Clock.fixed(Instant.parse("2026-10-14T16:00:00Z"), ZoneOffset.UTC)
    private val context = RuntimeEnvironment.getApplication()
    private var database: CatalogDatabase? = null

    @After
    fun close() {
        database?.close()
        context.deleteDatabase(NAME)
    }

    @Test
    fun aFullDownload_KeepsPaymentTypesTablesAndTheSellingRules() = runBlocking {
        val store = open()

        store.apply(BUSINESS, TestCatalog.full())

        // The till offers active types in the owner's order; inactive ones stay in the copy for past sales.
        assertEquals(listOf("Cash", "Card (terminal)"), store.activePaymentTypes().first().map { it.name })
        assertEquals(listOf("Cash", "Card (terminal)", "Cheque"), store.paymentTypes().map { it.name })
        val cashType = store.paymentTypes().first()
        assertEquals(PaymentKind.Cash, cashType.kind)
        assertTrue(cashType.builtIn && cashType.givesChange && cashType.opensDrawer)
        assertTrue(store.paymentTypes().last().asksReference)
        assertEquals(listOf("Hall" to true, "Patio" to false), store.tableAreas().map { it.name to it.active })
        assertEquals(listOf("1" to 4, "2" to 2), store.tables().map { it.name to it.seats })
        assertTrue(store.tables().all { it.areaId == hall })
        assertEquals(TestCatalog.selling, store.sellingRules.first())
    }

    @Test
    fun changes_ReplaceTheChangedItems_AndTheSellingRules() = runBlocking {
        val store = open()
        store.apply(BUSINESS, TestCatalog.full())
        val tipsOff = SellingRules(false, BigDecimal("12.5"), null, tipsEnabled = false, tipPresets = emptyList())

        store.apply(
            BUSINESS,
            TestCatalog.full(revision = 120).copy(
                full = false,
                taxProfiles = emptyList(),
                categories = emptyList(),
                products = emptyList(),
                discounts = emptyList(),
                paymentTypes = listOf(TestCatalog.paymentType(card, "Card", PaymentKind.ExternalCard, 0), TestCatalog.paymentType(cash, "Cash", PaymentKind.Cash, 1)),
                tableAreas = emptyList(),
                tables = listOf(TestCatalog.table(2, seats = 6, active = false), TestCatalog.table(3)),
                selling = tipsOff,
            ),
        )

        assertEquals(listOf("Card", "Cash"), store.activePaymentTypes().first().map { it.name })
        assertEquals(3, store.paymentTypes().size)
        assertEquals(listOf(Triple("1", 4, true), Triple("2", 6, false), Triple("3", 4, true)), store.tables().map { Triple(it.name, it.seats, it.active) })
        assertEquals(2, store.tableAreas().size)
        assertEquals(tipsOff, store.sellingRules.first())
        assertEquals(120L, store.status.first()?.revision)
    }

    @Test
    fun aFullDownload_ReplacesTheSettingsBefore() = runBlocking {
        val store = open()
        store.apply(BUSINESS, TestCatalog.full())

        store.apply(
            BUSINESS,
            TestCatalog.full(revision = 200, paymentTypes = listOf(TestCatalog.paymentType(cash, "Cash", PaymentKind.Cash, 0)), tableAreas = emptyList(), tables = emptyList()),
        )

        assertEquals(listOf("Cash"), store.paymentTypes().map { it.name })
        assertTrue(store.tableAreas().isEmpty())
        assertTrue(store.tables().isEmpty())
    }

    @Test
    fun erasing_RemovesTheSettingsToo() = runBlocking {
        val store = open()
        store.apply(BUSINESS, TestCatalog.full())

        store.erase()

        assertTrue(store.paymentTypes().isEmpty())
        assertTrue(store.tables().isEmpty())
        assertNull(store.sellingRules.first())
    }

    /** A till updated from a version 1 copy (feature #13) drops it, so its next refresh asks for everything. */
    @Test
    fun aVersion1Copy_IsDroppedOnUpgrade_SoTheNextRefreshIsAFullDownload() = runBlocking {
        createVersion1Copy()

        val store = open()

        assertNull(store.revisionFor(BUSINESS))
        assertNull(store.businessId())
        assertTrue(store.paymentTypes().isEmpty())
        store.apply(BUSINESS, TestCatalog.full())
        assertEquals(listOf("Cash", "Card (terminal)"), store.activePaymentTypes().first().map { it.name })
    }

    /** The production builder (destructive fallback included) on a file in the test's private storage. */
    private fun open(): CatalogStore {
        val opened = CatalogDatabase.builder(context, NAME).allowMainThreadQueries().build()
        database = opened
        return CatalogStore(opened, clock)
    }

    /** Builds the file a version 1 till left behind, from the committed schema, with a copy at revision 100. */
    private fun createVersion1Copy() {
        val schema = Json.parseToJsonElement(File(SCHEMA_V1).readText()).jsonObject["database"]!!.jsonObject
        val path = context.getDatabasePath(NAME)
        path.parentFile?.mkdirs()
        SQLiteDatabase.openOrCreateDatabase(path, null).use { db ->
            schema["entities"]!!.jsonArray.forEach { entity ->
                val table = entity.jsonObject["tableName"]!!.jsonPrimitive.content
                db.execSQL(entity.jsonObject["createSql"]!!.jsonPrimitive.content.replace("\${TABLE_NAME}", table))
            }
            schema["setupQueries"]!!.jsonArray.forEach { db.execSQL(it.jsonPrimitive.content) }
            db.execSQL(
                "INSERT INTO catalog_meta (id, businessId, revision, refreshedAtMillis, serverOffsetMillis, currency, pricingMode, timeZone, " +
                    "cashRoundingStep, depositsTaxable) VALUES (0, '$BUSINESS', 100, 0, 0, 'CAD', 'TaxExclusive', 'America/Toronto', 5, 0)",
            )
            db.version = 1
        }
    }

    private companion object {
        const val NAME = "settings-test.db"
        const val SCHEMA_V1 = "schemas/com.recbook.hellopos.data.catalog.CatalogDatabase/1.json"
    }
}
