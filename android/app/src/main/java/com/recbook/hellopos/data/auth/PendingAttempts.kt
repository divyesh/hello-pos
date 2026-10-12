package com.recbook.hellopos.data.auth

import com.recbook.hellopos.core.security.EncryptedFileStore
import com.recbook.hellopos.core.security.TamperedFileException
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json

/** A PIN attempt made offline, to be reported (research R4). Never the PIN. */
@Serializable
data class OfflineAttempt(val username: String, val success: Boolean, val at: String)

/** Offline attempts waiting to be reported, encrypted at rest; keeps the newest [MAX]. */
class PendingAttempts(private val files: EncryptedFileStore) {

    @Synchronized
    fun add(attempt: OfflineAttempt) = save((load() + attempt).takeLast(MAX))

    @Synchronized
    fun all(): List<OfflineAttempt> = load()

    /** Removes the first [count] (those just reported); newer ones stay. */
    @Synchronized
    fun drop(count: Int) = save(load().drop(count))

    private fun save(attempts: List<OfflineAttempt>) =
        if (attempts.isEmpty()) files.delete(FILE) else files.write(FILE, Json.encodeToString(attempts).toByteArray())

    private fun load(): List<OfflineAttempt> =
        try {
            files.read(FILE)?.let { Json.decodeFromString<List<OfflineAttempt>>(it.decodeToString()) }.orEmpty()
        } catch (_: TamperedFileException) {
            files.delete(FILE)
            emptyList()
        }

    companion object {
        /** The API takes at most 100 per report. */
        const val MAX = 100
        private const val FILE = "attempts.bin"
    }
}
