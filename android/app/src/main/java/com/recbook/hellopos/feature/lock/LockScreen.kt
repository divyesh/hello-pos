package com.recbook.hellopos.feature.lock

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import androidx.hilt.lifecycle.viewmodel.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.recbook.hellopos.core.designsystem.HelloPosTheme
import com.recbook.hellopos.core.designsystem.PinPad

@Composable
fun LockScreen(viewModel: LockViewModel = hiltViewModel()) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    LockContent(state, viewModel::onDigit, viewModel::onBackspace, viewModel::onClear, viewModel::submit, viewModel::notYou)
}

@Composable
fun LockContent(
    state: LockUiState,
    onDigit: (Char) -> Unit,
    onBackspace: () -> Unit,
    onClear: () -> Unit,
    onSubmit: () -> Unit,
    onNotYou: () -> Unit,
    modifier: Modifier = Modifier,
) {
    Column(
        modifier = modifier.fillMaxSize().padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp, Alignment.CenterVertically),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Text("Locked", style = MaterialTheme.typography.titleMedium)
        Text(state.displayName, style = MaterialTheme.typography.headlineMedium)
        Text(
            if (state.pinLength == 0) "Enter your PIN to continue" else "●".repeat(state.pinLength),
            style = MaterialTheme.typography.headlineSmall,
        )
        state.error?.let { Text(it, color = MaterialTheme.colorScheme.error, modifier = Modifier.widthIn(max = 480.dp)) }
        PinPad(onDigit = onDigit, onBackspace = onBackspace, onClear = onClear, enabled = !state.busy)
        if (state.busy) {
            CircularProgressIndicator()
        } else {
            Button(onClick = onSubmit, enabled = state.canSubmit, modifier = Modifier.widthIn(min = 240.dp)) { Text("Unlock") }
        }
        TextButton(onClick = onNotYou) { Text("Not you? Sign out") }
    }
}

@Preview(widthDp = 960, heightDp = 720)
@Composable
private fun LockContentPreview() {
    HelloPosTheme { LockContent(LockUiState(displayName = "Ravi", pinLength = 2), {}, {}, {}, {}, {}) }
}
