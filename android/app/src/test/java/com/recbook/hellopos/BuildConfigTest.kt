package com.recbook.hellopos

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Test

class BuildConfigTest {

    @Test
    fun `application id is com recbook hellopos`() {
        assertEquals("com.recbook.hellopos", BuildConfig.APPLICATION_ID)
    }
}
