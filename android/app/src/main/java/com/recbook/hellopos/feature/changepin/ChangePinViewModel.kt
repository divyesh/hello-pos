package com.recbook.hellopos.feature.changepin

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.recbook.hellopos.data.auth.ChangePinOutcome
import com.recbook.hellopos.data.auth.SessionRepository
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import javax.inject.Inject

data class ChangePinUiState(
    val currentPin: String = "",
    val newPin: String = "",
    val confirmPin: String = "",
    val busy: Boolean = false,
    val errors: Map<String, String> = emptyMap(),
    val message: String? = null,
    val done: Boolean = false,
) {
    override fun toString() = "ChangePinUiState(busy=$busy, done=$done)"
}

/**
 * A person changes their own PIN by giving the current one (FR-010, research R7). The API applies the
 * full PIN rules; this checks only what can be checked here. Needs a connection.
 */
@HiltViewModel
class ChangePinViewModel @Inject constructor(private val sessions: SessionRepository) : ViewModel() {

    private val _state = MutableStateFlow(ChangePinUiState())
    val state: StateFlow<ChangePinUiState> = _state.asStateFlow()

    fun onCurrentChange(value: String) = _state.update { it.copy(currentPin = digits(value), errors = it.errors - CURRENT, message = null) }

    fun onNewChange(value: String) = _state.update { it.copy(newPin = digits(value), errors = it.errors - NEW, message = null) }

    fun onConfirmChange(value: String) = _state.update { it.copy(confirmPin = digits(value), errors = it.errors - CONFIRM, message = null) }

    fun submit() {
        val current = _state.value
        if (current.busy) return
        val errors = buildMap {
            if (current.currentPin.length < MIN) put(CURRENT, "Enter your current PIN.")
            when {
                current.newPin.length < MIN -> put(NEW, "Use 4 to 6 digits.")
                current.newPin == current.currentPin -> put(NEW, "Choose a PIN different from the current one.")
            }
            if (current.confirmPin != current.newPin) put(CONFIRM, "The two new PINs do not match.")
        }
        if (errors.isNotEmpty()) {
            _state.update { it.copy(errors = errors) }
            return
        }
        _state.update { it.copy(busy = true, errors = emptyMap(), message = null) }
        viewModelScope.launch {
            val outcome = sessions.changePin(current.currentPin, current.newPin)
            _state.update {
                when (outcome) {
                    ChangePinOutcome.Changed -> ChangePinUiState(done = true, message = "Your PIN was changed.")
                    is ChangePinOutcome.Invalid -> it.copy(busy = false, currentPin = "", errors = outcome.errors)
                    ChangePinOutcome.NeedsConnection -> it.copy(busy = false, message = NEEDS_CONNECTION)
                    ChangePinOutcome.SessionEnded -> it.copy(busy = false, message = "Your session has ended. Sign in again.")
                }
            }
        }
    }

    private fun digits(value: String) = value.filter(Char::isDigit).take(MAX)

    companion object {
        const val CURRENT = "currentPin"
        const val NEW = "newPin"
        const val CONFIRM = "confirmPin"
        const val NEEDS_CONNECTION = "Changing your PIN needs a connection to Hello POS. Try again when online."
        private const val MIN = 4
        private const val MAX = 6
    }
}
