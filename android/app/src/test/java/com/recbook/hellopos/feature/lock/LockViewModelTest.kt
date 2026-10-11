package com.recbook.hellopos.feature.lock

import com.recbook.hellopos.core.security.EncryptedFileStore
import com.recbook.hellopos.core.security.SoftwareKeys
import com.recbook.hellopos.core.session.IdleTracker
import com.recbook.hellopos.data.auth.PinCheck
import com.recbook.hellopos.data.auth.SignInMemory
import com.recbook.hellopos.feature.signin.FakeSessionRepository
import com.recbook.hellopos.feature.signin.MutableClock
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.test.UnconfinedTestDispatcher
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.setMain
import org.junit.jupiter.api.AfterEach
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertNotNull
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.BeforeEach
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.io.TempDir
import java.io.File
import java.time.Duration
import java.time.Instant

/** Clarification Q3 / research R6: the same person unlocks with their PIN; failures count; "Not you?" signs out. */
@OptIn(ExperimentalCoroutinesApi::class)
class LockViewModelTest {

    @TempDir
    lateinit var dir: File

    private val clock = MutableClock(Instant.parse("2026-10-11T09:00:00Z"))
    private val sessions = FakeSessionRepository()
    private val idle = IdleTracker(clock)
    private lateinit var memory: SignInMemory

    @BeforeEach
    fun setUp() {
        Dispatchers.setMain(UnconfinedTestDispatcher())
        memory = SignInMemory(EncryptedFileStore(dir, SoftwareKeys()), clock)
        sessions.signedInAs("Ravi")
        clock.now = clock.now.plus(Duration.ofMinutes(5))
        idle.check()
    }

    @AfterEach
    fun tearDown() = Dispatchers.resetMain()

    @Test
    fun `shows who is signed in, and the right PIN unlocks`() {
        val viewModel = LockViewModel(sessions, memory, idle)
        assertEquals("Ravi", viewModel.state.value.displayName)
        assertTrue(idle.locked.value)

        enter(viewModel, "4826")

        assertEquals(listOf("4826"), sessions.checked)
        assertFalse(idle.locked.value)
        assertNotNull(sessions.session.value)
    }

    @Test
    fun `a wrong PIN says so and stays locked`() {
        val viewModel = LockViewModel(sessions, memory, idle)

        enter(viewModel, "9071")

        assertEquals(LockViewModel.WRONG, viewModel.state.value.error)
        assertTrue(idle.locked.value)
        assertEquals(0, viewModel.state.value.pinLength)
    }

    @Test
    fun `five wrong PINs sign the person out`() {
        val viewModel = LockViewModel(sessions, memory, idle)

        repeat(5) { enter(viewModel, "9071") }

        assertNull(sessions.session.value)
        assertFalse(idle.locked.value)
        assertNotNull(memory.lockedUntil("ravi"))
    }

    @Test
    fun `not you signs out`() {
        LockViewModel(sessions, memory, idle).notYou()

        assertEquals(1, sessions.signOuts)
        assertNull(sessions.session.value)
        assertFalse(idle.locked.value)
    }

    @Test
    fun `when the PIN cannot be checked it says so`() {
        sessions.nextCheck = PinCheck.Unavailable
        val viewModel = LockViewModel(sessions, memory, idle)

        enter(viewModel, "4826")

        assertEquals(LockViewModel.UNAVAILABLE, viewModel.state.value.error)
        assertTrue(idle.locked.value)
    }

    private fun enter(viewModel: LockViewModel, pin: String) {
        pin.forEach(viewModel::onDigit)
        viewModel.submit()
    }
}
