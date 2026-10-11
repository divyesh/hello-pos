package com.recbook.hellopos.core.security

import org.junit.jupiter.api.Assertions.assertArrayEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.assertThrows
import org.junit.jupiter.api.io.TempDir
import java.io.File

/** Research R5: secrets are stored encrypted and any change to a stored file is detected. */
class EncryptedFileStoreTest {

    @TempDir
    lateinit var dir: File

    private val keys = SoftwareKeys()
    private val secret = "dev_0199.0199.c2VjcmV0".toByteArray()

    @Test
    fun `round trip, and the file on disk does not contain the secret`() {
        val store = EncryptedFileStore(dir, keys)

        store.write("device.bin", secret)

        assertArrayEquals(secret, EncryptedFileStore(dir, keys).read("device.bin"))
        assertFalse(File(dir, "device.bin").readBytes().decodeToString().contains("dev_"))
    }

    @Test
    fun `a missing file reads as null, and delete removes it`() {
        val store = EncryptedFileStore(dir, keys)
        assertNull(store.read("session.bin"))

        store.write("session.bin", secret)
        store.delete("session.bin")

        assertNull(store.read("session.bin"))
    }

    @Test
    fun `a changed byte is detected`() {
        val store = EncryptedFileStore(dir, keys)
        store.write("device.bin", secret)
        val file = File(dir, "device.bin")
        val bytes = file.readBytes()
        bytes[bytes.size - 3] = (bytes[bytes.size - 3].toInt() xor 1).toByte()
        file.writeBytes(bytes)

        assertThrows<TamperedFileException> { store.read("device.bin") }
    }

    @Test
    fun `a file swapped for another is detected`() {
        val store = EncryptedFileStore(dir, keys)
        store.write("session.bin", secret)
        File(dir, "session.bin").copyTo(File(dir, "device.bin"))

        assertThrows<TamperedFileException> { store.read("device.bin") }
    }

    @Test
    fun `a truncated file is detected`() {
        val store = EncryptedFileStore(dir, keys)
        File(dir, "device.bin").writeBytes(byteArrayOf(12, 1, 2))

        assertThrows<TamperedFileException> { store.read("device.bin") }
    }

    @Test
    fun `files cannot be read once the keys are deleted`() {
        val store = EncryptedFileStore(dir, keys)
        store.write("device.bin", secret)

        keys.deleteAll()

        assertThrows<TamperedFileException> { store.read("device.bin") }
    }
}
