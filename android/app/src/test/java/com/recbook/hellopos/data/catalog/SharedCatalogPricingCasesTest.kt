package com.recbook.hellopos.data.catalog

import com.networknt.schema.SchemaRegistry
import com.networknt.schema.SpecificationVersion
import com.recbook.hellopos.data.remote.generated.infrastructure.Serializer
import com.recbook.hellopos.data.remote.generated.models.CatalogSync
import com.recbook.hellopos.domain.catalog.CatalogPriceFeed
import com.recbook.hellopos.domain.pricing.PricingEngine
import com.recbook.hellopos.domain.pricing.PricingResult
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.long
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.DynamicTest
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.TestFactory
import tools.jackson.databind.json.JsonMapper
import java.io.File
import java.math.BigDecimal
import java.time.Instant
import java.util.UUID

/**
 * Feature #13 FR-017, SC-005, research R9: every check of `contracts/catalog-pricing-cases.json`, read
 * through the same mapping as a real download and priced with [CatalogPriceFeed] and the engine. The .NET
 * suite prices the same file through the server's `CatalogPricing`.
 */
class SharedCatalogPricingCasesTest {

    private val contracts = File(requireNotNull(System.getProperty("hellopos.contractsDir")))
    private val file = File(contracts, "catalog-pricing-cases.json")
    private val root = Json.parseToJsonElement(file.readText()).jsonObject

    @TestFactory
    fun everyCheck(): List<DynamicTest> = root["catalogs"]!!.jsonArray.flatMap { entry ->
        val catalog = entry.jsonObject
        val download = CatalogMapping.toDownload(Serializer.kotlinxSerializationJson.decodeFromString<CatalogSync>(catalog["catalog"].toString()))
        val feed = CatalogPriceFeed.of(download)
        catalog["checks"]!!.jsonArray.map { element ->
            val check = element.jsonObject
            DynamicTest.dynamicTest("${catalog["id"]!!.jsonPrimitive.content}: ${check["id"]!!.jsonPrimitive.content}") {
                run(check, feed, download.products.associateBy { it.id })
            }
        }
    }

    @Test
    fun theFile_MatchesItsSchema() {
        val schema = SchemaRegistry.withDefaultDialect(SpecificationVersion.DRAFT_2020_12)
            .getSchema(File(contracts, "catalog-pricing-cases.schema.json").readText())
        val violations = schema.validate(JsonMapper.builder().build().readTree(file.readText()))
        assertTrue(violations.isEmpty(), violations.joinToString("\n"))
    }

    private fun run(check: JsonObject, feed: CatalogPriceFeed, products: Map<UUID, com.recbook.hellopos.domain.catalog.CatalogProduct>) {
        val product = products.getValue(UUID.fromString(check["productId"]!!.jsonPrimitive.content))
        val at = Instant.parse(check["at"]!!.jsonPrimitive.content)
        val expected = check["expected"]!!.jsonObject
        val line = feed.line(product, "a", at, BigDecimal(check["quantity"]!!.jsonPrimitive.content), check["enteredPrice"]?.jsonPrimitive?.long)

        val sale = (PricingEngine.price(feed.sale(listOf(line))) as PricingResult.Priced).sale

        val priced = sale.lines.single()
        assertEquals(expected["taxProfile"]!!.jsonPrimitive.content, feed.taxProfileOf(product).name)
        assertEquals(expected["lineAmount"]!!.jsonPrimitive.long, priced.lineAmount)
        assertEquals(expected["itemDiscount"]!!.jsonPrimitive.long, priced.itemDiscount)
        assertEquals(expected["deposit"]!!.jsonPrimitive.long, priced.deposit)
        assertEquals(
            expected["taxes"]!!.jsonArray.map { t ->
                val tax = t.jsonObject
                Triple(tax["label"]!!.jsonPrimitive.content, BigDecimal(tax["ratePercent"]!!.jsonPrimitive.content).stripTrailingZeros(), tax["amount"]!!.jsonPrimitive.long)
            },
            sale.taxes.map { Triple(it.label, it.ratePercent.stripTrailingZeros(), it.amount) },
        )
        assertEquals(expected["taxTotal"]!!.jsonPrimitive.long, sale.taxTotal)
        assertEquals(expected["total"]!!.jsonPrimitive.long, sale.total)
    }
}
