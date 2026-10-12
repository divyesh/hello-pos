package com.recbook.hellopos.core.session

import com.recbook.hellopos.feature.signin.MutableClock
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import java.time.Duration
import java.time.Instant

/** Clarification Q3: five idle minutes lock the till; any touch resets the timer. */
class IdleTrackerTest {

    private val clock = MutableClock(Instant.parse("2026-10-11T09:00:00Z"))
    private val idle = IdleTracker(clock)

    @Test
    fun `five idle minutes lock`() {
        advance(Duration.ofMinutes(4).plusSeconds(59))
        idle.check()
        assertFalse(idle.locked.value)

        advance(Duration.ofSeconds(1))
        idle.check()
        assertTrue(idle.locked.value)
    }

    @Test
    fun `a touch resets the timer`() {
        advance(Duration.ofMinutes(4))
        idle.onInteraction()
        advance(Duration.ofMinutes(4))
        idle.check()

        assertFalse(idle.locked.value)
    }

    @Test
    fun `touches while locked do not unlock, unlocking starts a new period`() {
        advance(Duration.ofMinutes(5))
        idle.check()
        idle.onInteraction()
        idle.check()
        assertTrue(idle.locked.value)

        idle.unlock()
        advance(Duration.ofMinutes(3))
        idle.check()
        assertFalse(idle.locked.value)
    }

    private fun advance(by: Duration) {
        clock.now = clock.now.plus(by)
    }
}
