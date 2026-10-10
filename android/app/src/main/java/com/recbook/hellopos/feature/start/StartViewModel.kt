package com.recbook.hellopos.feature.start

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.recbook.hellopos.data.repository.HealthRepository
import com.recbook.hellopos.domain.model.HealthCheck
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import javax.inject.Inject

@HiltViewModel
class StartViewModel @Inject constructor(
    private val health: HealthRepository,
) : ViewModel() {

    private val _state = MutableStateFlow<StartUiState>(StartUiState.Checking)
    val state: StateFlow<StartUiState> = _state.asStateFlow()

    init {
        check()
    }

    fun retry() = check()

    private fun check() {
        _state.value = StartUiState.Checking
        viewModelScope.launch {
            _state.value = when (val result = health.check()) {
                is HealthCheck.Available -> StartUiState.Available(result.health.status, result.health.version)
                is HealthCheck.Unavailable -> StartUiState.Unavailable(result.reason)
            }
        }
    }
}
