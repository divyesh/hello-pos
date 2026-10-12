package com.recbook.hellopos.data.catalog

import com.recbook.hellopos.data.auth.repeatEvery
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.launch
import javax.inject.Inject
import kotlin.time.Duration.Companion.minutes

/**
 * When the till refreshes its catalog (FR-005, research R10): while it is paired and the app is in the
 * foreground, at once and then every [PERIOD]; and each time someone signs in. A failed refresh keeps the
 * copy and is simply retried at the next trigger. Concurrent triggers share one download ([CatalogSync]).
 */
class CatalogRefreshScheduler @Inject constructor(private val catalog: CatalogRepository) {

    /** Runs until cancelled. */
    suspend fun run(paired: Flow<Boolean>, signedIn: Flow<Boolean>, foreground: Flow<Boolean>) = coroutineScope {
        launch {
            combine(paired, foreground) { isPaired, visible -> isPaired && visible }
                .distinctUntilChanged()
                .collectLatest { active -> if (active) repeatEvery(PERIOD) { catalog.refresh() } }
        }
        launch { signedIn.distinctUntilChanged().collectLatest { if (it) catalog.refresh() } }
    }

    companion object {
        val PERIOD = 5.minutes
    }
}
