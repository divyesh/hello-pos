package com.recbook.hellopos

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.recbook.hellopos.core.session.IdleTracker
import com.recbook.hellopos.data.auth.DeviceRepository
import com.recbook.hellopos.data.auth.OfflineAccessRepository
import com.recbook.hellopos.data.auth.SessionRepository
import com.recbook.hellopos.data.auth.repeatEvery
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch
import javax.inject.Inject
import kotlin.time.Duration.Companion.seconds

/** Where the app belongs: not registered → pairing; registered → sign-in; signed in → home, or the lock when idle. */
enum class Destination(val route: String) { Pairing("pairing"), SignIn("signin"), Home("home"), Lock("lock") }

@HiltViewModel
class AppViewModel @Inject constructor(
    private val devices: DeviceRepository,
    private val sessions: SessionRepository,
    private val offline: OfflineAccessRepository,
    private val idle: IdleTracker,
) : ViewModel() {

    val destination: StateFlow<Destination> =
        combine(devices.registration, sessions.session, idle.locked) { device, session, locked ->
            destinationOf(device != null, session != null, locked)
        }.stateIn(
            viewModelScope,
            SharingStarted.Eagerly,
            destinationOf(devices.registration.value != null, sessions.session.value != null, idle.locked.value),
        )

    init {
        // A revoked device learns it here and goes back to pairing (FR-003).
        viewModelScope.launch { devices.verify() }
        viewModelScope.launch { whileRegistered { repeatEvery(OfflineAccessRepository.REFRESH_PERIOD) { offline.refresh() } } }
        viewModelScope.launch { whileRegistered { repeatEvery(SYNC_PERIOD) { sessions.sync() } } }
        viewModelScope.launch {
            sessions.session.map { it != null }.distinctUntilChanged().collectLatest { signedIn ->
                idle.unlock() // a new person (or nobody) starts unlocked
                if (signedIn) repeatEvery(IDLE_CHECK_PERIOD) { idle.check() }
            }
        }
    }

    private suspend fun whileRegistered(block: suspend () -> Unit) =
        devices.registration.map { it != null }.distinctUntilChanged().collectLatest { registered -> if (registered) block() }

    private fun destinationOf(registered: Boolean, signedIn: Boolean, locked: Boolean) = when {
        !registered -> Destination.Pairing
        !signedIn -> Destination.SignIn
        locked -> Destination.Lock
        else -> Destination.Home
    }

    private companion object {
        val SYNC_PERIOD = 30.seconds
        val IDLE_CHECK_PERIOD = 5.seconds
    }
}
