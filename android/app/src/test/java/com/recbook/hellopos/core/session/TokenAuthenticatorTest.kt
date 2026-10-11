package com.recbook.hellopos.core.session

import com.recbook.hellopos.core.network.NetworkModule
import com.recbook.hellopos.core.security.EncryptedFileStore
import com.recbook.hellopos.core.security.SoftwareKeys
import mockwebserver3.Dispatcher
import mockwebserver3.MockResponse
import mockwebserver3.MockWebServer
import mockwebserver3.RecordedRequest
import okhttp3.OkHttpClient
import okhttp3.Request
import org.junit.jupiter.api.AfterEach
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.BeforeEach
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.io.TempDir
import java.io.File
import java.util.concurrent.Callable
import java.util.concurrent.CountDownLatch
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicInteger

/**
 * Research R2 on the device, through the real HTTP stack: the interceptor adds the right header, an
 * expired access token is refreshed once and the call retried, a refused refresh signs out, and
 * concurrent 401s share one refresh (a second refresh would reuse the rotated token and end the session).
 */
class TokenAuthenticatorTest {

    @TempDir
    lateinit var dir: File

    private val server = MockWebServer()
    private val refreshes = AtomicInteger()
    private val seenOnMe = mutableListOf<String?>()
    private lateinit var sessions: SessionStore
    private lateinit var devices: DeviceStore

    /** Access tokens the fake API accepts; a refresh with [refreshToken] issues "pat_2"/"prt_2". */
    @Volatile private var validAccess = "pat_1"

    @Volatile private var refreshAccepted = true

    @BeforeEach
    fun setUp() {
        val files = EncryptedFileStore(dir, SoftwareKeys())
        sessions = SessionStore(files)
        devices = DeviceStore(files)
        devices.save(DeviceRegistration("d1", "dev_cred", "b1", "Patel Grocers", "Front till"))
        sessions.save(Session("pat_1", "prt_1", "2026-10-11T21:00:00Z", "s1", "Ravi", "cashier", "Patel Grocers"))
        server.dispatcher = object : Dispatcher() {
            override fun dispatch(request: RecordedRequest): MockResponse = respond(request)
        }
        server.start()
    }

    @AfterEach
    fun tearDown() = server.close()

    @Test
    fun `calls carry the access token, device calls the device credential, pairing nothing`() {
        val client = client()
        call(client, "/pos/me")
        call(client, "/pos/device")
        call(client, "/pos/pair", post = true)

        val requests = (1..3).map { server.takeRequest() }
        assertEquals("Bearer pat_1", requests[0].headers["Authorization"])
        assertEquals("Device dev_cred", requests[1].headers["Authorization"])
        assertNull(requests[2].headers["Authorization"])
    }

    @Test
    fun `a 401 refreshes once and retries with the new token`() {
        validAccess = "pat_2"

        assertEquals(200, call(client(), "/pos/me"))

        assertEquals(1, refreshes.get())
        assertEquals(listOf("Bearer pat_1", "Bearer pat_2"), synchronized(seenOnMe) { seenOnMe.toList() })
        assertEquals("prt_2", sessions.session.value?.refreshToken)
    }

    @Test
    fun `a refused refresh signs out on the device`() {
        validAccess = "pat_2"
        refreshAccepted = false

        assertEquals(401, call(client(), "/pos/me"))

        assertNull(sessions.session.value)
    }

    @Test
    fun `concurrent 401s refresh only once`() {
        validAccess = "pat_2"
        val client = client()
        val start = CountDownLatch(1)
        val pool = Executors.newFixedThreadPool(4)
        try {
            val results = (1..4).map { pool.submit(Callable { start.await(); call(client, "/pos/me") }) }
            start.countDown()

            assertEquals(listOf(200, 200, 200, 200), results.map { it.get(10, TimeUnit.SECONDS) })
        } finally {
            pool.shutdownNow()
        }
        assertEquals(1, refreshes.get())
    }

    private fun respond(request: RecordedRequest): MockResponse {
        val auth = request.headers["Authorization"]
        return when (request.url.encodedPath) {
            "/pos/me" -> {
                synchronized(seenOnMe) { seenOnMe += auth }
                if (auth == "Bearer $validAccess") json(200, ME) else json(401, "{}")
            }

            "/pos/refresh" -> {
                refreshes.incrementAndGet()
                Thread.sleep(100) // widens the window in which the concurrent calls see their 401s
                when {
                    auth != "Device dev_cred" -> json(401, "{}")
                    refreshAccepted -> json(200, TOKENS)
                    else -> json(401, """{"title":"The session has ended. Sign in again."}""")
                }
            }

            else -> json(200, "{}")
        }
    }

    private fun client(): OkHttpClient {
        val base = server.url("/").toString()
        val interceptor = AuthInterceptor(devices, sessions)
        val deviceClient = NetworkModule.deviceClient(interceptor)
        val refresher = ApiTokenRefresher(NetworkModule.posApi(NetworkModule.retrofit(base, deviceClient)))
        return NetworkModule.sessionClient(deviceClient, TokenAuthenticator(sessions, refresher))
    }

    private fun call(client: OkHttpClient, path: String, post: Boolean = false): Int {
        val builder = Request.Builder().url(server.url(path))
        if (post) builder.post(okhttp3.RequestBody.EMPTY)
        return client.newCall(builder.build()).execute().use { it.code }
    }

    private fun json(code: Int, body: String) =
        MockResponse.Builder().code(code).addHeader("Content-Type", "application/json").body(body).build()

    private companion object {
        const val ME = """{"staffId":"0199d1c4-8000-7000-8000-000000000001","displayName":"Ravi","role":"cashier",""" +
            """"businessId":"0199d1c4-8000-7000-8000-000000000002","businessName":"Patel Grocers"}"""
        const val TOKENS = """{"accessToken":"pat_2","accessExpiresAt":"2026-10-11T09:15:00Z",""" +
            """"refreshToken":"prt_2","sessionExpiresAt":"2026-10-11T21:00:00Z"}"""
    }
}
