package com.recbook.hellopos.core.security

import android.content.Context
import android.content.pm.PackageManager
import android.os.Build
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.security.keystore.StrongBoxUnavailableException
import java.security.KeyPairGenerator
import java.security.KeyStore
import java.security.PrivateKey
import java.security.PublicKey
import java.security.spec.RSAKeyGenParameterSpec
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey

/**
 * The production [KeyProvider]: non-exportable keys in the Android Keystore (research R5), in
 * StrongBox when the device has one. Keys are deleted with the app's data.
 */
class KeystoreKeys(private val context: Context) : KeyProvider {

    private val keyStore: KeyStore = KeyStore.getInstance(ANDROID_KEYSTORE).apply { load(null) }

    @Synchronized
    override fun storageKey(): SecretKey =
        (keyStore.getKey(STORAGE_ALIAS, null) as SecretKey?) ?: withStrongBoxFallback { strongBox ->
            KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, ANDROID_KEYSTORE).run {
                init(
                    KeyGenParameterSpec.Builder(STORAGE_ALIAS, KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
                        .setKeySize(256)
                        .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                        .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                        .setRandomizedEncryptionRequired(true)
                        .strongBox(strongBox)
                        .build(),
                )
                generateKey()
            }
        }

    @Synchronized
    override fun devicePublicKey(): PublicKey =
        keyStore.getCertificate(DEVICE_ALIAS)?.publicKey ?: withStrongBoxFallback { strongBox ->
            KeyPairGenerator.getInstance(KeyProperties.KEY_ALGORITHM_RSA, ANDROID_KEYSTORE).run {
                initialize(
                    KeyGenParameterSpec.Builder(DEVICE_ALIAS, KeyProperties.PURPOSE_DECRYPT)
                        .setAlgorithmParameterSpec(RSAKeyGenParameterSpec(RSA_BITS, RSAKeyGenParameterSpec.F4))
                        .setDigests(KeyProperties.DIGEST_SHA256, KeyProperties.DIGEST_SHA1)
                        .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_RSA_OAEP)
                        .strongBox(strongBox)
                        .build(),
                )
                generateKeyPair().public
            }
        }

    @Synchronized
    override fun decryptWithDeviceKey(wrapped: ByteArray): ByteArray {
        val privateKey = keyStore.getKey(DEVICE_ALIAS, null) as PrivateKey
        return Cipher.getInstance(OAEP).run {
            init(Cipher.DECRYPT_MODE, privateKey)
            doFinal(wrapped)
        }
    }

    @Synchronized
    override fun deleteAll() {
        listOf(STORAGE_ALIAS, DEVICE_ALIAS).forEach { if (keyStore.containsAlias(it)) keyStore.deleteEntry(it) }
    }

    private fun KeyGenParameterSpec.Builder.strongBox(enabled: Boolean) = apply {
        if (enabled && Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) setIsStrongBoxBacked(true)
    }

    /** Tries StrongBox first; it does not support every key type (e.g. RSA 3072) on every device. */
    private fun <T> withStrongBoxFallback(create: (strongBox: Boolean) -> T): T {
        val hasStrongBox = Build.VERSION.SDK_INT >= Build.VERSION_CODES.P &&
            context.packageManager.hasSystemFeature(PackageManager.FEATURE_STRONGBOX_KEYSTORE)
        if (hasStrongBox) {
            try {
                return create(true)
            } catch (_: StrongBoxUnavailableException) {
                // Falls through to the TEE-backed key below.
            }
        }
        return create(false)
    }

    private companion object {
        const val ANDROID_KEYSTORE = "AndroidKeyStore"
        const val STORAGE_ALIAS = "hellopos.storage"
        const val DEVICE_ALIAS = "hellopos.device"
        const val RSA_BITS = 3072
        const val OAEP = "RSA/ECB/OAEPWithSHA-1AndMGF1Padding"
    }
}
