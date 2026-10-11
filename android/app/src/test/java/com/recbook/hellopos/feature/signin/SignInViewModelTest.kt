package com.recbook.hellopos.feature.signin

import com.recbook.hellopos.core.security.EncryptedFileStore
import com.recbook.hellopos.core.security.SoftwareKeys
import com.recbook.hellopos.core.session.Session
import com.recbook.hellopos.data.auth.ChangePinOutcome
import com.recbook.hellopos.data.auth.PinCheck
import com.recbook.hellopos.data.auth.SessionRepository
import com.recbook.hellopos.data.auth.SignInMemory
import com.recbook.hellopos.data.auth.SignInOutcome
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.test.UnconfinedTestDispatcher
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.setMain
import org.junit.jupiter.api.AfterEach
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.BeforeEach
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.io.TempDir
import java.io.File
import java.time.Clock
import java.time.Duration
import java.time.Instant
import java.time.ZoneOffset

/** Feature 007 US2 on the device: PIN pad, generic errors, local lockout, remembered usernames. */
@OptIn(ExperimentalCoroutinesApi::class)
class SignInViewModelTest {

    @TempDir
    lateinit var dir: File

    private val sessions = FakeSessionRepository()
    private val clock = MutableClock(Instant.parse("2026-10-11T09:00:00Z"))
    private val keys = SoftwareKeys()
    private lateinit var memory: SignInMemory

    @BeforeEach
    fun setUp() {
        Dispatchers.setMain(UnconfinedTestDispatcher())
        memory = SignInMemory(EncryptedFileStore(dir, keys), clock)
    }

    @AfterEach
    fun tearDown() = Dispatchers.resetMain()

    @Test
    fun `the PIN pad takes 4 to 6 digits and only digits`() {
        val viewModel = newViewModel()
        viewModel.onUsernameChange("ravi")
        "482".forEach(viewModel::onDigit)
        assertFalse(viewModel.state.value.canSubmit)

        "6x9012".forEach(viewModel::onDigit)
        assertEquals(6, viewModel.state.value.pinLength)
        assertTrue(viewModel.state.value.canSubmit)

        viewModel.onBackspace()
        assertEquals(5, viewModel.state.value.pinLength)
        viewModel.onClear()
        assertEquals(0, viewModel.state.value.pinLength)
    }

    @Test
    fun `success signs in with what was typed and remembers the username`() {
        val viewModel = typed("ravi", "4826")

        viewModel.submit()

        assertEquals(listOf("ravi" to "4826"), sessions.attempts)
        assertTrue(viewModel.state.value.signedIn)
        assertEquals(listOf("ravi"), SignInMemory(EncryptedFileStore(dir, keys), clock).recentUsernames)
        assertEquals("ravi", newViewModel().state.value.username)
    }

    @Test
    fun `a failure shows one generic message and clears the PIN`() {
        sessions.next = SignInOutcome.Refused
        val viewModel = typed("ravi", "9071")

        viewModel.submit()

        assertEquals(SignInViewModel.WRONG, viewModel.state.value.error)
        assertEquals(0, viewModel.state.value.pinLength)
        assertFalse(viewModel.state.value.signedIn)
    }

    @Test
    fun `five failures for a username lock it here for 15 minutes`() {
        sessions.next = SignInOutcome.Refused
        val viewModel = newViewModel()
        repeat(5) { attempt(viewModel, "ravi", "9071") }

        assertEquals("Too many wrong PINs for ravi. Try again in 15 minutes or ask a manager.", viewModel.state.value.error)

        sessions.next = null
        attempt(viewModel, "ravi", "4826")
        assertEquals(5, sessions.attempts.size)
        assertTrue(viewModel.state.value.error!!.startsWith("Too many wrong PINs for ravi."))

        attempt(viewModel, "mina", "4826")
        assertTrue(viewModel.state.value.signedIn)

        clock.now = clock.now.plus(Duration.ofMinutes(15))
        attempt(newViewModel(), "RAVI", "4826")
        assertEquals("RAVI" to "4826", sessions.attempts.last())
    }

    @Test
    fun `device back-off and no connection have their own messages`() {
        sessions.next = SignInOutcome.Wait(4)
        val viewModel = typed("ravi", "4826")
        viewModel.submit()
        assertEquals("Too many tries on this device. Wait 4 seconds and try again.", viewModel.state.value.error)

        sessions.next = SignInOutcome.Offline
        attempt(viewModel, "ravi", "4826")
        assertEquals(SignInViewModel.OFFLINE, viewModel.state.value.error)
    }

    @Test
    fun `the last few people are remembered, most recent first`() {
        val viewModel = newViewModel()
        listOf("a1", "b2", "c3", "d4", "e5", "f6", "b2").forEach { attempt(viewModel, it, "4826") }

        assertEquals(listOf("b2", "f6", "e5", "d4", "c3"), newViewModel().state.value.recentUsernames)
    }

    private fun newViewModel() = SignInViewModel(sessions, SignInMemory(EncryptedFileStore(dir, keys), clock), clock)

    private fun typed(username: String, pin: String) = newViewModel().apply {
        onUsernameChange(username)
        pin.forEach(::onDigit)
    }

    private fun attempt(viewModel: SignInViewModel, username: String, pin: String) {
        viewModel.onUsernameChange(username)
        viewModel.onClear()
        pin.forEach(viewModel::onDigit)
        viewModel.submit()
    }
}

class MutableClock(var now: Instant) : Clock() {
    override fun instant(): Instant = now
    override fun getZone() = ZoneOffset.UTC
    override fun withZone(zone: java.time.ZoneId?) = this
}

class FakeSessionRepository : SessionRepository {
    val attempts = mutableListOf<Pair<String, String>>()
    var next: SignInOutcome? = null
    var signOuts = 0
    var nextCheck: PinCheck? = null
    val checked = mutableListOf<String>()
    var nextChange: ChangePinOutcome = ChangePinOutcome.Changed
    val changes = mutableListOf<Pair<String, String>>()
    var syncs = 0
    private val current = MutableStateFlow<Session?>(null)
    override val session: StateFlow<Session?> = current

    fun signedInAs(name: String, role: String = "cashier") {
        current.value = Session("pat_a", "prt_r", "2026-10-11T21:00:00Z", "s1", name, role, "Patel Grocers", username = name.lowercase())
    }

    override suspend fun signIn(username: String, pin: String): SignInOutcome {
        attempts += username to pin
        return next ?: run {
            signedInAs(username)
            SignInOutcome.SignedIn(current.value!!)
        }
    }

    override suspend fun signOut() {
        signOuts++
        current.value = null
    }

    /** Correct for "4826" unless [nextCheck] says otherwise. */
    override suspend fun checkPin(pin: String): PinCheck {
        checked += pin
        return nextCheck ?: if (pin == "4826") PinCheck.Correct else PinCheck.Wrong
    }

    override suspend fun changePin(currentPin: String, newPin: String): ChangePinOutcome {
        changes += currentPin to newPin
        return nextChange
    }

    override suspend fun sync() {
        syncs++
    }
}
