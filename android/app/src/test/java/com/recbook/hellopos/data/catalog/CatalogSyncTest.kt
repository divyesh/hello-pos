package com.recbook.hellopos.data.catalog

import android.app.Application
import androidx.room.Room
import com.recbook.hellopos.core.network.NetworkModule
import com.recbook.hellopos.core.security.EncryptedFileStore
import com.recbook.hellopos.core.security.SoftwareKeys
import com.recbook.hellopos.core.session.AuthInterceptor
import com.recbook.hellopos.core.session.DeviceRegistration
import com.recbook.hellopos.core.session.DeviceStore
import com.recbook.hellopos.core.session.SessionStore
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.async
import kotlinx.coroutines.cancel
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import mockwebserver3.Dispatcher
import mockwebserver3.MockResponse
import mockwebserver3.MockWebServer
import mockwebserver3.RecordedRequest
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.RuntimeEnvironment
import org.robolectric.annotation.Config
import java.io.File
import java.time.Clock
import java.time.Instant
import java.time.ZoneOffset
import java.util.UUID
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicInteger

/** Feature #13 US1: downloading the catalog from the API into the store (FR-001, FR-005, FR-008). */
@RunWith(RobolectricTestRunner::class)
@Config(sdk = [35], application = Application::class)
class CatalogSyncTest {

    @get:Rule
    val dir = TemporaryFolder()

    private val server = MockWebServer()
    private val requests = AtomicInteger()
    private val authorizations = mutableListOf<String?>()
    private val targets = mutableListOf<String>()

    /** What the server answers with 200; the Ontario catalog unless a test sets it. */
    @Volatile private var body: String? = null

    @Volatile private var status = 200

    @Volatile private var gate: CountDownLatch? = null

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val context = RuntimeEnvironment.getApplication()
    private val database = Room.inMemoryDatabaseBuilder(context, CatalogDatabase::class.java).allowMainThreadQueries().build()
    private val store = CatalogStore(database, Clock.fixed(Instant.parse("2026-10-12T16:00:00Z"), ZoneOffset.UTC))
    private lateinit var devices: DeviceStore

    @Before
    fun setUp() {
        val files = EncryptedFileStore(dir.root, SoftwareKeys())
        devices = DeviceStore(files)
        devices.save(DeviceRegistration("d1", "dev_cred", TestCatalog.BUSINESS, "Patel Grocers", "Front till"))
        server.dispatcher = object : Dispatcher() {
            override fun dispatch(request: RecordedRequest): MockResponse {
                requests.incrementAndGet()
                synchronized(authorizations) { authorizations += request.headers["Authorization"] }
                synchronized(targets) { targets += request.url.encodedPath + (request.url.encodedQuery?.let { "?$it" } ?: "") }
                gate?.await(10, TimeUnit.SECONDS)
                return MockResponse.Builder().code(status).addHeader("Content-Type", "application/json")
                    .body(if (status == 200) body ?: ONTARIO else """{"title":"Server error","status":$status}""").build()
            }
        }
        server.start()
    }

    @After
    fun tearDown() {
        scope.cancel()
        server.close()
        database.close()
    }

    @Test
    fun theFirstDownload_StoresTheBusinessCatalog_AsTheDevice() = runBlocking {
        val outcome = sync().refresh()

        assertEquals(RefreshOutcome.Updated, outcome)
        assertEquals(listOf("Device dev_cred"), authorizations)
        assertEquals(TestCatalog.BUSINESS, store.businessId())
        assertEquals(listOf("Drinks", "Snacks", "Produce", "Services"), store.activeCategories().first().map { it.name })
        val drinks = store.activeProducts(UUID.fromString("00000000-0000-4000-8000-000000000201")).first()
        assertEquals(listOf("Cola 355 ml", "Tea", "Water 500 ml"), drinks.map { it.name })
        assertEquals(listOf("0067000004568"), drinks.first().barcodes)
        // Feature #12: the business settings come with the catalog.
        assertEquals(listOf("Cash", "Card (terminal)"), store.activePaymentTypes().first().map { it.name })
        assertEquals(listOf("Cash", "Card (terminal)", "Cheque"), store.paymentTypes().map { it.name })
        val rules = store.sellingRules.first()!!
        assertEquals(listOf("10", "15", "20"), rules.tipPresets.map { it.toPlainString() })
        assertEquals(2000L, rules.discountLimitAmount)
    }

