using Testcontainers.PostgreSql;

namespace HelloPos.Api.Tests.Infrastructure.Database;

/// <summary>
/// One throwaway PostgreSQL (the same image as the local environment) shared by the database tests
/// (Constitution VI: real PostgreSQL, not a substitute). Outages are simulated by pausing the
/// container, which makes the server hang: the hardest case for the probe's timeout.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:18.6")
        .Build();

    public string ConnectionString => container.GetConnectionString();

    public async ValueTask InitializeAsync() => await container.StartAsync().ConfigureAwait(false);

    public async ValueTask DisposeAsync() => await container.DisposeAsync().ConfigureAwait(false);

    public Task PauseAsync() => container.PauseAsync();

    public Task UnpauseAsync() => container.UnpauseAsync();
}

[CollectionDefinition(Name)]
public sealed class PostgresTests : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
