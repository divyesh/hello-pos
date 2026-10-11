package com.recbook.hellopos.feature.changepin

import com.recbook.hellopos.data.auth.ChangePinOutcome
import com.recbook.hellopos.feature.signin.FakeSessionRepository
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.test.UnconfinedTestDispatcher
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.setMain
import org.junit.jupiter.api.AfterEach
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.BeforeEach
import org.junit.jupiter.api.Test

/** FR-010 / research R7 on the device: own PIN change. */
@OptIn(ExperimentalCoroutinesApi::class)
class ChangePinViewModelTest {

    private val sessions = FakeSessionRepository()

    @BeforeEach
    fun setUp() = Dispatchers.setMain(UnconfinedTestDispatcher())

    @AfterEach
    fun tearDown() = Dispatchers.resetMain()

    @Test
    fun `local checks before sending`() {
        val viewModel = filled("48", "5938", "5939")

        viewModel.submit()

        assertEquals(setOf(ChangePinViewModel.CURRENT, ChangePinViewModel.CONFIRM), viewModel.state.value.errors.keys)
        assertTrue(sessions.changes.isEmpty())

        viewModel.onCurrentChange("4826")
        viewModel.onNewChange("4826")
        viewModel.onConfirmChange("4826")
        viewModel.submit()
        assertEquals("Choose a PIN different from the current one.", viewModel.state.value.errors[ChangePinViewModel.NEW])
    }

    @Test
    fun `only digits, at most six`() {
        val viewModel = ChangePinViewModel(sessions)
        viewModel.onNewChange("12a34-5678")
        assertEquals("123456", viewModel.state.value.newPin)
    }

    @Test
    fun `success sends both PINs and finishes`() {
        val viewModel = filled("4826", "5938", "5938")

        viewModel.submit()

        assertEquals(listOf("4826" to "5938"), sessions.changes)
        assertTrue(viewModel.state.value.done)
        assertEquals("", viewModel.state.value.newPin)
    }

    @Test
    fun `the API's field errors are shown and the current PIN cleared`() {
        sessions.nextChange = ChangePinOutcome.Invalid(mapOf("currentPin" to "The current PIN is not correct."))
        val viewModel = filled("4826", "5938", "5938")

        viewModel.submit()

        assertEquals("The current PIN is not correct.", viewModel.state.value.errors[ChangePinViewModel.CURRENT])
        assertEquals("", viewModel.state.value.currentPin)
        assertFalse(viewModel.state.value.done)
    }

    @Test
    fun `offline it needs a connection`() {
        sessions.nextChange = ChangePinOutcome.NeedsConnection
        val viewModel = filled("4826", "5938", "5938")

        viewModel.submit()

        assertEquals(ChangePinViewModel.NEEDS_CONNECTION, viewModel.state.value.message)
    }

    @Test
    fun `PINs never appear in text`() {
        val viewModel = filled("4826", "5938", "5938")
        assertFalse(viewModel.state.value.toString().contains("4826"))
    }

    private fun filled(current: String, new: String, confirm: String) = ChangePinViewModel(sessions).apply {
        onCurrentChange(current)
        onNewChange(new)
        onConfirmChange(confirm)
    }
}
