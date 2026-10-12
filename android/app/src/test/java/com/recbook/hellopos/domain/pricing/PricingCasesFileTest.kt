package com.recbook.hellopos.domain.pricing

import com.networknt.schema.Schema
import com.networknt.schema.SchemaRegistry
import com.networknt.schema.SpecificationVersion
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import tools.jackson.databind.json.JsonMapper
import java.io.File

/**
 * FR-014 / SC-005: the shared pricing test cases (Constitution, Principle I) are read by this suite
 * and validated against their schema. The .NET suite runs the same checks on the same files.
 */
class PricingCasesFileTest {

    private val contractsDir = File(
        requireNotNull(System.getProperty("hellopos.contractsDir")) {
            "System property hellopos.contractsDir is not set (see app/build.gradle.kts)."
        },
    )
    private val casesFile = File(contractsDir, "pricing-cases.json")
    private val schemaFile = File(contractsDir, "pricing-cases.schema.json")
    private val mapper = JsonMapper.builder().build()

    @Test
    fun `pricing cases file exists and matches its schema`() {
        assertTrue(casesFile.isFile, "Missing shared pricing cases: ${casesFile.absolutePath}")

        val violations = validate(casesFile.readText())

        assertTrue(
            violations.isEmpty(),
            "${casesFile.absolutePath} does not match ${schemaFile.name}:\n${violations.joinToString("\n")}",
        )
    }

    @Test
    fun `malformed case is rejected`() {
        val malformed = """{ "version": 2, "cases": [ { "id": "Bad Id" } ] }"""

        assertTrue(validate(malformed).isNotEmpty())
    }

    @Test
    fun `a case with both expected and expectedError is rejected`() {
        val both = """{ "version": 2, "cases": [ { "id": "both", "description": "d", "input": { "currency": "CAD", "pricing": "taxExclusive", "lines": [] }, "expected": { "lines": [], "taxes": [], "baseByKind": { "taxable": 0, "zeroRated": 0, "exempt": 0 }, "subtotal": 0, "taxTotal": 0, "tip": 0, "total": 0, "payments": [], "cashRounding": 0, "change": 0, "remaining": 0 }, "expectedError": { "code": "emptySale" } } ] }"""

        assertTrue(validate(both).isNotEmpty())
        assertTrue(validate(both.replace(", \"expectedError\": { \"code\": \"emptySale\" }", "")).isEmpty())
    }

    private fun validate(json: String): List<String> {
        val schema: Schema = SchemaRegistry
            .withDefaultDialect(SpecificationVersion.DRAFT_2020_12)
            .getSchema(schemaFile.readText())
        return schema.validate(mapper.readTree(json)).map { "${it.instanceLocation}: ${it.message}" }
    }
}
