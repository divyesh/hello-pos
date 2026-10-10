using System.Reflection;
using HelloPos.Api.Infrastructure;

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

    internal static GetHealthResponse Handle(TimeProvider clock) =>
        new(Logging.ApplicationName, Version, HealthState.Available, clock.GetUtcNow().UtcDateTime);

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
