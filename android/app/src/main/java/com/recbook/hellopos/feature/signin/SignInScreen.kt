package com.recbook.hellopos.feature.signin

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.AssistChip
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import androidx.hilt.lifecycle.viewmodel.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.recbook.hellopos.core.designsystem.HelloPosTheme
import com.recbook.hellopos.core.designsystem.PinPad

@Composable
fun SignInScreen(viewModel: SignInViewModel = hiltViewModel()) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    SignInContent(
        state = state,
        onUsernameChange = viewModel::onUsernameChange,
        onDigit = viewModel::onDigit,
        onBackspace = viewModel::onBackspace,
        onClear = viewModel::onClear,
        onSubmit = viewModel::submit,
    )
}

@Composable
fun SignInContent(
    state: SignInUiState,
    onUsernameChange: (String) -> Unit,
    onDigit: (Char) -> Unit,
    onBackspace: () -> Unit,
    onClear: () -> Unit,
    onSubmit: () -> Unit,
    modifier: Modifier = Modifier,
) {
    Column(
        modifier = modifier.fillMaxSize().padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp, Alignment.CenterVertically),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Text("Sign in", style = MaterialTheme.typography.headlineMedium)
        if (state.recentUsernames.isNotEmpty()) {
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                state.recentUsernames.forEach { name -> AssistChip(onClick = { onUsernameChange(name) }, label = { Text(name) }) }
            }
        }
        OutlinedTextField(
            value = state.username,
            onValueChange = onUsernameChange,
            label = { Text("Username") },
            singleLine = true,
            keyboardOptions = KeyboardOptions(capitalization = KeyboardCapitalization.None, autoCorrectEnabled = false),
            modifier = Modifier.widthIn(max = 320.dp),
        )
        Text(
            if (state.pinLength == 0) "Enter your PIN" else "●".repeat(state.pinLength),
            style = MaterialTheme.typography.headlineSmall,
        )
        state.error?.let { Text(it, color = MaterialTheme.colorScheme.error, modifier = Modifier.widthIn(max = 480.dp)) }
        PinPad(onDigit = onDigit, onBackspace = onBackspace, onClear = onClear, enabled = !state.busy)
        if (state.busy) {
            CircularProgressIndicator()
        } else {
            Button(onClick = onSubmit, enabled = state.canSubmit, modifier = Modifier.widthIn(min = 240.dp)) { Text("Sign in") }
        }
    }
}

@Preview(widthDp = 960, heightDp = 720)
@Composable
private fun SignInContentPreview() {
    HelloPosTheme {
        SignInContent(SignInUiState(username = "ravi", pinLength = 3, recentUsernames = listOf("ravi", "mina")), {}, {}, {}, {}, {})
    }
}
