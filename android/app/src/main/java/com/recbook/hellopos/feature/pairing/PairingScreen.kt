package com.recbook.hellopos.feature.pairing

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
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import androidx.hilt.lifecycle.viewmodel.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.recbook.hellopos.core.designsystem.HelloPosTheme

@Composable
fun PairingScreen(onCheckConnection: () -> Unit, viewModel: PairingViewModel = hiltViewModel()) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    PairingContent(
        state = state,
        onCodeChange = viewModel::onCodeChange,
        onDeviceNameChange = viewModel::onDeviceNameChange,
        onSubmit = viewModel::submit,
        onCheckConnection = onCheckConnection,
    )
}

@Composable
fun PairingContent(
    state: PairingUiState,
    onCodeChange: (String) -> Unit,
    onDeviceNameChange: (String) -> Unit,
    onSubmit: () -> Unit,
    onCheckConnection: () -> Unit,
    modifier: Modifier = Modifier,
) {
    Column(
        modifier = modifier.fillMaxSize().padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp, Alignment.CenterVertically),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Text("Register this device", style = MaterialTheme.typography.headlineMedium)
        Text(
            "In the Hello POS back office, open Devices and choose \"Register a device\". Type the code shown there.",
            modifier = Modifier.widthIn(max = 480.dp),
        )
        OutlinedTextField(
            value = state.code,
            onValueChange = onCodeChange,
            label = { Text("Code") },
            singleLine = true,
            textStyle = MaterialTheme.typography.headlineSmall.copy(fontFamily = FontFamily.Monospace),
            keyboardOptions = KeyboardOptions(capitalization = KeyboardCapitalization.Characters, autoCorrectEnabled = false),
            modifier = Modifier.widthIn(max = 320.dp),
        )
        OutlinedTextField(
            value = state.deviceName,
            onValueChange = onDeviceNameChange,
            label = { Text("Name for this device, e.g. Front till") },
            singleLine = true,
            modifier = Modifier.widthIn(max = 320.dp),
        )
        state.error?.let { Text(it, color = MaterialTheme.colorScheme.error, modifier = Modifier.widthIn(max = 480.dp)) }
        if (state.busy) {
            CircularProgressIndicator()
        } else {
            Button(onClick = onSubmit, enabled = state.canSubmit) { Text("Register") }
        }
        TextButton(onClick = onCheckConnection) { Text("Check connection") }
    }
}

@Preview(widthDp = 960, heightDp = 600)
@Composable
private fun PairingContentPreview() {
    HelloPosTheme {
        PairingContent(PairingUiState(code = "ABCD-EF", deviceName = "Front till"), {}, {}, {}, {})
    }
}
