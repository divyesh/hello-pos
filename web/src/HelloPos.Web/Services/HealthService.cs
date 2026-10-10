using System.Text.Json;
using HelloPos.Web.ApiClient;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;

namespace HelloPos.Web.Services;

/// <summary>Result of asking the API for its health, as shown on the start screen.</summary>
public abstract record HealthCheckResult
{
    private HealthCheckResult()
    {
    }

    public sealed record Available(string Status, string Version) : HealthCheckResult;

    public sealed record Unavailable(string Reason) : HealthCheckResult;
}

public interface IHealthService
{
    Task<HealthCheckResult> CheckAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Calls <c>GET /health</c> through the client generated from the contract. Every failure (timeout,
/// network error, non-200, malformed or incomplete body) becomes <see cref="HealthCheckResult.Unavailable"/>
/// and is logged; nothing is swallowed silently (Principle IX).
/// </summary>
public sealed partial class HealthService(HttpClient http, ILogger<HealthService> logger, TimeSpan timeout) : IHealthService
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    public async Task<HealthCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (http.BaseAddress is null)
        {
            LogNotConfigured(logger);
            return new HealthCheckResult.Unavailable("API address not configured");
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            var adapter = new HttpClientRequestAdapter(new AnonymousAuthenticationProvider(), httpClient: http)
            {
                BaseUrl = http.BaseAddress.ToString().TrimEnd('/'),
            };
            var health = await new HelloPosApiClient(adapter).Health
                .GetAsync(cancellationToken: timeoutSource.Token)
                .ConfigureAwait(false);

            if (health?.Status is not { } status || string.IsNullOrWhiteSpace(health.Version))
            {
                LogFailed(logger, "incomplete response");
                return new HealthCheckResult.Unavailable("incomplete response");
            }

            return new HealthCheckResult.Available(status.ToString().ToLowerInvariant(), health.Version);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogFailed(logger, "timed out");
            return new HealthCheckResult.Unavailable("timed out");
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or JsonException)
        {
            LogFailedWithException(logger, ex);
            return new HealthCheckResult.Unavailable(ex.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "API health check skipped: Api:BaseUrl is not configured")]
    private static partial void LogNotConfigured(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "API health check failed: {Reason}")]
    private static partial void LogFailed(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "API health check failed")]
    private static partial void LogFailedWithException(ILogger logger, Exception exception);
}
