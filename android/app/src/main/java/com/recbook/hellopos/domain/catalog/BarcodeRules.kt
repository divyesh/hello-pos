package com.recbook.hellopos.domain.catalog

enum class BarcodeKind { Ean13, Ean8, Plu }

/** Why a code is refused; messages match the server's (`HelloPos.Kernel.Catalog.BarcodeRules`). */
enum class BarcodeProblem { NotDigits, Length, CheckDigit, PluRange, InStore }

data class Barcode(val code: String, val kind: BarcodeKind)

sealed interface BarcodeParse {
    data class Valid(val barcode: Barcode) : BarcodeParse

    data class Invalid(val problem: BarcodeProblem) : BarcodeParse
}

/**
 * Normalizes and validates codes exactly as the server does (feature #13 research R8; shared cases in
 * `contracts/barcode-cases.json`). UPC-A becomes its 13-digit EAN form, so a code scanned as 12 or 13 digits
 * finds the same product.
 */
object BarcodeRules {

    fun parse(input: String?): BarcodeParse {
        val digits = (input ?: "").filter { !it.isWhitespace() && it != '-' }
        if (digits.isEmpty() || !digits.all { it in '0'..'9' }) return BarcodeParse.Invalid(BarcodeProblem.NotDigits)
        return when (digits.length) {
            4 -> if (isPlu(digits)) valid(digits, BarcodeKind.Plu) else BarcodeParse.Invalid(BarcodeProblem.PluRange)
            5 -> if (digits[0] == '9' && isPlu(digits.substring(1))) valid(digits, BarcodeKind.Plu) else BarcodeParse.Invalid(BarcodeProblem.PluRange)
            8 -> gtin(digits, BarcodeKind.Ean8, inStore = digits[0] == '2')
            12 -> gtin("0$digits", BarcodeKind.Ean13, inStore = digits[0] == '2')
            13 -> gtin(digits, BarcodeKind.Ean13, inStore = digits[0] == '2' || digits.startsWith("02"))
            else -> BarcodeParse.Invalid(BarcodeProblem.Length)
        }
    }

    fun message(problem: BarcodeProblem): String = when (problem) {
        BarcodeProblem.NotDigits -> "A barcode has only digits (spaces and dashes are ignored)."
        BarcodeProblem.Length -> "Use a 12-digit UPC-A, 13-digit EAN-13, 8-digit EAN-8, or a 4–5 digit PLU."
        BarcodeProblem.CheckDigit -> "The last digit (check digit) does not match; check the number."
        BarcodeProblem.PluRange -> "PLU codes are 3000–4999, or 9 followed by one of those for organic produce."
        BarcodeProblem.InStore -> "Codes starting with 2 are for in-store scale labels, which are not supported yet."
    }

    /** The GS1 mod-10 check digit for [body] (all digits but the last). */
    fun checkDigit(body: String): Int {
        var total = 0
        for (i in body.indices) {
            val digit = body[body.length - 1 - i] - '0'
            total += if (i % 2 == 0) digit * 3 else digit
        }
        return (10 - total % 10) % 10
    }

    private fun gtin(code: String, kind: BarcodeKind, inStore: Boolean): BarcodeParse = when {
        checkDigit(code.dropLast(1)) != code.last() - '0' -> BarcodeParse.Invalid(BarcodeProblem.CheckDigit)
        inStore -> BarcodeParse.Invalid(BarcodeProblem.InStore)
        else -> valid(code, kind)
    }

    private fun valid(code: String, kind: BarcodeKind) = BarcodeParse.Valid(Barcode(code, kind))

    private fun isPlu(fourDigits: String): Boolean = fourDigits.toInt() in 3000..4999
}
