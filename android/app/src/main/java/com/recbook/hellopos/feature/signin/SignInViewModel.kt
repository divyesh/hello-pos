package com.recbook.hellopos.feature.signin

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.recbook.hellopos.data.auth.SessionRepository
import com.recbook.hellopos.data.auth.SignInMemory
import com.recbook.hellopos.data.auth.SignInOutcome
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import java.time.Clock
import java.time.Duration
import javax.inject.Inject

data class SignInUiState(
    val username: String = "",
    val pinLength: Int = 0,
    val recentUsernames: List<String> = emptyList(),
    val busy: Boolean = false,
    val error: String? = null,
    val signedIn: Boolean = false,
) {
    val canSubmit: Boolean get() = !busy && username.isNotBlank() && pinLength >= MIN_PIN

    companion object {
        const val MIN_PIN = 4
        const val MAX_PIN = 6
    }
}

/** POS username and PIN pad (feature 007, US2). The PIN stays in this view model only, never in state or logs. */
@HiltViewModel
class SignInViewModel @Inject constructor(
    private val sessions: SessionRepository,
    private val memory: SignInMemory,
    private val clock: Clock,
) : ViewModel() {

    private var pin = ""
    private val _state = MutableStateFlow(
        SignInUiState(username = memory.recentUsernames.firstOrNull().orEmpty(), recentUsernames = memory.recentUsernames),
    )
    val state: StateFlow<SignInUiState> = _state.asStateFlow()

    fun onUsernameChange(input: String) = _state.update { it.copy(username = input.trim().take(MAX_USERNAME), error = null) }

    fun onDigit(digit: Char) {
        if (!digit.isDigit() || pin.length >= SignInUiState.MAX_PIN) return
        setPin(pin + digit)
    }

    fun onBackspace() = setPin(pin.dropLast(1))

    fun onClear() = setPin("")

    fun submit() {
        val current = _state.value
        if (!current.canSubmit) return
        val username = current.username
        memory.lockedUntil(username)?.let { until ->
            setPin("", error = lockedMessage(username, until))
            return
        }
        val attempt = pin
        _state.update { it.copy(busy = true, error = null) }
        viewModelScope.launch {
            val outcome = sessions.signIn(username, attempt)
            pin = ""
            when (outcome) {
                is SignInOutcome.SignedIn -> {
                    memory.recordSuccess(username)
                    _state.update { it.copy(busy = false, pinLength = 0, signedIn = true, recentUsernames = memory.recentUsernames) }
                }

                SignInOutcome.Refused -> {
                    val lockedUntil = memory.recordFailure(username)
                    finish(if (lockedUntil != null) lockedMessage(username, lockedUntil) else WRONG)
                }

                is SignInOutcome.Wait -> finish("Too many tries on this device. Wait ${outcome.seconds} seconds and try again.")
                SignInOutcome.Offline -> finish(OFFLINE)
            }
        }
    }

    private fun finish(error: String) = _state.update { it.copy(busy = false, pinLength = 0, error = error) }

    private fun setPin(value: String, error: String? = null) {
        pin = value
        _state.update { it.copy(pinLength = value.length, error = error) }
    }

    private fun lockedMessage(username: String, until: java.time.Instant): String {
        val minutes = Duration.between(clock.instant(), until).seconds.let { (it + 59) / 60 }.coerceAtLeast(1)
        return "Too many wrong PINs for $username. Try again in $minutes minute${if (minutes == 1L) "" else "s"} or ask a manager."
    }

    companion object {
        const val WRONG = "Username or PIN is incorrect."
        const val OFFLINE = "No connection to Hello POS. Check the network and try again."
        private const val MAX_USERNAME = 32
    }
}
