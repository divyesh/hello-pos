package com.recbook.hellopos.core.security

import java.nio.ByteBuffer
import java.security.MessageDigest
import java.util.Base64
import javax.crypto.SecretKeyFactory
import javax.crypto.spec.PBEKeySpec

/**
 * Checks a PIN against a hash made by ASP.NET Core Identity's password hasher, v3 format (research R4):
 * `0x01 | PRF (uint32 BE) | iterations (uint32 BE) | salt length (uint32 BE) | salt | subkey`, PBKDF2 with
 * HMAC-SHA1/256/512. The shared vectors in `contracts/pin-hash-cases.json` keep both sides in step.
 */
object PinHashVerifier {

    private const val FORMAT_V3: Byte = 0x01
    private const val HEADER_BYTES = 13
    private const val MIN_SALT = 16
    private const val MIN_ITERATIONS = 10_000

    fun verify(hash: String, pin: String): Boolean {
        val bytes = try {
            Base64.getDecoder().decode(hash)
        } catch (_: IllegalArgumentException) {
            return false
        }
        if (bytes.size < HEADER_BYTES || bytes[0] != FORMAT_V3) return false
        val header = ByteBuffer.wrap(bytes, 1, HEADER_BYTES - 1)
        val algorithm = when (header.int) {
            0 -> "PBKDF2WithHmacSHA1"
            1 -> "PBKDF2WithHmacSHA256"
            2 -> "PBKDF2WithHmacSHA512"
            else -> return false
        }
        val iterations = header.int
        val saltLength = header.int
        val subkeyLength = bytes.size - HEADER_BYTES - saltLength
        if (iterations < MIN_ITERATIONS || saltLength < MIN_SALT || subkeyLength < MIN_SALT) return false
        val salt = bytes.copyOfRange(HEADER_BYTES, HEADER_BYTES + saltLength)
        val expected = bytes.copyOfRange(HEADER_BYTES + saltLength, bytes.size)

        val spec = PBEKeySpec(pin.toCharArray(), salt, iterations, subkeyLength * 8)
        return try {
            MessageDigest.isEqual(expected, SecretKeyFactory.getInstance(algorithm).generateSecret(spec).encoded)
        } finally {
            spec.clearPassword()
        }
    }
}
