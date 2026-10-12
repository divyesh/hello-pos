package com.recbook.hellopos.core.security

import java.security.PublicKey
import javax.crypto.SecretKey

/**
 * The device's keys (research R5). Production keys live in the Android Keystore ([KeystoreKeys]);
 * JVM tests cannot reach the Keystore, so they pass a software provider instead.
 */
interface KeyProvider {

    /** AES-256 key that encrypts every file in [EncryptedFileStore]; created on first use. */
    fun storageKey(): SecretKey

    /** The device's RSA public key, sent at pairing so the API can encrypt the offline bundle to it. */
    fun devicePublicKey(): PublicKey

    /** Unwraps a key the API encrypted to [devicePublicKey] (RSA-OAEP with SHA-1, research R4). */
    fun decryptWithDeviceKey(wrapped: ByteArray): ByteArray

    /** Deletes every key; anything they encrypted can no longer be read (forces re-registration). */
    fun deleteAll()
}
