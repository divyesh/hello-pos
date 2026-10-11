using Npgsql;

namespace HelloPos.Api.Infrastructure.Database;

/// <summary>Answers whether the database is reachable, within a bounded time, without throwing.</summary>
public interface IDatabaseProbe
{
    Task<bool> IsReachableAsync(CancellationToken cancellationToken);
}

/// <summary>Used when no database is configured: there is no dependency to be degraded by.</summary>
public sealed class NoDatabaseProbe : IDatabaseProbe
{
    public Task<bool> IsReachableAsync(CancellationToken cancellationToken) => Task.FromResult(true);
}

/// <summary>
/// Runs <c>SELECT 1</c> with an overall 2-second bound (research R8). Failures are logged with the
/// request's trace id and reported as unreachable; no detail leaves this class (FR-014).
/// </summary>
public sealed partial class NpgsqlDatabaseProbe(NpgsqlDataSource dataSource, ILogger<NpgsqlDatabaseProbe> logger)
    : IDatabaseProbe
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    public async Task<bool> IsReachableAsync(CancellationToken cancellationToken)
    {
        // Cancelling a query against a hung server can itself hang (the cancel request goes to the
        // same server), so the bound is enforced on the wait, not only through cancellation.
        var query = QueryAsync(cancellationToken);
        try
        {
            await query.WaitAsync(Timeout, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            // The abandoned query is cancelled by its own token; observe its eventual failure.
            _ = query.ContinueWith(static t => t.Exception, TaskScheduler.Default);
            LogTimedOut(logger, Timeout.TotalSeconds);
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogTimedOut(logger, Timeout.TotalSeconds);
            return false;
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException)
        {
            LogUnreachable(logger, ex);
            return false;
        }
    }

    private async Task QueryAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        await using var command = dataSource.CreateCommand("SELECT 1");
        await command.ExecuteScalarAsync(timeout.Token).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Database probe timed out after {Seconds} s")]
    private static partial void LogTimedOut(ILogger logger, double seconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Database probe failed")]
    private static partial void LogUnreachable(ILogger logger, Exception exception);
}
