package com.recbook.hellopos.core.designsystem

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.size
import androidx.compose.material3.FilledTonalButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp

/** A large-key PIN pad for a shared till; the system keyboard (and its suggestions) never sees the PIN. */
@Composable
fun PinPad(onDigit: (Char) -> Unit, onBackspace: () -> Unit, onClear: () -> Unit, enabled: Boolean) {
    val rows = listOf("123", "456", "789")
    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        rows.forEach { row ->
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                row.forEach { digit -> PadKey(digit.toString(), enabled) { onDigit(digit) } }
            }
        }
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            PadKey("Clear", enabled, onClear)
            PadKey("0", enabled) { onDigit('0') }
            PadKey("⌫", enabled, onBackspace)
        }
    }
}

@Composable
private fun PadKey(label: String, enabled: Boolean, onClick: () -> Unit) {
    FilledTonalButton(onClick = onClick, enabled = enabled, modifier = Modifier.size(width = 88.dp, height = 64.dp)) {
        Text(label, style = MaterialTheme.typography.titleLarge)
    }
}
