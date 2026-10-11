package com.recbook.hellopos.feature.pairing

import com.recbook.hellopos.core.session.DeviceRegistration
import com.recbook.hellopos.data.auth.DeviceRepository
import com.recbook.hellopos.data.auth.PairOutcome
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.test.UnconfinedTestDispatcher
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.setMain
import org.junit.jupiter.api.AfterEach
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.BeforeEach
import org.junit.jupiter.api.Test

/** Feature 007 US1 on the device: typing the code and registering. */
@OptIn(ExperimentalCoroutinesApi::class)
class PairingViewModelTest {

    private val devices = FakeDeviceRepository()

    @BeforeEach
    fun setUp() = Dispatchers.setMain(UnconfinedTestDispatcher())

    @AfterEach
    fun tearDown() = Dispatchers.resetMain()

    @Test
    fun `codes are formatted as ABCD-EFGH whatever is typed`() {
        assertEquals("ABCD-EFGH", PairingViewModel.formatCode("abcdefgh"))
        assertEquals("ABCD-EFGH", PairingViewModel.formatCode(" ab cd-ef gh "))
        assertEquals("ABC", PairingViewModel.formatCode("abc"))
        assertEquals("ABCD-E", PairingViewModel.formatCode("ABCDE"))
        assertEquals("ABCD-EFGH", PairingViewModel.formatCode("ABCDEFGHJK"))
        assertEquals("2345", PairingViewModel.formatCode("0O1I2L345"))
    }

    @Test
    fun `submit needs a full code and a name`() {
        val viewModel = PairingViewModel(devices)
        viewModel.onCodeChange("ABCDEFG")
        viewModel.onDeviceNameChange("Front till")
        assertFalse(viewModel.state.value.canSubmit)

        viewModel.onCodeChange("ABCDEFGH")
        assertTrue(viewModel.state.value.canSubmit)
        viewModel.onDeviceNameChange("  ")
        assertFalse(viewModel.state.value.canSubmit)
    }

    @Test
    fun `success stores the registration and shows the business`() {
        val viewModel = PairingViewModel(devices)
        viewModel.onCodeChange("abcd-efgh")
        viewModel.onDeviceNameChange(" Front till ")

        viewModel.submit()

        assertEquals(listOf("ABCD-EFGH" to "Front till"), devices.paired)
        assertEquals("Patel Grocers", viewModel.state.value.pairedWith)
        assertEquals("Patel Grocers", devices.registration.value?.businessName)
        assertNull(viewModel.state.value.error)
    }

    @Test
    fun `an expired, used or unknown code shows the API's message`() {
        devices.next = PairOutcome.Refused("This code is wrong, already used or expired. Ask for a new code in the back office.")
        val viewModel = filledIn()

        viewModel.submit()

        assertEquals("This code is wrong, already used or expired. Ask for a new code in the back office.", viewModel.state.value.error)
        assertNull(viewModel.state.value.pairedWith)
        assertFalse(viewModel.state.value.busy)
    }

    @Test
    fun `no connection says so`() {
        devices.next = PairOutcome.Offline
        val viewModel = filledIn()

        viewModel.submit()

        assertEquals(PairingViewModel.OFFLINE_MESSAGE, viewModel.state.value.error)
    }

    @Test
    fun `typing again clears the error`() {
        devices.next = PairOutcome.Offline
        val viewModel = filledIn()
        viewModel.submit()

        viewModel.onCodeChange("ABCD-EFGJ")

        assertNull(viewModel.state.value.error)
    }

    private fun filledIn() = PairingViewModel(devices).apply {
        onCodeChange("ABCDEFGH")
        onDeviceNameChange("Front till")
    }
}

class FakeDeviceRepository : DeviceRepository {
    val paired = mutableListOf<Pair<String, String>>()
    var next: PairOutcome? = null
    var verified = 0
    private val current = MutableStateFlow<DeviceRegistration?>(null)
    override val registration: StateFlow<DeviceRegistration?> = current

    fun register(businessName: String = "Patel Grocers") {
        current.value = DeviceRegistration("d1", "dev_secret", "b1", businessName, "Front till")
    }

    override suspend fun pair(code: String, deviceName: String): PairOutcome {
        paired += code to deviceName
        return next ?: run {
            register()
            PairOutcome.Paired(current.value!!)
        }
    }

    override suspend fun verify() {
        verified++
    }

    override suspend fun forget() {
        current.value = null
    }
}
