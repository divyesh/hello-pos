using System.Diagnostics;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Infrastructure.Tenancy;
using HelloPos.Api.Tests.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace HelloPos.Api.Tests.Persistence;

/// <summary>User Story 1: migrations by the migrator; the app login has row rights only (FR-001–FR-004).</summary>
[Collection(PostgresTests.Name)]
public sealed class MigrationTests(PostgresFixture postgres)
{
    private static readonly Migrator Migrator = new(NullLogger<Migrator>.Instance);

    [Fact]
    public async Task EmptyDatabase_IsMigrated_ThenSecondRunChangesNothing()
    {
        var db = await postgres.CreateEmptyDatabaseAsync();

        var watch = Stopwatch.StartNew();
        var first = await Migrator.RunAsync(db.Settings, TestContext.Current.CancellationToken);
        watch.Stop();
        var second = await Migrator.RunAsync(db.Settings, TestContext.Current.CancellationToken);

        Assert.NotEmpty(first);
        Assert.Empty(second);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(60), $"migration took {watch.Elapsed}"); // SC-005
        Assert.Equal(first.Count, await ScalarAsync<long>(db.Migrator, "select count(*) from pos.__ef_migrations_history")
            + await ScalarAsync<long>(db.Migrator, "select count(*) from identity.__ef_migrations_history"));
    }

    [Fact]
    public async Task AppLogin_CanReadAndWriteRows_ButCannotChangeSchema()
    {
        var db = await postgres.CreateEmptyDatabaseAsync();
        await Migrator.RunAsync(db.Settings, TestContext.Current.CancellationToken);
        var business = Guid.CreateVersion7();

        await using var app = await OpenAsync(db.App);
        await ExecAsync(app, $"select set_config('pos.business_id', '{business}', false)");
        await ExecAsync(app, $"""
            insert into pos.businesses (id, name, country_code, currency_code, pricing_mode, financial_year_start_month, time_zone)
            values ('{business}', 'Test', 'IN', 'INR', 'tax_inclusive', 4, 'Asia/Kolkata')
            """);
        await ExecAsync(app, $"update pos.businesses set name = 'Renamed' where id = '{business}'");
        Assert.Equal(1L, await ScalarAsync<long>(app, "select count(*) from pos.businesses"));
        await ExecAsync(app, $"delete from pos.businesses where id = '{business}'");
        await ExecAsync(app, $"""
            insert into identity.owner_accounts (id, display_name, email, normalized_email, user_name, normalized_user_name,
              email_confirmed, phone_number_confirmed, two_factor_enabled, lockout_enabled, access_failed_count)
            values ('{Guid.CreateVersion7()}', 'Owner', 'o@example.com', 'O@EXAMPLE.COM', 'o@example.com', 'O@EXAMPLE.COM',
              false, false, false, true, 0)
            """);
        await ExecAsync(app, "insert into identity.data_protection_keys (friendly_name, xml) values ('k', '<key/>')");

        foreach (var ddl in new[]
                 {
                     "create table pos.x (id int)",
                     "alter table pos.businesses add column y int",
                     "drop table pos.businesses",
                     "create table public.x (id int)",
                     "create table identity.x (id int)",
                     "alter table identity.owner_accounts add column y int",
                 })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(app, ddl));
            Assert.Contains(error.SqlState, new[] { PostgresErrorCodes.InsufficientPrivilege, "42501" });
        }
    }

    [Fact]
    public async Task FailingMigration_LeavesNoPartialChanges_AndIsNamed()
    {
        var db = await postgres.CreateEmptyDatabaseAsync();
        await Migrator.BootstrapAsync(db.Settings, TestContext.Current.CancellationToken);
        await using var context = FailingMigrationContext.Create(db.Migrator);

        var error = await Assert.ThrowsAsync<MigrationFailedException>(
            () => Migrator.MigrateAsync(context, TestContext.Current.CancellationToken));

        Assert.Contains(FailingMigration.Id, error.Message, StringComparison.Ordinal);
        Assert.Equal(0L, await ScalarAsync<long>(db.Migrator,
            "select count(*) from information_schema.tables where table_name = 'half_done'"));
    }

    [Fact]
    public async Task UnknownAppliedMigration_StopsTheRun()
    {
        var db = await postgres.CreateEmptyDatabaseAsync();
        await Migrator.RunAsync(db.Settings, TestContext.Current.CancellationToken);
        await using (var migrator = await OpenAsync(db.Migrator))
        {
            await ExecAsync(migrator, "insert into pos.__ef_migrations_history values ('20990101000000_NotInCode', '10.0.12')");
        }

        var error = await Assert.ThrowsAsync<MigrationFailedException>(
            () => Migrator.RunAsync(db.Settings, TestContext.Current.CancellationToken));

        Assert.Contains("20990101000000_NotInCode", error.Message, StringComparison.Ordinal);
    }

    private static async Task<NpgsqlConnection> OpenAsync(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return connection;
    }

    private static async Task ExecAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = await OpenAsync(connectionString);
        return await ScalarAsync<T>(connection, sql);
    }

    /// <summary>A context whose only migration creates a table and then fails (test-only).</summary>
    private sealed class FailingMigrationContext(DbContextOptions options) : HelloPosDbContext(options, NoCurrentBusiness.Instance)
    {
        public static FailingMigrationContext Create(string connectionString) =>
            new(new DbContextOptionsBuilder()
                .UseNpgsql(connectionString, npgsql => npgsql
                    .MigrationsHistoryTable(MigrationsHistoryTable, Schema)
                    .MigrationsAssembly(typeof(FailingMigrationContext).Assembly.GetName().Name))
                // This test-only context has no model snapshot by design.
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
                .Options);
    }

    [DbContext(typeof(FailingMigrationContext))]
    [Migration(Id)]
    private sealed class FailingMigration : Migration
    {
        public const string Id = "20990101000000_FailsHalfway";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("create table pos.half_done (id int)");
            migrationBuilder.Sql("select 1 / 0");
        }
    }
}
