package com.recbook.hellopos.data.auth

import com.recbook.hellopos.core.network.ApiResult
import com.recbook.hellopos.core.network.apiCall
import com.recbook.hellopos.core.network.apiCallNoContent
import com.recbook.hellopos.core.session.Session
import com.recbook.hellopos.core.session.SessionStore
import com.recbook.hellopos.data.remote.generated.apis.PosApi
import com.recbook.hellopos.data.remote.generated.models.PosAttempt
import com.recbook.hellopos.data.remote.generated.models.PosAttemptReport
import com.recbook.hellopos.data.remote.generated.models.PosChangePin
import com.recbook.hellopos.data.remote.generated.models.PosOfflineSession
import com.recbook.hellopos.data.remote.generated.models.PosSignInRequest
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.withContext
import java.time.Clock
import java.time.Duration
import java.util.UUID
import javax.inject.Inject
import javax.inject.Singleton

sealed interface SignInOutcome {
    /** Signed in; [Session.offline] when checked against the offline bundle (no connection). */
    data class SignedIn(val session: Session) : SignInOutcome

    /** Wrong username or PIN, or a locked or inactive person: the API does not say which (FR-005). */
    data object Refused : SignInOutcome

    /** The device is backing off after repeated failures (FR-006). */
    data class Wait(val seconds: Long) : SignInOutcome

    /** No connection and no offline data to check against. */
    data object Offline : SignInOutcome
}

enum class PinCheck { Correct, Wrong, Unavailable }

sealed interface ChangePinOutcome {
    data object Changed : ChangePinOutcome

    data class Invalid(val errors: Map<String, String>) : ChangePinOutcome

    /** Changing the PIN changes server data, so it needs a connection (research R7). */
    data object NeedsConnection : ChangePinOutcome

    data object SessionEnded : ChangePinOutcome
}

/** PIN sign-in, the idle lock's check, own PIN change and sign-out on this device (feature 007, US2, US3). */
interface SessionRepository {
    val session: StateFlow<Session?>

    /** Online when possible; without a connection, against the offline bundle (clarification Q1). */
    suspend fun signIn(username: String, pin: String): SignInOutcome

    /** Ends the session on the server when reachable, and always on the device. */
    suspend fun signOut()

    /** Checks the signed-in person's PIN for the idle lock, offline when possible (research R6). */
    suspend fun checkPin(pin: String): PinCheck

    suspend fun changePin(currentPin: String, newPin: String): ChangePinOutcome

    /** When online: reports offline attempts and turns an offline sign-in into a session (or signs out). */
    suspend fun sync()
}

