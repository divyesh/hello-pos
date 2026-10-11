using HelloPos.Api.Infrastructure.Identity;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Infrastructure.Tenancy;
using Npgsql;

namespace HelloPos.Api.Infrastructure.Database;

/// <summary>
/// Database wiring for the API process: the health probe (feature 002) and the EF Core context with
/// the current business (feature 003), the owner identity context (feature 005). The API only ever receives the app login's connection string;
/// admin and migrator credentials exist only for <c>HelloPos.Api migrate</c>.
/// </summary>
public static class DatabaseSetup
{
    public const string ConnectionStringName = "HelloPos";

    public static IServiceCollection AddHelloPosDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentBusiness, ClaimsCurrentBusiness>();
        services.AddScoped<ICurrentOwner, ClaimsCurrentOwner>();

        var connectionString = configuration.GetConnectionString(ConnectionStringName);

        // Always registered, so endpoints and Identity's stores resolve even when the API runs without a
        // database (health and startup checks); using them then fails like any database call.
        services.AddDbContext<OwnerIdentityDbContext>(options => options.UseOwnerIdentityDatabase(connectionString));
        services.AddDbContext<HelloPosDbContext>(options => options.UseHelloPosDatabase(connectionString));
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
        var builder = new NpgsqlConnectionStringBuilder(Persistence.ConnectionStrings.Normalize(connectionString))
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
