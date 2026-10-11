package com.recbook.hellopos.feature.home

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.recbook.hellopos.core.session.Session
import com.recbook.hellopos.data.auth.SessionRepository
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch
import javax.inject.Inject

data class HomeUiState(val displayName: String, val role: String, val businessName: String, val offline: Boolean = false)

/** Placeholder home until the selling screen (#14): who is signed in, and sign out. */
@HiltViewModel
class HomeViewModel @Inject constructor(private val sessions: SessionRepository) : ViewModel() {

    val state: StateFlow<HomeUiState?> = sessions.session
        .map { it?.toUi() }
        .stateIn(viewModelScope, SharingStarted.Eagerly, sessions.session.value?.toUi())

    fun signOut() {
        viewModelScope.launch { sessions.signOut() }
    }

    private fun Session.toUi() = HomeUiState(displayName, role.replaceFirstChar(Char::uppercase), businessName, offline)
}
