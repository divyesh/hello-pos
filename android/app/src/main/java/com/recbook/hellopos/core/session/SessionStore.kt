package com.recbook.hellopos.core.session

import com.recbook.hellopos.core.security.EncryptedFileStore
import com.recbook.hellopos.core.security.TamperedFileException
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json

/** The signed-in person and their tokens (data-model "On the device"). Secret: never logged. */
@Serializable
data class Session(
    val accessToken: String,
    val refreshToken: String,
    val sessionExpiresAt: String,
    val staffId: String,
    val displayName: String,
    val role: String,
    val businessName: String,
    val username: String = "",
    /** Signed in on this device without a connection; [accessToken] is empty until the API accepts it. */
    val offline: Boolean = false,
    val signedInAt: String = "",
    /** The bundle's pinChangedAt the PIN was checked against (offline sign-ins only). */
    val pinChangedAt: String? = null,
) {
    override fun toString() = "Session(staffId=$staffId, role=$role)"
}

/**
 * The current POS session, kept only in memory and in an encrypted file (Constitution VIII: never
 * plain preferences or logs). Thread-safe: the token authenticator updates it from OkHttp threads.
 */
class SessionStore(private val files: EncryptedFileStore) {

    private val current = MutableStateFlow(load())
    val session: StateFlow<Session?> = current.asStateFlow()

    @Synchronized
    fun save(session: Session) {
        files.write(FILE, Json.encodeToString(session).toByteArray())
        current.value = session
    }

    /** Replaces the tokens after a refresh; does nothing when the session ended meanwhile. */
    @Synchronized
    fun updateTokens(accessToken: String, refreshToken: String, sessionExpiresAt: String) {
        val session = current.value ?: return
        save(session.copy(accessToken = accessToken, refreshToken = refreshToken, sessionExpiresAt = sessionExpiresAt))
    }

    @Synchronized
    fun clear() {
        files.delete(FILE)
        current.value = null
    }

    private fun load(): Session? =
        try {
            files.read(FILE)?.let { Json.decodeFromString<Session>(it.decodeToString()) }
        } catch (_: TamperedFileException) {
            files.delete(FILE)
            null
        }

    private companion object {
        const val FILE = "session.bin"
    }
}
