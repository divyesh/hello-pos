package com.recbook.hellopos.data.repository

import com.recbook.hellopos.data.remote.generated.apis.SystemApi
import com.recbook.hellopos.domain.model.HealthCheck
import com.recbook.hellopos.domain.model.HealthStatus
import kotlinx.serialization.SerializationException
import java.io.IOException
import javax.inject.Inject
import javax.inject.Singleton
import kotlin.coroutines.cancellation.CancellationException
import com.recbook.hellopos.data.remote.generated.models.HealthStatus as HealthStatusDto

/**
 * Maps `GET /health` to a [HealthCheck]. Each failure becomes a typed [HealthCheck.Unavailable]
 * carrying its cause, never a swallowed error (Constitution, Principle IX).
 */
@Singleton
class HealthRepository @Inject constructor(private val api: SystemApi) {

    suspend fun check(): HealthCheck =
        try {
            val response = api.getHealth()
            val body = response.body()
            if (response.isSuccessful && body != null) {
                HealthCheck.Available(body.toDomain())
            } else {
                HealthCheck.Unavailable("HTTP ${response.code()}")
            }
        } catch (e: CancellationException) {
            throw e
        } catch (e: IOException) {
            // Connection refused, DNS failure, or the call timeout (InterruptedIOException).
            HealthCheck.Unavailable("network error: ${e.javaClass.simpleName}")
        } catch (e: SerializationException) {
            HealthCheck.Unavailable("invalid response: ${e.message}")
        }

    private fun HealthStatusDto.toDomain() = HealthStatus(
        service = service,
        version = version,
        status = status.value,
        serverTimeUtc = serverTimeUtc,
    )
}
