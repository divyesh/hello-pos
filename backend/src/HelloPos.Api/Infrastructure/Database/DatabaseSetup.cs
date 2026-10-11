using Npgsql;

namespace HelloPos.Api.Infrastructure.Database;

/// <summary>
/// Database wiring. For now only the health probe uses the database (feature 002); the application
/// data source, EF Core and tenancy arrive with roadmap #3.
/// </summary>
public static class DatabaseSetup
{
    public const string ConnectionStringName = "HelloPos";

    public static IServiceCollection AddHelloPosDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return services.AddSingleton<IDatabaseProbe, NoDatabaseProbe>();
        }

        return services.AddSingleton<IDatabaseProbe>(sp => new NpgsqlDatabaseProbe(
            CreateProbeDataSource(connectionString),
            sp.GetRequiredService<ILogger<NpgsqlDatabaseProbe>>()));
    }

    /// <summary>
    /// A small data source dedicated to the probe, tuned so a hung or unreachable server cannot hold
    /// the health check past its bound: short connect and command timeouts, and no waiting on
    /// cancellation requests (a hung server would not answer them).
    /// </summary>
    public static NpgsqlDataSource CreateProbeDataSource(string connectionString)
    {
        var timeoutSeconds = (int)NpgsqlDatabaseProbe.Timeout.TotalSeconds;
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Timeout = timeoutSeconds,
            CommandTimeout = timeoutSeconds,
            CancellationTimeout = -1,
            MaxPoolSize = 2,
            ApplicationName = "hello-pos-api-health",
        };
        return NpgsqlDataSource.Create(builder);
    }
}