@Singleton
class DefaultSessionRepository @Inject constructor(
    private val api: PosApi,
    private val sessions: SessionStore,
    private val devices: DeviceRepository,
    private val offline: OfflineAccessRepository,
    private val pending: PendingAttempts,
    private val clock: Clock,
) : SessionRepository {

    override val session: StateFlow<Session?> = sessions.session

    override suspend fun signIn(username: String, pin: String): SignInOutcome =
        when (val result = apiCall { api.posSignIn(PosSignInRequest(username, pin)) }) {
            is ApiResult.Ok -> {
                val (tokens, staff) = result.value
                val session = Session(
                    accessToken = tokens.accessToken,
                    refreshToken = tokens.refreshToken,
                    sessionExpiresAt = tokens.sessionExpiresAt,
                    staffId = staff.staffId.toString(),
                    displayName = staff.displayName,
                    role = staff.role.value,
                    businessName = staff.businessName,
                    username = username.trim().lowercase(),
                    signedInAt = now(),
                )
                withContext(Dispatchers.IO) { sessions.save(session) }
                offline.refresh() // research R4: refreshed on every sign-in
                SignInOutcome.SignedIn(session)
            }

            is ApiResult.Problem -> when {
                result.status == 429 -> SignInOutcome.Wait(result.retryAfterSeconds ?: 1)
                result.status == 401 && result.title == null -> {
                    // No problem body: the device credential itself was refused (revoked device).
                    devices.verify()
                    SignInOutcome.Refused
                }
                else -> SignInOutcome.Refused
            }

            is ApiResult.Offline -> signInOffline(username, pin)
        }

    private suspend fun signInOffline(username: String, pin: String): SignInOutcome {
        if (!offline.available()) return SignInOutcome.Offline
        val person = offline.check(username, pin)
        record(username, success = person != null)
        if (person == null) return SignInOutcome.Refused
        val signedInAt = now()
        val session = Session(
            accessToken = "",
            refreshToken = "",
            sessionExpiresAt = clock.instant().plus(MAX_SESSION).toString(),
            staffId = person.staffId,
            displayName = person.displayName,
            role = person.role,
            businessName = devices.registration.value?.businessName.orEmpty(),
            username = person.username,
            offline = true,
            signedInAt = signedInAt,
            pinChangedAt = person.pinChangedAt,
        )
        withContext(Dispatchers.IO) { sessions.save(session) }
        return SignInOutcome.SignedIn(session)
    }

    override suspend fun signOut() {
        val current = sessions.session.value ?: return
        if (!current.offline) apiCallNoContent { api.posSignOut() }
        withContext(Dispatchers.IO) { sessions.clear() }
    }

    override suspend fun checkPin(pin: String): PinCheck {
        val current = sessions.session.value ?: return PinCheck.Wrong
        val result = offline.checkById(current.staffId, pin)
        if (result != null) {
            record(current.username, result)
            return if (result) PinCheck.Correct else PinCheck.Wrong
        }
        // No bundle yet: check online by signing in again, which also refreshes the session.
        return when (signIn(current.username, pin)) {
            is SignInOutcome.SignedIn -> PinCheck.Correct
            SignInOutcome.Refused, is SignInOutcome.Wait -> PinCheck.Wrong
            SignInOutcome.Offline -> PinCheck.Unavailable
        }
    }

    override suspend fun changePin(currentPin: String, newPin: String): ChangePinOutcome {
        val current = sessions.session.value ?: return ChangePinOutcome.SessionEnded
        if (current.offline) return ChangePinOutcome.NeedsConnection
        return when (val result = apiCall { api.posChangeOwnPin(PosChangePin(currentPin, newPin)) }) {
            is ApiResult.Ok -> {
                val tokens = result.value
                withContext(Dispatchers.IO) { sessions.updateTokens(tokens.accessToken, tokens.refreshToken, tokens.sessionExpiresAt) }
                offline.refresh() // the bundle now holds the new hash
                ChangePinOutcome.Changed
            }

            is ApiResult.Problem -> when (result.status) {
                400 -> ChangePinOutcome.Invalid(result.errors.mapValues { it.value.joinToString(" ") })
                401 -> ChangePinOutcome.SessionEnded
                else -> ChangePinOutcome.NeedsConnection
            }

            is ApiResult.Offline -> ChangePinOutcome.NeedsConnection
        }
    }

    override suspend fun sync() {
        val attempts = withContext(Dispatchers.IO) { pending.all() }
        if (attempts.isNotEmpty()) {
            val report = PosAttemptReport(attempts.map { PosAttempt(it.username, it.success, it.at) })
            if (apiCallNoContent { api.posReportAttempts(report) } is ApiResult.Ok) {
                withContext(Dispatchers.IO) { pending.drop(attempts.size) }
            }
        }

        val current = sessions.session.value?.takeIf { it.offline } ?: return
        val staffId = runCatching { UUID.fromString(current.staffId) }.getOrNull()
            ?: return withContext(Dispatchers.IO) { sessions.clear() } // damaged session: sign out rather than loop
        val request = PosOfflineSession(staffId, current.signedInAt, current.pinChangedAt)
        when (val result = apiCall { api.posSessionFromOffline(request) }) {
            is ApiResult.Ok -> withContext(Dispatchers.IO) {
                // Only if the same offline session is still the current one.
                if (sessions.session.value == current) {
                    val tokens = result.value
                    sessions.save(
                        current.copy(
                            accessToken = tokens.accessToken,
                            refreshToken = tokens.refreshToken,
                            sessionExpiresAt = tokens.sessionExpiresAt,
                            offline = false,
                        ),
                    )
                }
            }

            is ApiResult.Problem -> if (result.status == 401) {
                // Deactivated or PIN changed meanwhile: the device signs the person out (research R4).
                withContext(Dispatchers.IO) { if (sessions.session.value == current) sessions.clear() }
            }

            is ApiResult.Offline -> Unit
        }
    }

    private suspend fun record(username: String, success: Boolean) = withContext(Dispatchers.IO) {
        pending.add(OfflineAttempt(username.trim().lowercase(), success, now()))
    }

    private fun now(): String = clock.instant().toString()

    private companion object {
        val MAX_SESSION: Duration = Duration.ofHours(12)
    }
}
