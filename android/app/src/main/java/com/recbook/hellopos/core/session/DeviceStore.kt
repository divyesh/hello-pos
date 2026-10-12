package com.recbook.hellopos.core.session

import com.recbook.hellopos.core.security.EncryptedFileStore
import com.recbook.hellopos.core.security.TamperedFileException
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json

/** This device's registration with one business (data-model "On the device"). Secret: never logged. */
@Serializable
data class DeviceRegistration(
    val deviceId: String,
    val credential: String,
    val businessId: String,
    val businessName: String,
    val deviceName: String,
) {
    override fun toString() = "DeviceRegistration(deviceId=$deviceId, businessName=$businessName)"
}

/** The device registration, kept encrypted (research R5); a damaged file counts as not registered. */
class DeviceStore(private val files: EncryptedFileStore) {

    private val current = MutableStateFlow(load())
    val registration: StateFlow<DeviceRegistration?> = current.asStateFlow()

    fun save(registration: DeviceRegistration) {
        files.write(FILE, Json.encodeToString(registration).toByteArray())
        current.value = registration
    }

    fun clear() {
        files.delete(FILE)
        current.value = null
    }

    private fun load(): DeviceRegistration? =
        try {
            files.read(FILE)?.let { Json.decodeFromString<DeviceRegistration>(it.decodeToString()) }
        } catch (_: TamperedFileException) {
            files.delete(FILE)
            null
        }

    private companion object {
        const val FILE = "device.bin"
    }
}
