package com.recbook.hellopos.feature.home

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import androidx.hilt.lifecycle.viewmodel.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.recbook.hellopos.core.designsystem.HelloPosTheme

@Composable
fun HomeScreen(onChangePin: () -> Unit, viewModel: HomeViewModel = hiltViewModel()) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    state?.let { HomeContent(it, onSignOut = viewModel::signOut, onChangePin = onChangePin) }
}

@Composable
fun HomeContent(state: HomeUiState, onSignOut: () -> Unit, onChangePin: () -> Unit, modifier: Modifier = Modifier) {
    Column(
        modifier = modifier.fillMaxSize().padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp, Alignment.CenterVertically),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Text(state.businessName, style = MaterialTheme.typography.titleMedium)
        Text("Hello, ${state.displayName}", style = MaterialTheme.typography.headlineMedium)
        Text(state.role)
        if (state.offline) {
            Text("Signed in offline. The session is confirmed when the connection returns.", color = MaterialTheme.colorScheme.tertiary)
        }
        Text("Selling arrives in a later release.", style = MaterialTheme.typography.bodySmall)
        OutlinedButton(onClick = onChangePin) { Text("Change PIN") }
        Button(onClick = onSignOut) { Text("Sign out") }
    }
}

@Preview(widthDp = 960, heightDp = 600)
@Composable
private fun HomeContentPreview() {
    HelloPosTheme { HomeContent(HomeUiState("Ravi", "Cashier", "Patel Grocers"), {}, {}) }
}
