package com.recbook.hellopos.core.security

import android.content.Context
import com.recbook.hellopos.core.session.DeviceStore
import com.recbook.hellopos.core.session.IdleTracker
import com.recbook.hellopos.core.session.SessionStore
import dagger.Module
import dagger.Provides
import dagger.hilt.InstallIn
import dagger.hilt.android.qualifiers.ApplicationContext
import dagger.hilt.components.SingletonComponent
import java.io.File
import java.time.Clock
import javax.inject.Singleton

/** Keys, encrypted storage and the stores built on them (research R5). */
@Module
@InstallIn(SingletonComponent::class)
object SecurityModule {

    @Provides
    @Singleton
    fun provideKeys(@ApplicationContext context: Context): KeyProvider = KeystoreKeys(context)

    /** In no-backup storage: these files are useless elsewhere (their key never leaves this device). */
    @Provides
    @Singleton
    fun provideFiles(@ApplicationContext context: Context, keys: KeyProvider): EncryptedFileStore =
        EncryptedFileStore(File(context.noBackupFilesDir, "secure"), keys)

    @Provides
    @Singleton
    fun provideDeviceStore(files: EncryptedFileStore): DeviceStore = DeviceStore(files)

    @Provides
    @Singleton
    fun provideSessionStore(files: EncryptedFileStore): SessionStore = SessionStore(files)

    @Provides
    @Singleton
    fun provideClock(): Clock = Clock.systemUTC()

    @Provides
    @Singleton
    fun provideIdleTracker(clock: Clock): IdleTracker = IdleTracker(clock)
}
