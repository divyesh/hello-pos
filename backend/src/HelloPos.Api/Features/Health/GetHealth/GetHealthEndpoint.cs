using System.Reflection;
using HelloPos.Api.Infrastructure;
using HelloPos.Api.Infrastructure.Database;

namespace HelloPos.Api.Features.Health.GetHealth;

/// <summary>Slice for contract operation <c>getHealth</c>: <c>GET /health</c>, anonymous.</summary>
public static class GetHealthEndpoint
{
    private const string DefaultVersion = "0.1.0-dev";

    private static readonly string Version = ReadVersion();

    public static IEndpointRouteBuilder MapGetHealth(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", Handle)
            .WithName("getHealth")
            .AllowAnonymous();
        return app;
    }

    /// <summary>
    /// <c>degraded</c> when a configured database is unreachable (FR-013); the response never says why
    /// (FR-014). The probe is bounded to 2 seconds, so this stays fast (SC-003).
    /// </summary>
    internal static async Task<GetHealthResponse> Handle(
        TimeProvider clock, IDatabaseProbe database, CancellationToken cancellationToken)
    {
        var status = await database.IsReachableAsync(cancellationToken).ConfigureAwait(false)
            ? HealthState.Available
            : HealthState.Degraded;
        return new(Logging.ApplicationName, Version, status, clock.GetUtcNow().UtcDateTime);
    }

    private static string ReadVersion()
    {
        var informational = typeof(GetHealthEndpoint).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
        {
            return DefaultVersion;
        }

        // Drop source-control metadata ("0.1.0-dev+abc123") from the public version.
        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus > 0 ? informational[..plus] : informational;
    }
}
