package com.recbook.hellopos.data.auth

import com.recbook.hellopos.core.network.ApiResult
import com.recbook.hellopos.core.network.apiCall
import com.recbook.hellopos.core.security.EncryptedFileStore
import com.recbook.hellopos.core.security.KeyProvider
import com.recbook.hellopos.core.security.PinHashVerifier
import com.recbook.hellopos.core.security.TamperedFileException
import com.recbook.hellopos.data.remote.generated.apis.PosApi
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.withContext
import kotlinx.serialization.Serializable
import kotlinx.serialization.SerializationException
import kotlinx.serialization.json.Json
import java.nio.ByteBuffer
import java.security.GeneralSecurityException
import java.util.Base64
import javax.crypto.Cipher
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec
import javax.inject.Inject
import javax.inject.Singleton
import kotlin.time.Duration
import kotlin.time.Duration.Companion.minutes

/** One person this device can sign in without a connection (bundle plaintext, research R4). */
@Serializable
data class OfflineStaff(
    val staffId: String,
    val username: String,
    val displayName: String,
    val role: String,
    val pinHash: String,
    val pinChangedAt: String? = null,
) {
    override fun toString() = "OfflineStaff(staffId=$staffId, role=$role)"
}

@Serializable
data class OfflineBundle(val businessId: String, val generatedAt: String, val staff: List<OfflineStaff>)

/**
 * The offline-access bundle (clarification Q1, research R4): fetched while online, kept as received
 * (encrypted to this device's key) inside an encrypted file, and decrypted only in memory for one PIN
 * check. A failed refresh keeps the last good bundle.
 */
@Singleton
class OfflineAccessRepository @Inject constructor(
    private val api: PosApi,
    private val files: EncryptedFileStore,
    private val keys: KeyProvider,
) {

    @Serializable
    private data class Stored(val version: String, val generatedAt: String, val bundle: String)

    /** Fetches a fresh bundle; true when stored. Keeps the previous one on any failure. */
    suspend fun refresh(): Boolean {
        val result = apiCall { api.posOfflineAccess() }
        if (result !is ApiResult.Ok) return false
        val stored = Stored(result.value.version, result.value.generatedAt, result.value.bundle)
        return withContext(Dispatchers.IO) {
            if (open(stored) == null) return@withContext false
            files.write(FILE, Json.encodeToString(stored).toByteArray())
            true
        }
    }

    /** Whether a bundle is held, i.e. offline sign-in is possible at all. */
    suspend fun available(): Boolean = withContext(Dispatchers.IO) { load() != null }

    /** The person with this username when [pin] matches their hash; null for a wrong PIN or unknown name. */
    suspend fun check(username: String, pin: String): OfflineStaff? = withContext(Dispatchers.Default) {
        val key = username.trim().lowercase()
        bundle()?.staff?.firstOrNull { it.username == key }?.takeIf { PinHashVerifier.verify(it.pinHash, pin) }
    }

    /** Checks [pin] for the person with [staffId] (the idle lock); null when there is no bundle or no such person. */
    suspend fun checkById(staffId: String, pin: String): Boolean? = withContext(Dispatchers.Default) {
        bundle()?.staff?.firstOrNull { it.staffId == staffId }?.let { PinHashVerifier.verify(it.pinHash, pin) }
    }

    suspend fun clear() = withContext(Dispatchers.IO) { files.delete(FILE) }

    private suspend fun bundle(): OfflineBundle? = withContext(Dispatchers.IO) { load()?.let(::open) }

    private fun load(): Stored? =
        try {
            files.read(FILE)?.let { Json.decodeFromString<Stored>(it.decodeToString()) }
        } catch (_: TamperedFileException) {
            files.delete(FILE)
            null
        } catch (_: SerializationException) {
            files.delete(FILE)
            null
        }

    /** [2-byte length][RSA-OAEP-wrapped AES key][12-byte nonce][AES-GCM ciphertext + tag], as the API seals it. */
    private fun open(stored: Stored): OfflineBundle? =
        try {
            val sealed = Base64.getDecoder().decode(stored.bundle)
            val buffer = ByteBuffer.wrap(sealed)
            val wrapped = ByteArray(buffer.short.toInt() and 0xFFFF).also(buffer::get)
            val nonce = ByteArray(NONCE_BYTES).also(buffer::get)
            val aesKey = keys.decryptWithDeviceKey(wrapped)
            try {
                val cipher = Cipher.getInstance("AES/GCM/NoPadding")
                cipher.init(Cipher.DECRYPT_MODE, SecretKeySpec(aesKey, "AES"), GCMParameterSpec(TAG_BITS, nonce))
                val plaintext = cipher.doFinal(sealed, buffer.position(), buffer.remaining())
                JSON.decodeFromString<OfflineBundle>(plaintext.decodeToString())
            } finally {
                aesKey.fill(0)
            }
        } catch (_: GeneralSecurityException) {
            null
        } catch (_: IllegalArgumentException) {
            null
        } catch (_: java.nio.BufferUnderflowException) {
            null
        }

    companion object {
        val REFRESH_PERIOD: Duration = 15.minutes
        private const val FILE = "offline.bin"
        private const val NONCE_BYTES = 12
        private const val TAG_BITS = 128
        private val JSON = Json { ignoreUnknownKeys = true }
    }
}

/** Runs [action] now and then every [period] until cancelled (the app's periodic refreshes). */
suspend fun repeatEvery(period: Duration, action: suspend () -> Unit) {
    while (true) {
        action()
        delay(period)
    }
}
