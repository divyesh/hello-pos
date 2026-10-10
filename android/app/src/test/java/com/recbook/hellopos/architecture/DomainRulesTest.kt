package com.recbook.hellopos.architecture

import com.lemonappdev.konsist.api.Konsist
import com.lemonappdev.konsist.api.ext.list.withPackage
import com.lemonappdev.konsist.api.verify.assertFalse
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

/** FR-010 / Constitution VII: the domain layer is pure Kotlin. */
class DomainRulesTest {

    @Test
    fun `domain does not import Android framework classes`() {
        val domainFiles = Konsist.scopeFromProduction().files.withPackage("com.recbook.hellopos.domain..")

        assertTrue(domainFiles.isNotEmpty(), "No domain files found; the rule would pass vacuously.")
        domainFiles.assertFalse { file ->
            file.imports.any { it.name.startsWith("android.") || it.name.startsWith("androidx.") }
        }
    }
}
