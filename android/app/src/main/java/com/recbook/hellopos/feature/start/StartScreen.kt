package com.recbook.hellopos.feature.start

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
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
fun StartScreen(viewModel: StartViewModel = hiltViewModel()) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    StartContent(state = state, onRetry = viewModel::retry)
}

@Composable
fun StartContent(state: StartUiState, onRetry: () -> Unit, modifier: Modifier = Modifier) {
    Column(
        modifier = modifier.fillMaxSize().padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp, Alignment.CenterVertically),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Text("Hello POS", style = MaterialTheme.typography.headlineMedium)
        when (state) {
            StartUiState.Checking -> {
                CircularProgressIndicator()
                Text("Checking API…")
            }

            is StartUiState.Available -> {
                Text("API ${state.status}", style = MaterialTheme.typography.titleMedium)
                Text("Version ${state.version}")
            }

            is StartUiState.Unavailable -> {
                Text(
                    "API unavailable",
                    style = MaterialTheme.typography.titleMedium,
                    color = MaterialTheme.colorScheme.error,
                )
                Button(onClick = onRetry) { Text("Retry") }
            }
        }
    }
}

@Preview(widthDp = 960, heightDp = 600)
@Composable
private fun StartContentPreview() {
    HelloPosTheme { StartContent(StartUiState.Available("available", "0.1.0-dev"), onRetry = {}) }
}
