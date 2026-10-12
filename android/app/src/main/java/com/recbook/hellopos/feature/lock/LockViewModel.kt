package com.recbook.hellopos.feature.lock

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.recbook.hellopos.core.session.IdleTracker
import com.recbook.hellopos.data.auth.PinCheck
import com.recbook.hellopos.data.auth.SessionRepository
import com.recbook.hellopos.data.auth.SignInMemory
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import javax.inject.Inject

data class LockUiState(
    val displayName: String = "",
    val pinLength: Int = 0,
    val busy: Boolean = false,
    val error: String? = null,
) {
    val canSubmit: Boolean get() = !busy && pinLength >= MIN_PIN

    companion object {
        const val MIN_PIN = 4
        const val MAX_PIN = 6
    }
}

/**
 * The idle lock (clarification Q3, research R6): the same person types their PIN again, checked offline
 * when possible; failures count like sign-ins and a lockout signs them out. "Not you?" signs out.
 */
@HiltViewModel
class LockViewModel @Inject constructor(
    private val sessions: SessionRepository,
    private val memory: SignInMemory,
    private val idle: IdleTracker,
) : ViewModel() {

    private var pin = ""
    private val _state = MutableStateFlow(LockUiState(displayName = sessions.session.value?.displayName.orEmpty()))
    val state: StateFlow<LockUiState> = _state.asStateFlow()

    fun onDigit(digit: Char) {
        if (!digit.isDigit() || pin.length >= LockUiState.MAX_PIN) return
        setPin(pin + digit)
    }

    fun onBackspace() = setPin(pin.dropLast(1))

    fun onClear() = setPin("")

    fun submit() {
        if (!_state.value.canSubmit) return
        val username = sessions.session.value?.username.orEmpty()
        val attempt = pin
        _state.update { it.copy(busy = true, error = null) }
        viewModelScope.launch {
            if (memory.lockedUntil(username) != null) {
                signOut()
                return@launch
            }
            val check = sessions.checkPin(attempt)
            pin = ""
            when (check) {
                PinCheck.Correct -> {
                    memory.recordSuccess(username)
                    idle.unlock()
                    _state.update { it.copy(busy = false, pinLength = 0) }
                }

                PinCheck.Wrong -> if (memory.recordFailure(username) != null) {
                    signOut()
                } else {
                    _state.update { it.copy(busy = false, pinLength = 0, error = WRONG) }
                }

                PinCheck.Unavailable -> _state.update { it.copy(busy = false, pinLength = 0, error = UNAVAILABLE) }
            }
        }
    }

    fun notYou() {
        viewModelScope.launch { signOut() }
    }

    private suspend fun signOut() {
        sessions.signOut()
        idle.unlock()
    }

    private fun setPin(value: String) {
        pin = value
        _state.update { it.copy(pinLength = value.length, error = null) }
    }

    companion object {
        const val WRONG = "That PIN is not correct."
        const val UNAVAILABLE = "The PIN cannot be checked right now. Connect to the network, or choose \"Not you?\"."
    }
}
