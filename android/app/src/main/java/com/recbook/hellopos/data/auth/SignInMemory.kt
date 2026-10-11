package com.recbook.hellopos.data.auth

import com.recbook.hellopos.core.security.EncryptedFileStore
import com.recbook.hellopos.core.security.TamperedFileException
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import java.time.Clock
import java.time.Duration
import java.time.Instant

/**
 * What the device remembers between sign-ins, encrypted (data-model "On the device"): the last few
 * usernames for a quick shift change, and wrong-PIN counts per username so a locked person is told
 * so here (the API answers every failure the same way, FR-005). Never PINs.
 */
class SignInMemory(private val files: EncryptedFileStore, private val clock: Clock) {

    @Serializable
    private data class State(
        val recent: List<String> = emptyList(),
        val failures: Map<String, Int> = emptyMap(),
        val lockedUntil: Map<String, Long> = emptyMap(),
    )

    private var state = load()

    val recentUsernames: List<String> @Synchronized get() = state.recent

    /** When [username] may try again, or null when it is not locked here. */
    @Synchronized
    fun lockedUntil(username: String): Instant? =
        state.lockedUntil[key(username)]?.let(Instant::ofEpochMilli)?.takeIf { it.isAfter(clock.instant()) }

    /** Counts a wrong PIN; returns when the username is locked if this failure locked it. */
    @Synchronized
    fun recordFailure(username: String): Instant? {
        val key = key(username)
        val failures = (state.failures[key] ?: 0) + 1
        if (failures < MAX_FAILURES) {
            save(state.copy(failures = state.failures + (key to failures)))
            return null
        }
        val until = clock.instant().plus(LOCK)
        save(state.copy(failures = state.failures - key, lockedUntil = state.lockedUntil + (key to until.toEpochMilli())))
        return until
    }

    @Synchronized
    fun recordSuccess(username: String) {
        val key = key(username)
        save(
            State(
                recent = (listOf(username.trim()) + state.recent.filterNot { key(it) == key }).take(MAX_RECENT),
                failures = state.failures - key,
                lockedUntil = state.lockedUntil - key,
            ),
        )
    }

    private fun key(username: String) = username.trim().lowercase()

    private fun save(next: State) {
        files.write(FILE, Json.encodeToString(next).toByteArray())
        state = next
    }

    private fun load(): State =
        try {
            files.read(FILE)?.let { Json.decodeFromString<State>(it.decodeToString()) } ?: State()
        } catch (_: TamperedFileException) {
            files.delete(FILE)
            State()
        }

    companion object {
        const val MAX_FAILURES = 5
        const val MAX_RECENT = 5
        val LOCK: Duration = Duration.ofMinutes(15)
        private const val FILE = "signin.bin"
    }
}
