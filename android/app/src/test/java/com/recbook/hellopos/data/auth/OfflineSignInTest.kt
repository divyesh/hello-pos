package com.recbook.hellopos.data.auth

import com.recbook.hellopos.core.network.NetworkModule
import com.recbook.hellopos.core.security.EncryptedFileStore
import com.recbook.hellopos.core.security.SoftwareKeys
import com.recbook.hellopos.core.session.AuthInterceptor
import com.recbook.hellopos.core.session.DeviceStore
import com.recbook.hellopos.core.session.SessionStore
import com.recbook.hellopos.feature.pairing.FakeDeviceRepository
import com.recbook.hellopos.feature.signin.MutableClock
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.advanceTimeBy
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import mockwebserver3.Dispatcher
import mockwebserver3.MockResponse
import mockwebserver3.MockWebServer
import mockwebserver3.RecordedRequest
import org.junit.jupiter.api.AfterEach
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertInstanceOf
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.BeforeEach
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.io.TempDir
import java.io.File
import java.nio.ByteBuffer
import java.security.PublicKey
import java.security.SecureRandom
import java.time.Instant
import java.util.Base64
import javax.crypto.Cipher
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec
import kotlin.time.Duration.Companion.minutes

/**
 * Clarification Q1 / research R4 on the device: the bundle is opened with the device key and checked
 * offline with the shared PIN-hash vectors; offline sign-ins and attempts are sent once online.
 */
@OptIn(ExperimentalCoroutinesApi::class)
class OfflineSignInTest {

    @TempDir
    lateinit var dir: File

    private val online = MockWebServer()
    private val unreachable = MockWebServer()
    private val keys = SoftwareKeys()
    private val clock = MutableClock(Instant.parse("2026-10-11T09:00:00Z"))
    private lateinit var files: EncryptedFileStore
    private lateinit var sessions: SessionStore
    private val requests = mutableListOf<Pair<String, String>>()

    @Volatile private var bundleKey: PublicKey? = null

    @Volatile private var offlineAccessStatus = 200

    @Volatile private var offlineSessionStatus = 200

    private val vector = LENIENT.decodeFromString<Vectors>(
        File(requireNotNull(System.getProperty("hellopos.contractsDir")), "pin-hash-cases.json").readText(),
    ).cases.first()

    @BeforeEach
    fun setUp() {
        files = EncryptedFileStore(dir, keys)
        sessions = SessionStore(files)
        DeviceStore(files).save(com.recbook.hellopos.core.session.DeviceRegistration("d1", "dev_cred", "b1", "Patel Grocers", "Front till"))
        online.dispatcher = object : Dispatcher() {
            override fun dispatch(request: RecordedRequest): MockResponse = respond(request)
        }
        online.start()
        unreachable.start()
        unreachable.close()
    }

    @AfterEach
    fun tearDown() = online.close()

    @Test
    fun `a fetched bundle checks PINs offline`() = runTest {
        val repository = offlineAccess(online)

        assertTrue(repository.refresh())

        assertEquals(STAFF, repository.check(" RAVI ", vector.pin)?.staffId)
        vector.wrongPins.forEach { assertNull(repository.check("ravi", it)) }
        assertNull(repository.check("nobody", vector.pin))
        assertEquals(true, repository.checkById(STAFF, vector.pin))
        assertEquals(false, repository.checkById(STAFF, vector.wrongPins.first()))
    }

    @Test
    fun `a failed refresh keeps the last good bundle`() = runTest {
        val repository = offlineAccess(online)
        repository.refresh()

        offlineAccessStatus = 500
        assertFalse(repository.refresh())
        assertFalse(offlineAccess(unreachable).refresh())

        assertEquals(STAFF, repository.check("ravi", vector.pin)?.staffId)
    }

    @Test
    fun `a bundle sealed to another device's key is not accepted`() = runTest {
        bundleKey = SoftwareKeys().devicePublicKey()

        assertFalse(offlineAccess(online).refresh())
        assertFalse(offlineAccess(online).available())
    }

    @Test
    fun `refreshes run now and every 15 minutes`() = runTest {
        var runs = 0
        val job = launch { repeatEvery(OfflineAccessRepository.REFRESH_PERIOD) { runs++ } }
        runCurrent()
        assertEquals(1, runs)
        advanceTimeBy(15.minutes)
        runCurrent()
        assertEquals(2, runs)
        advanceTimeBy(30.minutes)
        runCurrent()
        assertEquals(4, runs)
        job.cancel()
    }

    @Test
    fun `offline, the right PIN signs in on the device and the wrong one is refused and remembered`() = runTest {
        offlineAccess(online).refresh()
        val repository = sessionRepository(unreachable)

        assertEquals(SignInOutcome.Refused, repository.signIn("ravi", vector.wrongPins.first()))
        val signedIn = assertInstanceOf(SignInOutcome.SignedIn::class.java, repository.signIn("ravi", vector.pin))

        assertTrue(signedIn.session.offline)
        assertEquals("", signedIn.session.accessToken)
        assertEquals("Ravi", signedIn.session.displayName)
        assertEquals("Patel Grocers", signedIn.session.businessName)
        assertEquals(listOf(false, true), PendingAttempts(files).all().map { it.success })
    }

