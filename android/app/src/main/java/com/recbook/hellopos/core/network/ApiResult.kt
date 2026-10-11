package com.recbook.hellopos.core.network

import kotlinx.serialization.SerializationException
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import retrofit2.Response
import java.io.IOException
import kotlin.coroutines.cancellation.CancellationException

/** The outcome of one API call, with failures typed instead of thrown (Constitution IX). */
sealed interface ApiResult<out T> {
    data class Ok<T>(val value: T) : ApiResult<T>

    /** The API answered with an error; [title] is the problem title meant for people, [errors] the per-field messages. */
    data class Problem(
        val status: Int,
        val title: String?,
        val retryAfterSeconds: Long?,
        val errors: Map<String, List<String>> = emptyMap(),
    ) : ApiResult<Nothing>

    /** No answer: no connection, timeout or an unreadable response. */
    data class Offline(val reason: String) : ApiResult<Nothing>
}

/** Runs a generated-client call and maps the response. Never logs bodies (they may hold secrets). */
suspend fun <T> apiCall(call: suspend () -> Response<T>): ApiResult<T> =
    try {
        val response = call()
        val body = response.body()
        when {
            response.isSuccessful && body != null -> ApiResult.Ok(body)
            response.isSuccessful -> ApiResult.Problem(response.code(), null, null)
            else -> {
                val problem = response.errorBody()?.use { parseProblem(it.string()) }
                ApiResult.Problem(
                    status = response.code(),
                    title = problem?.first,
                    retryAfterSeconds = response.headers()["Retry-After"]?.toLongOrNull(),
                    errors = problem?.second.orEmpty(),
                )
            }
        }
    } catch (e: CancellationException) {
        throw e
    } catch (e: IOException) {
        ApiResult.Offline("network error: ${e.javaClass.simpleName}")
    } catch (e: SerializationException) {
        ApiResult.Offline("invalid response: ${e.javaClass.simpleName}")
    }

/** Like [apiCall] for calls without a response body (204). */
suspend fun apiCallNoContent(call: suspend () -> Response<Unit>): ApiResult<Unit> =
    when (val result = apiCall(call)) {
        is ApiResult.Problem -> if (result.status in 200..299) ApiResult.Ok(Unit) else result
        else -> result
    }

/** (title, field errors) of an RFC 9457 problem body, or null when it is not one. */
private fun parseProblem(body: String): Pair<String?, Map<String, List<String>>>? =
    try {
        val problem = Json.parseToJsonElement(body).jsonObject
        val errors = (problem["errors"] as? JsonObject).orEmpty().mapValues { (_, messages) ->
            (messages as? JsonArray).orEmpty().mapNotNull { it.jsonPrimitive.contentOrNull }
        }
        problem["title"]?.jsonPrimitive?.contentOrNull to errors
    } catch (_: SerializationException) {
        null
    } catch (_: IllegalArgumentException) {
        null
    }
