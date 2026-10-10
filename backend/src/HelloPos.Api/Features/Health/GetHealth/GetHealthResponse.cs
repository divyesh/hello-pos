using System.Text.Json.Serialization;

namespace HelloPos.Api.Features.Health.GetHealth;

/// <summary>Contract schema <c>HealthStatus</c>: these four fields and nothing else (FR-005, FR-007).</summary>
public sealed record GetHealthResponse(
    string Service,
    string Version,
    HealthState Status,
    DateTime ServerTimeUtc);

[JsonConverter(typeof(JsonStringEnumConverter<HealthState>))]
public enum HealthState
{
    [JsonStringEnumMemberName("available")]
    Available,

    [JsonStringEnumMemberName("degraded")]
    Degraded,
}
