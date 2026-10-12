package com.recbook.hellopos.core.util

import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.flow
import dagger.Binds
import dagger.Module
import dagger.hilt.InstallIn
import dagger.hilt.components.SingletonComponent
import javax.inject.Inject
import kotlin.time.Duration

/** Periodic signals for screens that show elapsed time; tests replace it so no endless timer runs. */
fun interface Ticker {
    /** Emits at once, then every [period], until collection stops. */
    fun every(period: Duration): Flow<Unit>
}

class DelayTicker @Inject constructor() : Ticker {
    override fun every(period: Duration): Flow<Unit> = flow {
        while (true) {
            emit(Unit)
            delay(period)
        }
    }
}

@Module
@InstallIn(SingletonComponent::class)
abstract class UtilModule {
    @Binds
    abstract fun bindTicker(ticker: DelayTicker): Ticker
}
