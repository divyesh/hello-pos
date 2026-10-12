package com.recbook.hellopos.core.security

import java.security.KeyPair
import java.security.KeyPairGenerator
import java.security.PublicKey
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey

/** A [KeyProvider] in plain memory for JVM tests (the Android Keystore is not available there). */
class SoftwareKeys : KeyProvider {

    private var storage: SecretKey? = null
    private var device: KeyPair? = null

    override fun storageKey(): SecretKey =
        storage ?: KeyGenerator.getInstance("AES").apply { init(256) }.generateKey().also { storage = it }

    override fun devicePublicKey(): PublicKey =
        (device ?: KeyPairGenerator.getInstance("RSA").apply { initialize(2048) }.generateKeyPair().also { device = it }).public

    override fun decryptWithDeviceKey(wrapped: ByteArray): ByteArray =
        Cipher.getInstance("RSA/ECB/OAEPWithSHA-1AndMGF1Padding").run {
            init(Cipher.DECRYPT_MODE, requireNotNull(device) { "no device key" }.private)
            doFinal(wrapped)
        }

    override fun deleteAll() {
        storage = null
        device = null
    }
}
