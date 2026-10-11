package com.recbook.hellopos.core.security

import java.io.File
import java.io.IOException
import java.security.GeneralSecurityException
import javax.crypto.Cipher
import javax.crypto.spec.GCMParameterSpec

/** A stored file could not be decrypted: it was changed, swapped or its key is gone. */
class TamperedFileException(name: String, cause: Throwable) : IOException("Stored file '$name' could not be read", cause)

/**
 * Small secret files in app-private storage, each AES-GCM encrypted under [KeyProvider.storageKey]
 * (research R5, Constitution VIII). The file name is the associated data, so one file cannot be
 * swapped for another. Writes are atomic (temp file, then rename).
 */
class EncryptedFileStore(private val directory: File, private val keys: KeyProvider) {

    @Synchronized
    fun write(name: String, plaintext: ByteArray) {
        directory.mkdirs()
        val cipher = Cipher.getInstance(TRANSFORMATION).apply { init(Cipher.ENCRYPT_MODE, keys.storageKey()) }
        cipher.updateAAD(name.toByteArray())
        val iv = cipher.iv
        val sealed = cipher.doFinal(plaintext)
        val temp = File(directory, "$name.tmp")
        temp.outputStream().use {
            it.write(iv.size)
            it.write(iv)
            it.write(sealed)
        }
        if (!temp.renameTo(fileOf(name))) {
            fileOf(name).delete()
            if (!temp.renameTo(fileOf(name))) throw IOException("Could not store '$name'")
        }
    }

    /** The decrypted contents, or null when the file does not exist; throws [TamperedFileException] when it cannot be read. */
    @Synchronized
    fun read(name: String): ByteArray? {
        val file = fileOf(name)
        if (!file.exists()) return null
        val bytes = file.readBytes()
        return try {
            val ivLength = bytes.first().toInt()
            require(ivLength in 12..16 && bytes.size > 1 + ivLength) { "bad header" }
            val cipher = Cipher.getInstance(TRANSFORMATION)
            cipher.init(Cipher.DECRYPT_MODE, keys.storageKey(), GCMParameterSpec(TAG_BITS, bytes, 1, ivLength))
            cipher.updateAAD(name.toByteArray())
            cipher.doFinal(bytes, 1 + ivLength, bytes.size - 1 - ivLength)
        } catch (e: GeneralSecurityException) {
            throw TamperedFileException(name, e)
        } catch (e: IllegalArgumentException) {
            throw TamperedFileException(name, e)
        } catch (e: NoSuchElementException) {
            throw TamperedFileException(name, e)
        }
    }

    @Synchronized
    fun delete(name: String) {
        fileOf(name).delete()
    }

    private fun fileOf(name: String): File {
        require(name.matches(SAFE_NAME)) { "Invalid file name" }
        return File(directory, name)
    }

    private companion object {
        const val TRANSFORMATION = "AES/GCM/NoPadding"
        const val TAG_BITS = 128
        val SAFE_NAME = Regex("[a-z0-9-]+\\.bin")
    }
}
