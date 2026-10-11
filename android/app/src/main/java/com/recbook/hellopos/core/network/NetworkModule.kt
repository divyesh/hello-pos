package com.recbook.hellopos.core.network

import com.recbook.hellopos.BuildConfig
import com.recbook.hellopos.core.session.ApiTokenRefresher
import com.recbook.hellopos.core.session.AuthInterceptor
import com.recbook.hellopos.core.session.DeviceStore
import com.recbook.hellopos.core.session.SessionStore
import com.recbook.hellopos.core.session.TokenAuthenticator
import com.recbook.hellopos.data.remote.generated.apis.PosApi
import com.recbook.hellopos.data.remote.generated.apis.SystemApi
import com.recbook.hellopos.data.remote.generated.infrastructure.Serializer
import dagger.Module
import dagger.Provides
import dagger.hilt.InstallIn
import dagger.hilt.components.SingletonComponent
import okhttp3.Authenticator
import okhttp3.Dispatcher
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import retrofit2.Retrofit
import retrofit2.converter.kotlinx.serialization.asConverterFactory
import java.util.concurrent.TimeUnit
import javax.inject.Named
import javax.inject.Singleton

/**
 * HTTP stack for the API. The Retrofit interfaces and models are generated from
 * `contracts/openapi.yaml` (research R7); this module only wires them up.
 *
 * Two clients share one connection pool: the device client (credential header, no refresh) used for
 * token refresh, and the session client that also refreshes on 401. The session client has its own
 * dispatcher so calls waiting in the authenticator can never use up the slots the refresh needs.
 */
@Module
@InstallIn(SingletonComponent::class)
object NetworkModule {

    /** Whole-call timeout; the start screen shows "API unavailable" when it is exceeded (SC-003). */
    const val CALL_TIMEOUT_MILLIS = 5_000L

    private const val DEVICE_CLIENT = "device"

    fun okHttpClient(callTimeoutMillis: Long = CALL_TIMEOUT_MILLIS): OkHttpClient =
        OkHttpClient.Builder()
            .callTimeout(callTimeoutMillis, TimeUnit.MILLISECONDS)
            .build()

    fun deviceClient(interceptor: AuthInterceptor, callTimeoutMillis: Long = CALL_TIMEOUT_MILLIS): OkHttpClient =
        okHttpClient(callTimeoutMillis).newBuilder().addInterceptor(interceptor).build()

    fun sessionClient(deviceClient: OkHttpClient, authenticator: Authenticator): OkHttpClient =
        deviceClient.newBuilder().dispatcher(Dispatcher()).authenticator(authenticator).build()

    fun retrofit(baseUrl: String, client: OkHttpClient): Retrofit =
        Retrofit.Builder()
            .baseUrl(baseUrl)
            .client(client)
            .addConverterFactory(Serializer.kotlinxSerializationJson.asConverterFactory("application/json".toMediaType()))
            .build()

    fun systemApi(retrofit: Retrofit): SystemApi = retrofit.create(SystemApi::class.java)

    fun posApi(retrofit: Retrofit): PosApi = retrofit.create(PosApi::class.java)

    @Provides
    @Singleton
    fun provideAuthInterceptor(devices: DeviceStore, sessions: SessionStore): AuthInterceptor = AuthInterceptor(devices, sessions)

    @Provides
    @Singleton
    @Named(DEVICE_CLIENT)
    fun provideDeviceClient(interceptor: AuthInterceptor): OkHttpClient = deviceClient(interceptor)

    @Provides
    @Singleton
    @Named(DEVICE_CLIENT)
    fun provideDevicePosApi(@Named(DEVICE_CLIENT) client: OkHttpClient): PosApi =
        posApi(retrofit(BuildConfig.API_BASE_URL, client))

    @Provides
    @Singleton
    fun provideOkHttpClient(
        @Named(DEVICE_CLIENT) deviceClient: OkHttpClient,
        @Named(DEVICE_CLIENT) deviceApi: PosApi,
        sessions: SessionStore,
    ): OkHttpClient = sessionClient(deviceClient, TokenAuthenticator(sessions, ApiTokenRefresher(deviceApi)))

    @Provides
    @Singleton
    fun provideRetrofit(client: OkHttpClient): Retrofit = retrofit(BuildConfig.API_BASE_URL, client)

    @Provides
    @Singleton
    fun provideSystemApi(retrofit: Retrofit): SystemApi = systemApi(retrofit)

    @Provides
    @Singleton
    fun providePosApi(retrofit: Retrofit): PosApi = posApi(retrofit)
}
