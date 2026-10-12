package com.recbook.hellopos.core.session

import okhttp3.Interceptor
import okhttp3.Response

/**
 * Adds the right `Authorization` header (research R1, R2): the device credential for the calls the
 * device makes on its own (sign-in, refresh, device info), the person's access token for everything
 * else while someone is signed in, nothing for pairing and health. Never logs the header.
 */
class AuthInterceptor(private val devices: DeviceStore, private val sessions: SessionStore) : Interceptor {

    override fun intercept(chain: Interceptor.Chain): Response {
        val request = chain.request()
        if (request.header(AUTHORIZATION) != null) return chain.proceed(request)
        val path = request.url.encodedPath
        val value = when {
            DEVICE_PATHS.any(path::endsWith) -> devices.registration.value?.let { "$DEVICE_SCHEME ${it.credential}" }
            ANONYMOUS_PATHS.any(path::endsWith) -> null
            else -> sessions.session.value?.accessToken?.takeIf(String::isNotEmpty)?.let { "$BEARER_SCHEME $it" }
        }
        return chain.proceed(if (value == null) request else request.newBuilder().header(AUTHORIZATION, value).build())
    }

    companion object {
        const val AUTHORIZATION = "Authorization"
        const val DEVICE_SCHEME = "Device"
        const val BEARER_SCHEME = "Bearer"
        private val DEVICE_PATHS =
            listOf("/pos/sign-in", "/pos/refresh", "/pos/device", "/pos/offline-access", "/pos/sessions/offline", "/pos/attempts")
        private val ANONYMOUS_PATHS = listOf("/pos/pair", "/health")
    }
}
