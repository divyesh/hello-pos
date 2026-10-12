package com.recbook.hellopos

import android.os.Bundle
import android.view.WindowManager
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.viewModels
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.Surface
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.rememberNavController
import com.recbook.hellopos.core.designsystem.HelloPosTheme
import com.recbook.hellopos.core.session.IdleTracker
import com.recbook.hellopos.feature.catalog.CatalogScreen
import com.recbook.hellopos.feature.changepin.ChangePinScreen
import com.recbook.hellopos.feature.home.HomeScreen
import com.recbook.hellopos.feature.lock.LockScreen
import com.recbook.hellopos.feature.pairing.PairingScreen
import com.recbook.hellopos.feature.signin.SignInScreen
import com.recbook.hellopos.feature.start.StartScreen
import dagger.hilt.android.AndroidEntryPoint
import javax.inject.Inject

@AndroidEntryPoint
class MainActivity : ComponentActivity() {

    private val app: AppViewModel by viewModels()

    @Inject
    lateinit var idle: IdleTracker

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        // PINs, codes and names on screen: no screenshots, blank in recent apps (research R5).
        window.setFlags(WindowManager.LayoutParams.FLAG_SECURE, WindowManager.LayoutParams.FLAG_SECURE)
        enableEdgeToEdge()
        setContent {
            HelloPosTheme {
                Surface(modifier = Modifier.fillMaxSize()) {
                    AppNavigation(app)
                }
            }
        }
    }

    // Any touch, key press or trackball event counts as activity for the idle lock (clarification Q3).
    override fun onUserInteraction() {
        super.onUserInteraction()
        idle.onInteraction()
    }
}

@Composable
private fun AppNavigation(app: AppViewModel) {
    val destination by app.destination.collectAsStateWithLifecycle()
    val navController = rememberNavController()
    val start = remember { destination.route }

    NavHost(navController = navController, startDestination = start) {
        composable(Destination.Pairing.route) { PairingScreen(onCheckConnection = { navController.navigate(START) }) }
        composable(Destination.SignIn.route) { SignInScreen() }
        composable(Destination.Home.route) { HomeScreen(onChangePin = { navController.navigate(CHANGE_PIN) }, onCatalog = { navController.navigate(CATALOG) }) }
        composable(Destination.Lock.route) { LockScreen() }
        composable(CHANGE_PIN) { ChangePinScreen(onBack = { navController.popBackStack() }) }
        composable(CATALOG) { CatalogScreen(onBack = { navController.popBackStack() }) }
        composable(START) { StartScreen() }
    }

    // Registration and session decide the screen; there is no way "back" past them.
    LaunchedEffect(destination) {
        val current = navController.currentDestination?.route
        val onHomeSubpage = destination == Destination.Home && current in HOME_SUBPAGES
        if (current != destination.route && !onHomeSubpage) {
            navController.navigate(destination.route) { popUpTo(navController.graph.id) { inclusive = true } }
        }
    }
}

private const val START = "start"
private const val CHANGE_PIN = "changepin"
private const val CATALOG = "catalog"
private val HOME_SUBPAGES = setOf(CHANGE_PIN, CATALOG)
