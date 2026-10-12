package com.recbook.hellopos.domain.pricing

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.boolean
import kotlinx.serialization.json.buildJsonArray
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.long
import kotlinx.serialization.json.put
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertInstanceOf
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.DynamicTest
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.TestFactory
import org.junit.jupiter.api.fail
import java.io.File
import java.math.BigDecimal

/**
 * Runs every case of `contracts/pricing-cases.json` through the Kotlin engine (Constitution I, VI; feature
 * #10 FR-004). The .NET suite runs the same file; both must pass every case.
 */
class SharedPricingCasesTest {

    private val cases: List<JsonObject> = Json.parseToJsonElement(
        File(requireNotNull(System.getProperty("hellopos.contractsDir")), "pricing-cases.json").readText(),
    ).jsonObject.getValue("cases").jsonArray.map { it.jsonObject }

    @TestFactory
    fun `shared pricing cases`(): List<DynamicTest> = cases.map { case ->
        DynamicTest.dynamicTest(case.str("id")) { run(case) }
    }

    @Test
    fun `every case in the file is run and ids are unique`() {
        val ids = cases.map { it.str("id") }
        assertEquals(ids.size, `shared pricing cases`().size)
        assertEquals(ids.size, ids.toSet().size)
    }

    private fun run(case: JsonObject) {
        val id = case.str("id")
        val result = PricingEngine.price(toInput(case.getValue("input").jsonObject))
        val expectedError = case["expectedError"]?.jsonObject
        if (expectedError != null) {
            val refused = assertInstanceOf(PricingResult.Refused::class.java, result, "$id: expected a refusal")
            val code = expectedError.str("code")
            val lineId = expectedError["lineId"]?.jsonPrimitive?.content
            assertTrue(
                refused.problems.any { it.code == code && (lineId == null || it.lineId == lineId) },
                "$id: expected refusal $code${lineId?.let { " on line $it" } ?: ""}, got ${refused.problems}",
            )
            return
        }
        if (result is PricingResult.Refused) fail("$id: refused: ${result.problems}")

        val actual = toJson((result as PricingResult.Priced).sale)
        val expected = normalize(case.getValue("expected").jsonObject)
        val differences = (expected.keys + actual.keys).filter { expected[it] != actual[it] }
            .map { "$id: $it expected ${expected[it] ?: "(absent)"} but was ${actual[it] ?: "(absent)"}" }
        assertTrue(differences.isEmpty(), differences.joinToString("\n"))
    }

    private fun toInput(input: JsonObject) = SaleInput(
        currency = input.str("currency"),
        pricing = if (input.str("pricing") == "taxInclusive") PricingMode.TaxInclusive else PricingMode.TaxExclusive,
        lines = input.getValue("lines").jsonArray.map { toLine(it.jsonObject) },
        cashRoundingStep = input["cashRoundingStep"]?.jsonPrimitive?.long ?: 0,
        depositsTaxable = input["depositsTaxable"]?.jsonPrimitive?.boolean ?: false,
        orderDiscount = input["orderDiscount"]?.jsonObject?.let(::toDiscount),
        tip = input["tip"]?.jsonObject?.let { Tip(it["amount"]?.jsonPrimitive?.long, it["percentOfSubtotal"]?.let { p -> dec(p) }) },
        payments = input["payments"]?.jsonArray.orEmpty().map {
            val payment = it.jsonObject
            PaymentInput(
                when (payment.str("method")) {
                    "cash" -> PaymentMethod.Cash
                    "card" -> PaymentMethod.Card
                    else -> PaymentMethod.Other
                },
                payment["amount"]?.jsonPrimitive?.long,
                payment["tendered"]?.jsonPrimitive?.long,
            )
        },
    )

    private fun toLine(line: JsonObject) = LineInput(
        id = line.str("id"),
        unitPrice = line.getValue("unitPrice").jsonPrimitive.long,
        quantity = dec(line.getValue("quantity")),
        taxProfile = line.getValue("taxProfile").jsonObject.let { profile ->
            TaxProfile(
                profile.str("name"),
                when (profile.str("kind")) {
                    "zeroRated" -> TaxKind.ZeroRated
                    "exempt" -> TaxKind.Exempt
                    else -> TaxKind.Taxable
                },
                profile.getValue("components").jsonArray.map {
                    TaxComponent(it.jsonObject.str("label"), dec(it.jsonObject.getValue("ratePercent")))
                },
            )
        },
        originalUnitPrice = line["originalUnitPrice"]?.jsonPrimitive?.long,
        openPrice = line["openPrice"]?.jsonPrimitive?.boolean ?: false,
        unit = line["unit"]?.jsonPrimitive?.content ?: "piece",
        minQuantity = line["minQuantity"]?.let(::dec) ?: BigDecimal.ONE,
        step = line["step"]?.let(::dec) ?: BigDecimal.ONE,
        itemDiscount = line["itemDiscount"]?.jsonObject?.let(::toDiscount),
        depositPerUnit = line["depositPerUnit"]?.jsonPrimitive?.long ?: 0,
    )

    private fun toDiscount(discount: JsonObject) = if (discount.str("kind") == "percent") {
        Discount(DiscountKind.Percent, dec(discount.getValue("value")))
    } else {
        Discount(DiscountKind.Fixed, BigDecimal.valueOf(discount.getValue("value").jsonPrimitive.long))
    }

    private fun toJson(sale: PricedSale): JsonObject = buildJsonObject {
        put("lines", buildJsonArray {
            sale.lines.forEach {
                add(buildJsonObject {
                    put("id", it.id); put("lineAmount", it.lineAmount); put("itemDiscount", it.itemDiscount)
                    put("orderDiscountShare", it.orderDiscountShare); put("taxable", it.taxable); put("deposit", it.deposit)
                })
            }
        })
        put("taxes", buildJsonArray {
            sale.taxes.forEach {
                add(buildJsonObject {
                    put("label", it.label); put("ratePercent", rate(it.ratePercent))
                    it.profile?.let { profile -> put("profile", profile) }
                    put("base", it.base); put("amount", it.amount)
                })
            }
        })
        put("baseByKind", buildJsonObject {
            put("taxable", sale.baseByKind.taxable); put("zeroRated", sale.baseByKind.zeroRated); put("exempt", sale.baseByKind.exempt)
        })
        put("subtotal", sale.subtotal); put("taxTotal", sale.taxTotal); put("tip", sale.tip); put("total", sale.total)
        sale.cashDue?.let { put("cashDue", it) }
        put("payments", buildJsonArray {
            sale.payments.forEach {
                add(buildJsonObject {
                    put("method", it.method.name.lowercase()); put("applied", it.applied)
                    put("rounding", it.rounding); put("change", it.change)
                })
            }
        })
        put("cashRounding", sale.cashRounding); put("change", sale.change); put("remaining", sale.remaining)
    }

    /** Rates compare as numbers ("13" = "13.0"); everything else as written. */
    private fun normalize(expected: JsonObject): JsonObject = JsonObject(
        expected.mapValues { (key, value) ->
            if (key != "taxes") {
                value
            } else {
                JsonArray(value.jsonArray.map { row ->
                    JsonObject(row.jsonObject.mapValues { (k, v) -> if (k == "ratePercent") JsonPrimitive(rate(dec(v))) else v })
                })
            }
        },
    )

    private fun rate(value: BigDecimal): String = value.stripTrailingZeros().toPlainString()

    private fun dec(element: JsonElement): BigDecimal = BigDecimal(element.jsonPrimitive.content)

    private fun JsonObject.str(key: String): String = getValue(key).let { if (it is JsonNull) "" else it.jsonPrimitive.content }
}
