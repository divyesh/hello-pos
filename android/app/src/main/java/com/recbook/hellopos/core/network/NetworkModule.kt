package com.recbook.hellopos.core.network

import com.recbook.hellopos.BuildConfig
import com.recbook.hellopos.data.remote.generated.apis.SystemApi
import com.recbook.hellopos.data.remote.generated.infrastructure.Serializer
import dagger.Module
import dagger.Provides
import dagger.hilt.InstallIn
import dagger.hilt.components.SingletonComponent
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import retrofit2.Retrofit
import retrofit2.converter.kotlinx.serialization.asConverterFactory
import java.util.concurrent.TimeUnit
import javax.inject.Singleton

/**
 * HTTP stack for the API. The Retrofit interfaces and models are generated from
 * `contracts/openapi.yaml` (research R7); this module only wires them up.
 */
@Module
@InstallIn(SingletonComponent::class)
object NetworkModule {

    /** Whole-call timeout; the start screen shows "API unavailable" when it is exceeded (SC-003). */
    const val CALL_TIMEOUT_MILLIS = 5_000L

    fun okHttpClient(callTimeoutMillis: Long = CALL_TIMEOUT_MILLIS): OkHttpClient =
        OkHttpClient.Builder()
            .callTimeout(callTimeoutMillis, TimeUnit.MILLISECONDS)
            .build()

    fun retrofit(baseUrl: String, client: OkHttpClient): Retrofit =
        Retrofit.Builder()
            .baseUrl(baseUrl)
            .client(client)
            .addConverterFactory(Serializer.kotlinxSerializationJson.asConverterFactory("application/json".toMediaType()))
            .build()

    fun systemApi(retrofit: Retrofit): SystemApi = retrofit.create(SystemApi::class.java)

    @Provides
    @Singleton
    fun provideOkHttpClient(): OkHttpClient = okHttpClient()

    @Provides
    @Singleton
    fun provideRetrofit(client: OkHttpClient): Retrofit = retrofit(BuildConfig.API_BASE_URL, client)

    @Provides
    @Singleton
    fun provideSystemApi(retrofit: Retrofit): SystemApi = systemApi(retrofit)
}
