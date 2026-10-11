package com.recbook.hellopos.core.security

import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import java.io.File

/** Research R10: every hash the .NET hasher produced in the shared vectors verifies here, and wrong PINs fail. */
class PinHashVerifierTest {

    @Serializable
    private data class Case(val pin: String, val hash: String, val wrongPins: List<String>)

    @Serializable
    private data class Cases(val description: String, val cases: List<Case>)

    private val cases = Json.decodeFromString<Cases>(
        File(requireNotNull(System.getProperty("hellopos.contractsDir")), "pin-hash-cases.json").readText(),
    ).cases

    @Test
    fun `every shared case verifies and its wrong PINs fail`() {
        assertTrue(cases.size >= 3, "expected the shared vectors")
        cases.forEach { case ->
            assertTrue(PinHashVerifier.verify(case.hash, case.pin), "PIN ${case.pin} should verify")
            case.wrongPins.forEach { assertFalse(PinHashVerifier.verify(case.hash, it), "wrong PIN $it should fail") }
        }
    }

    @Test
    fun `damaged or foreign hashes never verify`() {
        val good = cases.first()
        assertFalse(PinHashVerifier.verify("not base64!", good.pin))
        assertFalse(PinHashVerifier.verify("", good.pin))
        assertFalse(PinHashVerifier.verify("AA" + good.hash.drop(2), good.pin))
        assertFalse(PinHashVerifier.verify(good.hash.dropLast(8) + "AAAAAAA=", good.pin))
    }
}
