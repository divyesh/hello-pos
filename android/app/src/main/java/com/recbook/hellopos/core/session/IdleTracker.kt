package com.recbook.hellopos.core.session

import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import java.time.Clock
import java.time.Duration
import java.time.Instant

/**
 * Locks the till after [timeout] without a touch or key press (clarification Q3, research R6). The
 * server session continues; the lock only asks the same person for their PIN again.
 */
class IdleTracker(private val clock: Clock, private val timeout: Duration = DEFAULT_TIMEOUT) {

    private var lastInteraction: Instant = clock.instant()
    private val _locked = MutableStateFlow(false)
    val locked: StateFlow<Boolean> = _locked.asStateFlow()

    @Synchronized
    fun onInteraction() {
        if (!_locked.value) lastInteraction = clock.instant()
    }

    /** Called regularly; locks once the idle time has reached [timeout]. */
    @Synchronized
    fun check() {
        if (!_locked.value && Duration.between(lastInteraction, clock.instant()) >= timeout) _locked.value = true
    }

    @Synchronized
    fun unlock() {
        lastInteraction = clock.instant()
        _locked.value = false
    }

    companion object {
        val DEFAULT_TIMEOUT: Duration = Duration.ofMinutes(5)
    }
}
