package com.recbook.hellopos.data.auth

import com.recbook.hellopos.core.network.ApiResult
import com.recbook.hellopos.core.network.apiCall
import com.recbook.hellopos.core.security.KeyProvider
import com.recbook.hellopos.core.session.DeviceRegistration
import com.recbook.hellopos.core.session.DeviceStore
import com.recbook.hellopos.core.session.SessionStore
import com.recbook.hellopos.data.catalog.CatalogStore
import com.recbook.hellopos.data.remote.generated.apis.PosApi
import com.recbook.hellopos.data.remote.generated.models.PosPairRequest
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.withContext
import java.util.Base64
import javax.inject.Inject
import javax.inject.Singleton

sealed interface PairOutcome {
    data class Paired(val registration: DeviceRegistration) : PairOutcome

    /** Wrong, used or expired code: the API does not say which (FR-002). */
    data class Refused(val message: String) : PairOutcome

    data object Offline : PairOutcome
}

/** Device registration (feature 007, US1). */
interface DeviceRepository {
    val registration: StateFlow<DeviceRegistration?>

    suspend fun pair(code: String, deviceName: String): PairOutcome

    /** Checks the registration with the API; forgets it when the device was revoked. */
    suspend fun verify()

    /** Forgets the registration and everything held for it (session, offline bundle, pending reports). */
    suspend fun forget()
}

@Singleton
class DefaultDeviceRepository @Inject constructor(
    private val api: PosApi,
    private val devices: DeviceStore,
    private val sessions: SessionStore,
    private val keys: KeyProvider,
    private val offline: OfflineAccessRepository,
    private val pending: PendingAttempts,
    private val catalog: CatalogStore,
) : DeviceRepository {

    override val registration: StateFlow<DeviceRegistration?> = devices.registration

    override suspend fun pair(code: String, deviceName: String): PairOutcome {
        val publicKey = withContext(Dispatchers.IO) { Base64.getEncoder().encodeToString(keys.devicePublicKey().encoded) }
        return when (val result = apiCall { api.posPair(PosPairRequest(code, deviceName, publicKey)) }) {
            is ApiResult.Ok -> {
                val paired = result.value
                val registration = DeviceRegistration(
                    deviceId = paired.deviceId.toString(),
                    credential = paired.deviceCredential,
                    businessId = paired.businessId.toString(),
                    businessName = paired.businessName,
                    deviceName = deviceName,
                )
                withContext(Dispatchers.IO) { devices.save(registration) }
                PairOutcome.Paired(registration)
            }

            is ApiResult.Problem -> PairOutcome.Refused(
                if (result.status == 429) {
                    "Too many tries. Wait a minute and try again."
                } else {
                    result.title ?: "This code was not accepted. Ask for a new code in the back office."
                },
            )

            is ApiResult.Offline -> PairOutcome.Offline
        }
    }

    override suspend fun verify() {
        if (devices.registration.value == null) return
        val result = apiCall { api.posDevice() }
        if (result is ApiResult.Problem && result.status == 401) forget()
    }

    override suspend fun forget() = withContext(Dispatchers.IO) {
        sessions.clear()
        offline.clear()
        pending.drop(PendingAttempts.MAX)
        devices.clear()
        catalog.erase()
    }
}
