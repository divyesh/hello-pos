package com.recbook.hellopos.core.session

import com.recbook.hellopos.core.network.ApiResult
import com.recbook.hellopos.core.network.apiCall
import com.recbook.hellopos.data.remote.generated.apis.PosApi
import com.recbook.hellopos.data.remote.generated.models.PosRefreshRequest
import com.recbook.hellopos.data.remote.generated.models.PosTokens
import kotlinx.coroutines.runBlocking
import okhttp3.Authenticator
import okhttp3.Request
import okhttp3.Response
import okhttp3.Route

sealed interface RefreshOutcome {
    data class Refreshed(val tokens: PosTokens) : RefreshOutcome

    /** The API refused the refresh token: the session is over (sign-out, reuse, revocation…). */
    data object Rejected : RefreshOutcome

    data object Unreachable : RefreshOutcome
}

fun interface TokenRefresher {
    fun refresh(refreshToken: String): RefreshOutcome
}

/** Refreshes through a client that sends the device credential and has no authenticator of its own. */
class ApiTokenRefresher(private val deviceApi: PosApi) : TokenRefresher {
    override fun refresh(refreshToken: String): RefreshOutcome = runBlocking {
        when (val result = apiCall { deviceApi.posRefresh(PosRefreshRequest(refreshToken)) }) {
            is ApiResult.Ok -> RefreshOutcome.Refreshed(result.value)
            is ApiResult.Problem -> if (result.status == 401) RefreshOutcome.Rejected else RefreshOutcome.Unreachable
            is ApiResult.Offline -> RefreshOutcome.Unreachable
        }
    }
}

/**
 * On a 401 for a call made with the access token, refreshes once and retries (research R2). Refresh
 * tokens rotate and a reused one ends the session, so concurrent 401s must not refresh twice: the
 * first caller refreshes under the lock, the others see the new token and just retry with it.
 * A refused refresh signs the person out on the device.
 */
class TokenAuthenticator(private val sessions: SessionStore, private val refresher: TokenRefresher) : Authenticator {

    private val lock = Any()

    override fun authenticate(route: Route?, response: Response): Request? {
        val sent = response.request.header(AuthInterceptor.AUTHORIZATION) ?: return null
        if (!sent.startsWith("${AuthInterceptor.BEARER_SCHEME} ")) return null
        if (response.priorResponse?.code == 401) return null

        synchronized(lock) {
            val session = sessions.session.value?.takeIf { it.refreshToken.isNotEmpty() } ?: return null
            if (sent != bearer(session.accessToken)) return response.request.withToken(session.accessToken)
            return when (val outcome = refresher.refresh(session.refreshToken)) {
                is RefreshOutcome.Refreshed -> {
                    val tokens = outcome.tokens
                    sessions.updateTokens(tokens.accessToken, tokens.refreshToken, tokens.sessionExpiresAt)
                    response.request.withToken(tokens.accessToken)
                }

                RefreshOutcome.Rejected -> {
                    sessions.clear()
                    null
                }

                RefreshOutcome.Unreachable -> null
            }
        }
    }

    private fun Request.withToken(token: String) = newBuilder().header(AuthInterceptor.AUTHORIZATION, bearer(token)).build()

    private fun bearer(token: String) = "${AuthInterceptor.BEARER_SCHEME} $token"
}
