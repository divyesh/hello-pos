package com.recbook.hellopos

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.Surface
import androidx.compose.ui.Modifier
import com.recbook.hellopos.core.designsystem.HelloPosTheme
import com.recbook.hellopos.feature.start.StartScreen
import dagger.hilt.android.AndroidEntryPoint

@AndroidEntryPoint
class MainActivity : ComponentActivity() {

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContent {
            HelloPosTheme {
                Surface(modifier = Modifier.fillMaxSize()) {
                    StartScreen()
                }
            }
        }
    }
}
