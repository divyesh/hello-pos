package com.recbook.hellopos.data.catalog

import com.recbook.hellopos.core.network.ApiResult
import com.recbook.hellopos.core.network.apiCall
import com.recbook.hellopos.core.session.DeviceStore
import com.recbook.hellopos.data.remote.generated.apis.PosApi
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Deferred
import kotlinx.coroutines.async
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlin.coroutines.cancellation.CancellationException

/** The result of one refresh; a failure always leaves the previous copy in use (FR-008). */
sealed interface RefreshOutcome {
    data object Updated : RefreshOutcome

    data class Failed(val reason: String) : RefreshOutcome
}

/**
 * Downloads the catalog and applies it (research R2, R10). Triggers that arrive while a refresh runs
 * share it instead of starting another request. Runs in [scope] so leaving a screen does not cut a
 * refresh short.
 */
class CatalogSync(
    private val api: PosApi,
    private val store: CatalogStore,
    private val devices: DeviceStore,
    private val scope: CoroutineScope,
) {
    private val lock = Mutex()
    private var running: Deferred<RefreshOutcome>? = null

    suspend fun refresh(): RefreshOutcome {
        val job = lock.withLock {
            running?.takeIf { it.isActive } ?: scope.async { download() }.also { running = it }
        }
        return job.await()
    }

    private suspend fun download(): RefreshOutcome {
        val registration = devices.registration.value ?: return RefreshOutcome.Failed("This till is not paired.")
        val since = store.revisionFor(registration.businessId)
        return when (val result = apiCall { api.posCatalog(since) }) {
            is ApiResult.Ok -> try {
                store.apply(registration.businessId, CatalogMapping.toDownload(result.value))
                RefreshOutcome.Updated
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                // Never the body: only what kind of failure it was.
                RefreshOutcome.Failed("The catalog could not be saved (${e.javaClass.simpleName}).")
            }
            is ApiResult.Problem -> RefreshOutcome.Failed("The server refused the catalog download (${result.status}).")
            is ApiResult.Offline -> RefreshOutcome.Failed("No connection to the server.")
        }
    }
}
