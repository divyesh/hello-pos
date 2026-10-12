package com.recbook.hellopos.data.catalog

import android.content.Context
import androidx.room.Room
import com.recbook.hellopos.BuildConfig
import com.recbook.hellopos.core.network.NetworkModule
import com.recbook.hellopos.core.session.AuthInterceptor
import com.recbook.hellopos.core.session.DeviceStore
import com.recbook.hellopos.data.remote.generated.apis.PosApi
import dagger.Binds
import dagger.Module
import dagger.Provides
import dagger.hilt.InstallIn
import dagger.hilt.android.qualifiers.ApplicationContext
import dagger.hilt.components.SingletonComponent
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import java.time.Clock
import javax.inject.Named
import javax.inject.Singleton

/** The catalog copy, its download client and the refresh service (feature #13). */
@Module
@InstallIn(SingletonComponent::class)
abstract class CatalogModule {

    @Binds
    abstract fun bindCatalogRepository(repository: DefaultCatalogRepository): CatalogRepository

    companion object {
        /** A full download of a large catalog takes longer than the 5-second budget of other calls (SC-001). */
        const val DOWNLOAD_TIMEOUT_MILLIS = 120_000L

        private const val CATALOG_CLIENT = "catalog"

        @Provides
        @Singleton
        fun provideDatabase(@ApplicationContext context: Context): CatalogDatabase =
            Room.databaseBuilder(context, CatalogDatabase::class.java, CatalogDatabase.NAME).build()

        @Provides
        @Singleton
        fun provideStore(database: CatalogDatabase, clock: Clock): CatalogStore = CatalogStore(database, clock)

        @Provides
        @Singleton
        @Named(CATALOG_CLIENT)
        fun provideCatalogApi(interceptor: AuthInterceptor): PosApi =
            NetworkModule.posApi(NetworkModule.retrofit(BuildConfig.API_BASE_URL, NetworkModule.deviceClient(interceptor, DOWNLOAD_TIMEOUT_MILLIS)))

        @Provides
        @Singleton
        fun provideSync(@Named(CATALOG_CLIENT) api: PosApi, store: CatalogStore, devices: DeviceStore): CatalogSync =
            CatalogSync(api, store, devices, CoroutineScope(SupervisorJob() + Dispatchers.IO))
    }
}
