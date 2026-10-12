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
import com.recbook.hellopos.data.catalog.TestCatalog.BUSINESS
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.RuntimeEnvironment
import org.robolectric.annotation.Config
import java.time.Clock
import java.time.Instant
import java.time.ZoneOffset

/** Feature #13 US4: a till only ever holds its own business's catalog (FR-019, SC-007, Constitution III). */
@RunWith(RobolectricTestRunner::class)
@Config(sdk = [35], application = Application::class)
class CatalogEraseTest {

    @get:Rule
    val dir = TemporaryFolder()

    private val context = RuntimeEnvironment.getApplication()
    private val database = Room.databaseBuilder(context, CatalogDatabase::class.java, NAME).allowMainThreadQueries().build()
    private val store = CatalogStore(database, Clock.fixed(Instant.parse("2026-10-12T16:00:00Z"), ZoneOffset.UTC))

    @After
    fun close() {
        database.close()
        context.deleteDatabase(NAME)
    }

    @Test
    fun erasing_LeavesNoRow_AndNoTraceInTheFile() = runBlocking {
        store.apply(BUSINESS, TestCatalog.full(products = listOf(TestCatalog.product(TestCatalog.cola, "Zebracola-unique-name"))))

        store.erase()

        val dao = database.catalog()
        assertNull(dao.meta())
        assertTrue(dao.taxProfiles().isEmpty() && dao.taxVersions().isEmpty() && dao.categories().isEmpty())
        assertTrue(dao.products().isEmpty() && dao.barcodes().isEmpty() && dao.discounts().isEmpty())
        database.openHelper.writableDatabase.query("PRAGMA wal_checkpoint(TRUNCATE)").close()
        val bytes = context.getDatabasePath(NAME).readBytes().toString(Charsets.ISO_8859_1)
        assertFalse("the erased name is still in the database file", bytes.contains("Zebracola-unique-name"))
    }

    @Test
    fun aCopyOfAnotherBusiness_IsErasedBeforeAnythingIsShown() = runBlocking {
        store.apply(OTHER_BUSINESS, TestCatalog.full())
        val devices = devices(BUSINESS)

        val repository = repository(devices)

        assertNull(repository.status.first())
        assertTrue(repository.categories().first().isEmpty())
        assertNull(store.businessId())
    }

    @Test
    fun anUnpairedTill_ShowsNothing_AndKeepsNothing() = runBlocking {
        store.apply(BUSINESS, TestCatalog.full())
        val devices = devices(BUSINESS)
        val repository = repository(devices)
        assertEquals(100L, repository.status.first()?.revision)

        devices.clear()

        assertNull(repository.status.first())
        assertNull(repository.priceFeed())
        assertEquals(0, database.catalog().products().size)
    }

    @Test
    fun theOwnBusinesssCopy_IsKept() = runBlocking {
        store.apply(BUSINESS, TestCatalog.full())

        val repository = repository(devices(BUSINESS))

        assertEquals(100L, repository.status.first()?.revision)
        assertEquals(listOf("Drinks", "Snacks"), repository.categories().first().map { it.name })
    }

    private fun devices(business: String): DeviceStore {
        val store = DeviceStore(EncryptedFileStore(dir.root, SoftwareKeys()))
        store.save(DeviceRegistration("d1", "dev_cred", business, "Patel Grocers", "Front till"))
        return store
    }

    private fun repository(devices: DeviceStore): DefaultCatalogRepository {
        val client = NetworkModule.deviceClient(AuthInterceptor(devices, SessionStore(EncryptedFileStore(dir.root, SoftwareKeys()))))
        val api = NetworkModule.posApi(NetworkModule.retrofit("http://127.0.0.1:9/", client))
        return DefaultCatalogRepository(store, CatalogSync(api, store, devices, CoroutineScope(Dispatchers.IO)), devices)
    }

    private companion object {
        const val NAME = "catalog-erase-test.db"
        const val OTHER_BUSINESS = "0199d1c4-8000-7000-8000-0000000000bb"
    }
}
