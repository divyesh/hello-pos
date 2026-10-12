package com.recbook.hellopos.domain.catalog

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.boolean
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.DynamicTest
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.TestFactory
import java.io.File

/**
 * Feature #13 FR-011, research R8: the till parses every code of `contracts/barcode-cases.json` exactly as
 * the server's kernel does (the .NET suite runs the same file).
 */
class SharedBarcodeCasesTest {

    private val cases: List<JsonObject> = Json.parseToJsonElement(
        File(requireNotNull(System.getProperty("hellopos.contractsDir")), "barcode-cases.json").readText(),
    ).jsonObject["cases"]!!.jsonArray.map { it.jsonObject }

    @TestFactory
    fun everySharedCase(): List<DynamicTest> = cases.mapIndexed { index, case ->
        val input = case["input"]!!.jsonPrimitive.content
        DynamicTest.dynamicTest("case ${index + 1}: [$input]") {
            val result = BarcodeRules.parse(input)
            if (case["valid"]!!.jsonPrimitive.boolean) {
                val barcode = (result as BarcodeParse.Valid).barcode
                assertEquals(case["code"]!!.jsonPrimitive.content, barcode.code)
                assertEquals(case["kind"]!!.jsonPrimitive.content, barcode.kind.name.lowercase())
            } else {
                val problem = (result as BarcodeParse.Invalid).problem
                assertEquals(case["reason"]!!.jsonPrimitive.content, problem.name.replaceFirstChar(Char::lowercase))
            }
        }
    }

    @Test
    fun theCasesCoverEveryProblem_AndEachHasAMessage() {
        val reasons = cases.mapNotNull { it["reason"]?.jsonPrimitive?.content }.toSet()
        BarcodeProblem.entries.forEach { problem ->
            assertTrue(problem.name.replaceFirstChar(Char::lowercase) in reasons, "no shared case for $problem")
            assertTrue(BarcodeRules.message(problem).isNotBlank())
        }
    }

    @Test
    fun aUpcAAndItsEan13Form_AreTheSameCode() {
        assertEquals(BarcodeRules.parse("067000004568"), BarcodeRules.parse("0067000004568"))
        assertEquals(BarcodeRules.parse("0 67000-00456 8"), BarcodeRules.parse("0067000004568"))
    }
}
