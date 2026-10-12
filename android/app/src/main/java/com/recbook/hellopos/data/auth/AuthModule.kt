package com.recbook.hellopos.data.auth

import com.recbook.hellopos.core.security.EncryptedFileStore
import dagger.Binds
import dagger.Module
import dagger.Provides
import dagger.hilt.InstallIn
import dagger.hilt.components.SingletonComponent
import java.time.Clock
import javax.inject.Singleton

@Module
@InstallIn(SingletonComponent::class)
abstract class AuthModule {

    @Binds
    abstract fun bindDeviceRepository(repository: DefaultDeviceRepository): DeviceRepository

    @Binds
    abstract fun bindSessionRepository(repository: DefaultSessionRepository): SessionRepository

    companion object {
        @Provides
        @Singleton
        fun provideSignInMemory(files: EncryptedFileStore, clock: Clock): SignInMemory = SignInMemory(files, clock)

        @Provides
        @Singleton
        fun providePendingAttempts(files: EncryptedFileStore): PendingAttempts = PendingAttempts(files)
    }
}
