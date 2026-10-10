package com.recbook.hellopos.domain.model

/** The API's self-report (contract schema `HealthStatus`). Pure Kotlin, no Android types. */
data class HealthStatus(
    val service: String,
    val version: String,
    val status: String,
    val serverTimeUtc: String,
)

/** Outcome of checking the API. Every failure is an explicit [Unavailable], never an exception. */
sealed interface HealthCheck {
    data class Available(val health: HealthStatus) : HealthCheck

    data class Unavailable(val reason: String) : HealthCheck
}
