package com.recbook.hellopos.feature.start

import app.cash.turbine.ReceiveTurbine
import app.cash.turbine.test
import com.recbook.hellopos.core.network.NetworkModule
import com.recbook.hellopos.data.repository.HealthRepository
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.test.UnconfinedTestDispatcher
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.test.setMain
import mockwebserver3.MockResponse
import mockwebserver3.MockWebServer
import org.junit.jupiter.api.AfterEach
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertInstanceOf
import org.junit.jupiter.api.BeforeEach
import org.junit.jupiter.api.Test
import java.util.concurrent.TimeUnit
import kotlin.time.Duration.Companion.seconds

/**
 * Start screen states (data-model.md): Checking -> Available or Unavailable, against a real HTTP
 * stack (generated Retrofit client + OkHttp) talking to MockWebServer. The call timeout is shortened
 * from the production 5 seconds to keep the timeout test fast; the behavior is the same.
 */
@OptIn(ExperimentalCoroutinesApi::class)
class StartViewModelTest {

    private val server = MockWebServer()

    @BeforeEach
    fun setUp() {
        Dispatchers.setMain(UnconfinedTestDispatcher())
        server.start()
    }

    @AfterEach
    fun tearDown() {
        server.close()
        Dispatchers.resetMain()
    }

    @Test
    fun `valid 200 shows available with status and version`() = runTest {
        server.enqueue(json(200, VALID_BODY, delayMillis = 300))

        newViewModel().state.test(timeout = 10.seconds) {
            assertEquals(StartUiState.Checking, awaitItem())
            assertEquals(StartUiState.Available(status = "available", version = "0.1.0-dev"), awaitSettled())
        }
    }

    @Test
    fun `server error shows unavailable`() = runTest {
        server.enqueue(json(500, """{"title":"boom"}"""))
        assertUnavailable()
    }

    @Test
    fun `malformed json shows unavailable`() = runTest {
        server.enqueue(json(200, "not json"))
        assertUnavailable()
    }

    @Test
    fun `connection refused shows unavailable`() = runTest {
        server.close()
        assertUnavailable()
    }

    @Test
    fun `response slower than the timeout shows unavailable`() = runTest {
        server.enqueue(json(200, VALID_BODY, delayMillis = 3_000))
        assertUnavailable(callTimeoutMillis = 500)
    }

    @Test
    fun `retry checks again`() = runTest {
        server.enqueue(json(500, "{}"))
        server.enqueue(json(200, VALID_BODY))

        val viewModel = newViewModel()
        viewModel.state.test(timeout = 10.seconds) {
            assertInstanceOf(StartUiState.Unavailable::class.java, awaitSettled())
            viewModel.retry()
            assertInstanceOf(StartUiState.Available::class.java, awaitSettled())
        }
        assertEquals(2, server.requestCount)
    }

    private suspend fun assertUnavailable(callTimeoutMillis: Long = 5_000) {
        newViewModel(callTimeoutMillis).state.test(timeout = 10.seconds) {
            assertInstanceOf(StartUiState.Unavailable::class.java, awaitSettled())
        }
    }

    /** Skips the transient Checking state and returns the first settled state. */
    private suspend fun ReceiveTurbine<StartUiState>.awaitSettled(): StartUiState {
        var item = awaitItem()
        while (item == StartUiState.Checking) item = awaitItem()
        return item
    }

    private fun newViewModel(callTimeoutMillis: Long = 5_000): StartViewModel {
        val baseUrl = server.url("/").toString()
        val client = NetworkModule.okHttpClient(callTimeoutMillis)
        val api = NetworkModule.systemApi(NetworkModule.retrofit(baseUrl, client))
        return StartViewModel(HealthRepository(api))
    }

    private fun json(code: Int, body: String, delayMillis: Long = 0): MockResponse =
        MockResponse.Builder()
            .code(code)
            .addHeader("Content-Type", "application/json")
            .body(body)
            .headersDelay(delayMillis, TimeUnit.MILLISECONDS)
            .build()

    private companion object {
        const val VALID_BODY =
            """{"service":"hello-pos-api","version":"0.1.0-dev","status":"available","serverTimeUtc":"2026-10-10T14:03:22Z"}"""
    }
}
