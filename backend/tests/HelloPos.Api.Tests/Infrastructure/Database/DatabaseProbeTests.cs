using System.Diagnostics;
using HelloPos.Api.Infrastructure.Database;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace HelloPos.Api.Tests.Infrastructure.Database;

/// <summary>FR-013: the probe answers within its 2-second bound and never throws.</summary>
[Collection(PostgresTests.Name)]
public sealed class DatabaseProbeTests(PostgresFixture postgres)
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(3);

    [Fact]
    public async Task ReachableDatabase_IsReachable()
    {
        Assert.True(await Probe(postgres.ConnectionString).IsReachableAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task HangingDatabase_IsUnreachable_WithinLimit()
    {
        var probe = Probe(postgres.ConnectionString);
        Assert.True(await probe.IsReachableAsync(TestContext.Current.CancellationToken), "precondition: reachable");

        await postgres.PauseAsync();
        try
        {
            var watch = Stopwatch.StartNew();
            var reachable = await probe.IsReachableAsync(TestContext.Current.CancellationToken);
            watch.Stop();

            Assert.False(reachable);
            Assert.True(watch.Elapsed < Limit, $"probe took {watch.Elapsed}");
        }
        finally
        {
            await postgres.UnpauseAsync();
        }

        Assert.True(await probe.IsReachableAsync(TestContext.Current.CancellationToken), "reachable again after the outage");
    }

    [Fact]
    public async Task WrongPassword_IsUnreachable()
    {
        var builder = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Password = "wrong" };

        Assert.False(await Probe(builder.ConnectionString).IsReachableAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NothingListening_IsUnreachable_WithinLimit()
    {
        var builder = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Port = 1 };

        var watch = Stopwatch.StartNew();
        Assert.False(await Probe(builder.ConnectionString).IsReachableAsync(TestContext.Current.CancellationToken));
        Assert.True(watch.Elapsed < Limit, $"probe took {watch.Elapsed}");
    }

    private static NpgsqlDatabaseProbe Probe(string connectionString) =>
        new(DatabaseSetup.CreateProbeDataSource(connectionString), NullLogger<NpgsqlDatabaseProbe>.Instance);
}
