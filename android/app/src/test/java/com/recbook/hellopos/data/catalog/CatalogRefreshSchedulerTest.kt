package com.recbook.hellopos.data.catalog

import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.advanceTimeBy
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Test
import kotlin.time.Duration.Companion.minutes
import kotlin.time.Duration.Companion.seconds

/** Feature #13: when the till refreshes (FR-005; US1 start and sign-in, US2 every 5 minutes in the foreground). */
@OptIn(ExperimentalCoroutinesApi::class)
class CatalogRefreshSchedulerTest {

    private val catalog = FakeCatalogRepository()
    private val paired = MutableStateFlow(false)
    private val signedIn = MutableStateFlow(false)
    private val foreground = MutableStateFlow(true)

    @Test
    fun nothingHappens_UntilTheTillIsPaired() = runTest {
        val job = start()
        assertEquals(0, catalog.refreshes)

        paired.value = true
        runCurrent()

        assertEquals(1, catalog.refreshes)
        job.cancel()
    }

    @Test
    fun eachSignIn_Refreshes() = runTest {
        paired.value = true
        val job = start()

        signedIn.value = true
        runCurrent()
        signedIn.value = false
        runCurrent()
        signedIn.value = true
        runCurrent()

        assertEquals(3, catalog.refreshes)
        job.cancel()
    }

    @Test
    fun inTheForeground_ItRefreshesEveryFiveMinutes_EvenAfterAFailure() = runTest {
        paired.value = true
        catalog.nextRefresh = RefreshOutcome.Failed("No connection to the server.")
        val job = start()
        assertEquals(1, catalog.refreshes)

        advanceTimeBy(4.minutes + 59.seconds)
        runCurrent()
        assertEquals(1, catalog.refreshes)

        advanceTimeBy(2.seconds)
        runCurrent()
        assertEquals(2, catalog.refreshes)

        advanceTimeBy(10.minutes)
        runCurrent()
        assertEquals(4, catalog.refreshes)
        job.cancel()
    }

    @Test
    fun inTheBackground_ItStops_AndRefreshesOnReturn() = runTest {
        paired.value = true
        val job = start()
        foreground.value = false
        runCurrent()

        advanceTimeBy(30.minutes)
        runCurrent()
        assertEquals(1, catalog.refreshes)

        foreground.value = true
        runCurrent()
        assertEquals(2, catalog.refreshes)
        job.cancel()
    }

    private fun TestScope.start() = launch { CatalogRefreshScheduler(catalog).run(paired, signedIn, foreground) }.also { runCurrent() }
}