    @Test
    fun `offline without a bundle says offline`() = runTest {
        assertEquals(SignInOutcome.Offline, sessionRepository(unreachable).signIn("ravi", vector.pin))
    }

    @Test
    fun `once online, attempts are reported and the offline sign-in becomes a session`() = runTest {
        offlineAccess(online).refresh()
        sessionRepository(unreachable).signIn("ravi", vector.pin)

        sessionRepository(online).sync()

        val session = sessions.session.value!!
        assertFalse(session.offline)
        assertEquals("pat_9", session.accessToken)
        assertTrue(PendingAttempts(files).all().isEmpty())
        val sent = requests.toMap()
        assertTrue(sent.getValue("/pos/attempts").contains("\"username\":\"ravi\""))
        assertTrue(sent.getValue("/pos/sessions/offline").contains("\"pinChangedAt\":\"2026-10-01T08:00:00Z\""))
    }

    @Test
    fun `once online, a refused offline sign-in signs the person out`() = runTest {
        offlineAccess(online).refresh()
        sessionRepository(unreachable).signIn("ravi", vector.pin)
        offlineSessionStatus = 401

        sessionRepository(online).sync()

        assertNull(sessions.session.value)
    }

    private fun respond(request: RecordedRequest): MockResponse {
        val path = request.url.encodedPath
        synchronized(requests) { requests += path to (request.body?.utf8() ?: "") }
        return when (path) {
            "/pos/offline-access" -> if (offlineAccessStatus == 200) json(200, offlineAccessBody()) else json(offlineAccessStatus, "{}")
            "/pos/attempts" -> MockResponse.Builder().code(204).build()
            "/pos/sessions/offline" -> if (offlineSessionStatus == 200) json(200, TOKENS) else json(offlineSessionStatus, "{}")
            else -> json(404, "{}")
        }
    }

    private fun offlineAccessBody(): String {
        val plaintext = """{"businessId":"b1","generatedAt":"2026-10-11T09:00:00Z","staff":[""" +
            """{"staffId":"$STAFF","username":"ravi","displayName":"Ravi","role":"cashier",""" +
            """"pinHash":"${vector.hash}","pinChangedAt":"2026-10-01T08:00:00Z"}]}"""
        val bundle = seal(plaintext.toByteArray(), bundleKey ?: keys.devicePublicKey())
        return """{"version":"v1","generatedAt":"2026-10-11T09:00:00Z","bundle":"${Base64.getEncoder().encodeToString(bundle)}"}"""
    }

    private fun api(server: MockWebServer) = NetworkModule.posApi(
        NetworkModule.retrofit(server.url("/").toString(), NetworkModule.deviceClient(AuthInterceptor(DeviceStore(files), sessions))),
    )

    private fun offlineAccess(server: MockWebServer) = OfflineAccessRepository(api(server), files, keys)

    private fun sessionRepository(server: MockWebServer) = DefaultSessionRepository(
        api(server), sessions, FakeDeviceRepository().apply { register() }, offlineAccess(server), PendingAttempts(files), clock,
    )

    private fun json(code: Int, body: String) =
        MockResponse.Builder().code(code).addHeader("Content-Type", "application/json").body(body).build()

    @Serializable
    private data class Vector(val pin: String, val hash: String, val wrongPins: List<String>)

    @Serializable
    private data class Vectors(val cases: List<Vector>)

    private companion object {
        const val STAFF = "0199d1c4-8000-7000-8000-000000000001"
        val LENIENT = Json { ignoreUnknownKeys = true }
        const val TOKENS = """{"accessToken":"pat_9","accessExpiresAt":"2026-10-11T09:15:00Z",""" +
            """"refreshToken":"prt_9","sessionExpiresAt":"2026-10-11T21:00:00Z"}"""

        /** The API's format: [2-byte length][RSA-OAEP-SHA1 wrapped key][12-byte nonce][AES-GCM ciphertext + tag]. */
        fun seal(plaintext: ByteArray, publicKey: PublicKey): ByteArray {
            val random = SecureRandom()
            val key = ByteArray(32).also(random::nextBytes)
            val nonce = ByteArray(12).also(random::nextBytes)
            val wrapped = Cipher.getInstance("RSA/ECB/OAEPWithSHA-1AndMGF1Padding")
                .apply { init(Cipher.ENCRYPT_MODE, publicKey) }.doFinal(key)
            val sealed = Cipher.getInstance("AES/GCM/NoPadding")
                .apply { init(Cipher.ENCRYPT_MODE, SecretKeySpec(key, "AES"), GCMParameterSpec(128, nonce)) }.doFinal(plaintext)
            return ByteBuffer.allocate(2 + wrapped.size + nonce.size + sealed.size)
                .putShort(wrapped.size.toShort()).put(wrapped).put(nonce).put(sealed).array()
        }
    }
}
