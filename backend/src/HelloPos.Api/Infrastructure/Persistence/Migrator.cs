using System.Text.RegularExpressions;
using HelloPos.Api.Infrastructure.Identity;
using HelloPos.Api.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace HelloPos.Api.Infrastructure.Persistence;

/// <summary>Connection strings for one database: setup (admin), schema owner (migrator), API (app).</summary>
public sealed record MigratorSettings(string AdminConnectionString, string MigratorConnectionString, string AppConnectionString);

public sealed class MigrationFailedException : Exception
{
    public MigrationFailedException()
    {
    }

    public MigrationFailedException(string message)
        : base(message)
    {
    }

    public MigrationFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Applies the schema (research R2, R3): an idempotent bootstrap as the admin login creates the roles,
/// then each pending migration runs as the migrator login, which owns schemas <c>pos</c> and
/// <c>identity</c> (feature 005). The API
/// process never runs this; it runs as <c>HelloPos.Api migrate</c>, in tests, and in deploy jobs.
/// </summary>
public sealed partial class Migrator(ILogger<Migrator> logger)
{
    public const string AppGroupRole = "pos_app";

    /// <summary>Bootstrap, then migrate. Returns the migrations applied by this run.</summary>
    public async Task<IReadOnlyList<string>> RunAsync(MigratorSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await BootstrapAsync(settings, cancellationToken).ConfigureAwait(false);

        var options = new DbContextOptionsBuilder<HelloPosDbContext>()
            .UseHelloPosDatabase(settings.MigratorConnectionString)
            .Options;
        await using var context = new HelloPosDbContext(options, NoCurrentBusiness.Instance);
        var applied = new List<string>(await MigrateAsync(context, cancellationToken).ConfigureAwait(false));

        var identityOptions = new DbContextOptionsBuilder<OwnerIdentityDbContext>()
            .UseOwnerIdentityDatabase(settings.MigratorConnectionString)
            .Options;
        await using var identity = new OwnerIdentityDbContext(identityOptions);
        applied.AddRange(await MigrateAsync(identity, cancellationToken).ConfigureAwait(false));
        return applied;
    }

    /// <summary>
    /// Creates or updates the roles, as the admin login. Safe to run any number of times; also upgrades
    /// the feature 002 app login (<c>INHERIT</c>, member of <see cref="AppGroupRole"/>).
    /// </summary>
    public async Task BootstrapAsync(MigratorSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var migrator = new NpgsqlConnectionStringBuilder(settings.MigratorConnectionString);
        var app = new NpgsqlConnectionStringBuilder(settings.AppConnectionString);
        var database = Identifier(app.Database);
        var migratorRole = Identifier(migrator.Username);
        var appRole = Identifier(app.Username);

        var sql = $"""
            DO $$
            BEGIN
              IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '{AppGroupRole}') THEN
                CREATE ROLE {AppGroupRole} NOLOGIN;
              END IF;
              IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = {Literal(migrator.Username)}) THEN
                CREATE ROLE {migratorRole} LOGIN;
              END IF;
              IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = {Literal(app.Username)}) THEN
                CREATE ROLE {appRole} LOGIN;
              END IF;
            END
            $$;
            ALTER ROLE {migratorRole} WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS PASSWORD {Literal(migrator.Password)};
            ALTER ROLE {appRole} WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS INHERIT PASSWORD {Literal(app.Password)};
            GRANT {AppGroupRole} TO {appRole};
            GRANT CONNECT, CREATE ON DATABASE {database} TO {migratorRole};
            GRANT CONNECT ON DATABASE {database} TO {appRole};
            """;

        await using var connection = new NpgsqlConnection(ConnectionStrings.Normalize(settings.AdminConnectionString));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        LogBootstrapped(logger, app.Database ?? string.Empty);
    }

    /// <summary>
    /// Applies pending migrations one by one, each in its own transaction, so a failure names the
    /// migration and leaves none of its changes. Stops first if the database records a migration
    /// that this code does not contain.
    /// </summary>
    public async Task<IReadOnlyList<string>> MigrateAsync(DbContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var known = context.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);
        var applied = await context.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false);
        var unknown = applied.Where(m => !known.Contains(m)).ToList();
        if (unknown.Count > 0)
        {
            throw new MigrationFailedException(
                $"The database records migrations this code does not contain: {string.Join(", ", unknown)}. " +
                "Applied migrations are never edited or removed; deploy the code that contains them.");
        }

        var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).ToList();
        var migrator = context.GetService<IMigrator>();
        foreach (var migration in pending)
        {
            try
            {
                await migrator.MigrateAsync(migration, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or DbUpdateException)
            {
                throw new MigrationFailedException($"Migration {migration} failed; none of its changes were applied.", ex);
            }

            LogApplied(logger, migration);
        }

        if (pending.Count == 0)
        {
            LogUpToDate(logger);
        }

        return pending;
    }

    private static string Identifier(string? name)
    {
        if (name is null || !SafeName().IsMatch(name))
        {
            throw new ArgumentException($"Invalid PostgreSQL name '{name}'. Use lower case letters, digits and underscores.");
        }

        return $"\"{name}\"";
    }

    private static string Literal(string? value) => $"'{(value ?? string.Empty).Replace("'", "''", StringComparison.Ordinal)}'";

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$")]
    private static partial Regex SafeName();

    [LoggerMessage(Level = LogLevel.Information, Message = "Database roles bootstrapped for {Database}")]
    private static partial void LogBootstrapped(ILogger logger, string database);

    [LoggerMessage(Level = LogLevel.Information, Message = "Applied migration {Migration}")]
    private static partial void LogApplied(ILogger logger, string migration);

    [LoggerMessage(Level = LogLevel.Information, Message = "Database schema is up to date")]
    private static partial void LogUpToDate(ILogger logger);
}
