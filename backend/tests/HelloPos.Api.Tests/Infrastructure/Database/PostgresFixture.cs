using HelloPos.Api.Infrastructure.Persistence;
using Npgsql;
using Testcontainers.PostgreSql;

namespace HelloPos.Api.Tests.Infrastructure.Database;

/// <summary>
/// One throwaway PostgreSQL (the same image as the local environment) shared by the database tests
/// (Constitution VI: real PostgreSQL, not a substitute). The container's superuser is used only for
/// setup, like the admin login in real environments; business data is always accessed as the app
/// login, because a superuser bypasses row-level security and would make tenancy tests lie.
/// Outages are simulated by pausing the container (the server hangs).
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string MigratorLogin = "hellopos_migrator";
    public const string MigratorPassword = "test-migrator";
    public const string AppLogin = "hellopos_app";
    public const string AppPassword = "test-app";

    // Many short-lived test databases, each with its own connection pools: allow more connections
    // than the default 100 (test container only).
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:18.6")
        .WithCommand("-c", "max_connections=500")
        .Build();

    /// <summary>Superuser connection to the default database (setup and the feature 002 probe tests).</summary>
    public string ConnectionString => container.GetConnectionString();

    public async ValueTask InitializeAsync() => await container.StartAsync().ConfigureAwait(false);

    public async ValueTask DisposeAsync() => await container.DisposeAsync().ConfigureAwait(false);

    public Task PauseAsync() => container.PauseAsync();

    public Task UnpauseAsync() => container.UnpauseAsync();

    /// <summary>Creates a new, empty database and returns the three logins' connection strings for it.</summary>
    public async Task<DatabaseLogins> CreateEmptyDatabaseAsync()
    {
        var name = "t_" + Guid.NewGuid().ToString("N")[..12];
        await using (var connection = new NpgsqlConnection(ConnectionString))
        {
            await connection.OpenAsync().ConfigureAwait(false);
            await using var command = new NpgsqlCommand($"CREATE DATABASE {name}", connection);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        return new DatabaseLogins(
            Admin: With(name, null, null),
            Migrator: With(name, MigratorLogin, MigratorPassword),
            App: With(name, AppLogin, AppPassword));
    }

    private string With(string database, string? user, string? password)
    {
        var builder = new NpgsqlConnectionStringBuilder(ConnectionString) { Database = database };
        if (user is not null)
        {
            builder.Username = user;
            builder.Password = password;
        }

        return builder.ConnectionString;
    }
}

/// <summary>Connection strings for one database: setup (admin), schema owner (migrator), API (app).</summary>
public sealed record DatabaseLogins(string Admin, string Migrator, string App)
{
    public MigratorSettings Settings => new(Admin, Migrator, App);
}

[CollectionDefinition(Name)]
public sealed class PostgresTests : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
