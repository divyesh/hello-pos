package com.recbook.hellopos.feature.home

import com.recbook.hellopos.feature.signin.FakeSessionRepository
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.test.UnconfinedTestDispatcher
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.setMain
import org.junit.jupiter.api.AfterEach
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.BeforeEach
import org.junit.jupiter.api.Test

/** Feature 007 US2: home shows who is signed in; sign out ends the session. */
@OptIn(ExperimentalCoroutinesApi::class)
class HomeViewModelTest {

    private val sessions = FakeSessionRepository()

    @BeforeEach
    fun setUp() = Dispatchers.setMain(UnconfinedTestDispatcher())

    @AfterEach
    fun tearDown() = Dispatchers.resetMain()

    @Test
    fun `shows name, role and business`() {
        sessions.signedInAs("Ravi")

        assertEquals(HomeUiState("Ravi", "Cashier", "Patel Grocers"), HomeViewModel(sessions).state.value)
    }

    @Test
    fun `sign out clears the session`() {
        sessions.signedInAs("Ravi")
        val viewModel = HomeViewModel(sessions)

        viewModel.signOut()

        assertEquals(1, sessions.signOuts)
        assertNull(sessions.session.value)
        assertNull(viewModel.state.value)
    }
}