    @Test
    fun noConnection_KeepsNoCopy_AndSaysWhy() = runBlocking {
        server.close()

        val outcome = sync().refresh()

        assertTrue(outcome is RefreshOutcome.Failed)
        assertNull(store.status.first())
    }

    @Test
    fun aRefusedRefresh_KeepsThePreviousCopy() = runBlocking {
        sync().refresh()
        val before = store.status.first()
        status = 500

        val outcome = sync().refresh()

        assertTrue(outcome is RefreshOutcome.Failed)
        assertEquals(before, store.status.first())
    }

    @Test
    fun twoTriggersAtOnce_MakeOneRequest() = runBlocking {
        val latch = CountDownLatch(1)
        gate = latch
        val sync = sync()

        val first = async(Dispatchers.IO) { sync.refresh() }
        val second = async(Dispatchers.IO) { sync.refresh() }
        while (requests.get() == 0) Thread.sleep(10)
        Thread.sleep(100)
        latch.countDown()

        assertEquals(RefreshOutcome.Updated, first.await())
        assertEquals(RefreshOutcome.Updated, second.await())
        assertEquals(1, requests.get())
    }

    @Test
    fun theNextRefresh_AsksOnlyForChangesSinceTheCopy() = runBlocking {
        sync().refresh()
        body = """{"mode":"changes","revision":7,"serverTime":"2026-10-12T16:05:00Z","business":$BUSINESS_SETTINGS,""" +
            """"taxProfiles":[],"categories":[],"products":[],"discounts":[],"removedDiscountIds":["00000000-0000-4000-8000-000000000402"],""" +
            """"paymentTypes":[{"id":"00000000-0000-4000-8000-000000000902","name":"Card","kind":"externalCard","builtIn":false,""" +
            """"opensDrawer":false,"asksReference":false,"givesChange":false,"position":1,"active":false}],"tableAreas":[],"tables":[],""" +
            """"selling":{"usesTables":false,"discountLimitPercent":"5","tipsEnabled":false,"tipPresets":["10"]}}"""

        assertEquals(RefreshOutcome.Updated, sync().refresh())

        assertEquals(listOf("/pos/catalog", "/pos/catalog?since=1"), targets)
        assertEquals(7L, store.status.first()?.revision)
        assertTrue(database.catalog().discounts().none { it.id == "00000000-0000-4000-8000-000000000402" })
        assertEquals(7, database.catalog().products().size)
        assertEquals(listOf("Cash"), store.activePaymentTypes().first().map { it.name })
        assertEquals(listOf("Cash", "Card", "Cheque"), store.paymentTypes().map { it.name })
        val rules = store.sellingRules.first()!!
        assertEquals("5", rules.discountLimitPercent.toPlainString())
        assertNull(rules.discountLimitAmount)
        // Tips off: no presets, whatever the server sent.
        assertTrue(rules.tipPresets.isEmpty())
    }

    private fun sync(): CatalogSync {
        val client = NetworkModule.deviceClient(AuthInterceptor(devices, SessionStore(EncryptedFileStore(dir.root, SoftwareKeys()))))
        val api = NetworkModule.posApi(NetworkModule.retrofit(server.url("/").toString(), client))
        return CatalogSync(api, store, devices, scope)
    }

    private companion object {
        /** The Ontario catalog of the shared catalog pricing cases, exactly as the server sends it. */
        val ONTARIO: String by lazy {
            val cases = Json.parseToJsonElement(
                File(requireNotNull(System.getProperty("hellopos.contractsDir")), "catalog-pricing-cases.json").readText(),
            )
            cases.jsonObject["catalogs"]!!.jsonArray.first().jsonObject["catalog"].toString()
        }

        val BUSINESS_SETTINGS: String by lazy { Json.parseToJsonElement(ONTARIO).jsonObject["business"].toString() }
    }
}
