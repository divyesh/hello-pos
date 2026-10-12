package com.recbook.hellopos.domain.catalog

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Test

/** Feature #13 FR-014: name search ignores case and accents. */
class SearchTextTest {

    @Test
    fun caseAndAccents_AreIgnored() {
        assertEquals("creme brulee", SearchText.normalize("Crème Brûlée"))
        assertEquals("jalapeno chips", SearchText.normalize("  JALAPEÑO Chips "))
        assertEquals("dal makhani", SearchText.normalize("Dal Makhani"))
    }
}
