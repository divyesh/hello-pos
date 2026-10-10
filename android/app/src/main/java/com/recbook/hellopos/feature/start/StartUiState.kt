package com.recbook.hellopos.feature.start

/** Start screen states (data-model.md, "Client view state"). */
sealed interface StartUiState {
    data object Checking : StartUiState

    data class Available(val status: String, val version: String) : StartUiState

    data class Unavailable(val reason: String) : StartUiState
}
