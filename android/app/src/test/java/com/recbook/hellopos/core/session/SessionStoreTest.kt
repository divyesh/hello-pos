package com.recbook.hellopos.core.session

import com.recbook.hellopos.core.security.EncryptedFileStore
import com.recbook.hellopos.core.security.SoftwareKeys
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.io.TempDir
import java.io.File

/** Research R5: the session survives a restart, is encrypted, and never shows its tokens in text. */
class SessionStoreTest {

    @TempDir
    lateinit var dir: File

    private val keys = SoftwareKeys()
    private val session = Session("pat_access", "prt_refresh", "2026-10-11T21:00:00Z", "s1", "Ravi", "cashier", "Patel Grocers")

    @Test
    fun `a saved session is there after a restart`() {
        SessionStore(files()).save(session)

        assertEquals(session, SessionStore(files()).session.value)
        assertFalse(dir.walk().filter { it.isFile }.any { it.readText(Charsets.ISO_8859_1).contains("pat_access") })
    }

    @Test
    fun `new tokens keep the person`() {
        val store = SessionStore(files())
        store.save(session)

        store.updateTokens("pat_new", "prt_new", "2026-10-11T21:00:00Z")

        val reloaded = SessionStore(files()).session.value!!
        assertEquals("pat_new", reloaded.accessToken)
        assertEquals("prt_new", reloaded.refreshToken)
        assertEquals("Ravi", reloaded.displayName)
    }

    @Test
    fun `new tokens after sign-out are ignored`() {
        val store = SessionStore(files())
        store.save(session)
        store.clear()

        store.updateTokens("pat_new", "prt_new", "2026-10-11T21:00:00Z")

        assertNull(SessionStore(files()).session.value)
    }

    @Test
    fun `a damaged file means signed out`() {
        SessionStore(files()).save(session)
        File(dir, "session.bin").writeBytes(ByteArray(40) { 12 })

        assertNull(SessionStore(files()).session.value)
    }

    @Test
    fun `tokens never appear in text`() {
        assertFalse(session.toString().contains("pat_"))
        assertFalse(DeviceRegistration("d1", "dev_secret", "b1", "Shop", "Till").toString().contains("dev_"))
    }

    private fun files() = EncryptedFileStore(dir, keys)
}
