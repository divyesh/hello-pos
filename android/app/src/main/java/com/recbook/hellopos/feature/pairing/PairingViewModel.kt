package com.recbook.hellopos.feature.pairing

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.recbook.hellopos.data.auth.DeviceRepository
import com.recbook.hellopos.data.auth.PairOutcome
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import javax.inject.Inject

data class PairingUiState(
    val code: String = "",
    val deviceName: String = "",
    val busy: Boolean = false,
    val error: String? = null,
    val pairedWith: String? = null,
) {
    val canSubmit: Boolean get() = !busy && code.length == CODE_LENGTH_WITH_DASH && deviceName.isNotBlank()

    companion object {
        const val CODE_LENGTH_WITH_DASH = 9
        const val MAX_DEVICE_NAME = 60
    }
}

/** Registering this device with the code from the back office (feature 007, US1). */
@HiltViewModel
class PairingViewModel @Inject constructor(private val devices: DeviceRepository) : ViewModel() {

    private val _state = MutableStateFlow(PairingUiState())
    val state: StateFlow<PairingUiState> = _state.asStateFlow()

    fun onCodeChange(input: String) = _state.update { it.copy(code = formatCode(input), error = null) }

    fun onDeviceNameChange(input: String) =
        _state.update { it.copy(deviceName = input.take(PairingUiState.MAX_DEVICE_NAME), error = null) }

    fun submit() {
        val current = _state.value
        if (!current.canSubmit) return
        _state.update { it.copy(busy = true, error = null) }
        viewModelScope.launch {
            val outcome = devices.pair(current.code, current.deviceName.trim())
            _state.update {
                when (outcome) {
                    is PairOutcome.Paired -> it.copy(busy = false, pairedWith = outcome.registration.businessName)
                    is PairOutcome.Refused -> it.copy(busy = false, error = outcome.message)
                    PairOutcome.Offline -> it.copy(busy = false, error = OFFLINE_MESSAGE)
                }
            }
        }
    }

    companion object {
        const val OFFLINE_MESSAGE = "No connection to Hello POS. Check the network and try again."

        /** Code characters the back office issues (no 0, 1, I, L, O); the dash is added after four. */
        private const val ALPHABET = "23456789ABCDEFGHJKMNPQRSTUVWXYZ"

        /** Upper-cases, drops anything not in the code alphabet, keeps 8 characters, shows `ABCD-EFGH`. */
        fun formatCode(input: String): String {
            val chars = input.uppercase().filter { it in ALPHABET }.take(8)
            return if (chars.length > 4) "${chars.take(4)}-${chars.drop(4)}" else chars
        }
    }
}
