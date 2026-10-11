package com.recbook.hellopos.feature.changepin

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import androidx.hilt.lifecycle.viewmodel.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.recbook.hellopos.core.designsystem.HelloPosTheme

@Composable
fun ChangePinScreen(onBack: () -> Unit, viewModel: ChangePinViewModel = hiltViewModel()) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    ChangePinContent(state, viewModel::onCurrentChange, viewModel::onNewChange, viewModel::onConfirmChange, viewModel::submit, onBack)
}

@Composable
fun ChangePinContent(
    state: ChangePinUiState,
    onCurrentChange: (String) -> Unit,
    onNewChange: (String) -> Unit,
    onConfirmChange: (String) -> Unit,
    onSubmit: () -> Unit,
    onBack: () -> Unit,
    modifier: Modifier = Modifier,
) {
    Column(
        modifier = modifier.fillMaxSize().padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp, Alignment.CenterVertically),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Text("Change your PIN", style = MaterialTheme.typography.headlineMedium)
        if (state.done) {
            Text(state.message.orEmpty())
            Button(onClick = onBack) { Text("Done") }
            return@Column
        }
        PinField("Current PIN", state.currentPin, state.errors[ChangePinViewModel.CURRENT], onCurrentChange)
        PinField("New PIN (4 to 6 digits)", state.newPin, state.errors[ChangePinViewModel.NEW], onNewChange)
        PinField("New PIN again", state.confirmPin, state.errors[ChangePinViewModel.CONFIRM], onConfirmChange)
        state.message?.let { Text(it, color = MaterialTheme.colorScheme.error, modifier = Modifier.widthIn(max = 480.dp)) }
        if (state.busy) {
            CircularProgressIndicator()
        } else {
            Button(onClick = onSubmit) { Text("Change PIN") }
        }
        TextButton(onClick = onBack) { Text("Cancel") }
    }
}

@Composable
private fun PinField(label: String, value: String, error: String?, onChange: (String) -> Unit) {
    OutlinedTextField(
        value = value,
        onValueChange = onChange,
        label = { Text(label) },
        singleLine = true,
        isError = error != null,
        supportingText = error?.let { { Text(it) } },
        visualTransformation = PasswordVisualTransformation(),
        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.NumberPassword, autoCorrectEnabled = false),
        modifier = Modifier.widthIn(max = 320.dp),
    )
}

@Preview(widthDp = 960, heightDp = 720)
@Composable
private fun ChangePinContentPreview() {
    HelloPosTheme { ChangePinContent(ChangePinUiState(currentPin = "48"), {}, {}, {}, {}, {}) }
}
