using HelloPos.Api.Infrastructure.Identity;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Infrastructure.Tenancy;
using HelloPos.Api.Tests.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace HelloPos.Api.Tests.Persistence;

/// <summary>Feature 006 research R1: a database with feature 005 owners keeps them, as owner staff.</summary>
[Collection(PostgresTests.Name)]
public sealed class StaffMigrationTests(PostgresFixture postgres)
{
    private const string LastMigrationOf005 = "20261011021500_AddBusinessOwnership";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OwnersFrom005_BecomeActiveOwnerStaff_AndTheOldTableIsGone()
    {
        var db = await postgres.CreateEmptyDatabaseAsync();
        var migrator = new Migrator(NullLogger<Migrator>.Instance);
        await migrator.BootstrapAsync(db.Settings, Ct);
        await using (var identity = new OwnerIdentityDbContext(
                         new DbContextOptionsBuilder<OwnerIdentityDbContext>().UseOwnerIdentityDatabase(db.Migrator).Options))
        {
            await migrator.MigrateAsync(identity, Ct);
        }

        await using (var pos = new HelloPosDbContext(
                         new DbContextOptionsBuilder<HelloPosDbContext>().UseHelloPosDatabase(db.Migrator).Options,
                         NoCurrentBusiness.Instance))
        {
            await pos.GetService<IMigrator>().MigrateAsync(LastMigrationOf005, cancellationToken: Ct);
        }

        var account = Guid.CreateVersion7();
        var business = Guid.CreateVersion7();
        await ExecAsync(db.Migrator, $"""
            insert into identity.owner_accounts (id, display_name, email, normalized_email, user_name, normalized_user_name,
              email_confirmed, phone_number_confirmed, two_factor_enabled, lockout_enabled, access_failed_count)
            values ('{account}', 'Asha Patel', 'a@example.com', 'A@EXAMPLE.COM', 'a@example.com', 'A@EXAMPLE.COM', true, false, false, true, 0);
            insert into pos.businesses (id, name, country_code, currency_code, pricing_mode, financial_year_start_month, time_zone)
            values ('{business}', 'Shop', 'IN', 'INR', 'tax_inclusive', 4, 'Asia/Kolkata');
            insert into pos.business_owners (business_id, id, owner_id) values ('{business}', '{Guid.CreateVersion7()}', '{account}');
            """);

        var applied = await migrator.RunAsync(db.Settings, Ct);

        Assert.Contains(applied, m => m.EndsWith("_AddStaff", StringComparison.Ordinal));
        Assert.Equal("Asha Patel|owner|true|1", await ScalarAsync<string>(db.Migrator,
            $"select display_name || '|' || role || '|' || active || '|' || version from pos.staff_members where business_id = '{business}' and account_id = '{account}'"));
        Assert.Equal(0L, await ScalarAsync<long>(db.Migrator,
            "select count(*) from information_schema.tables where table_schema = 'pos' and table_name = 'business_owners'"));
        Assert.Empty(await SchemaConventions.CheckAsync(db.Migrator));
    }

    private static async Task ExecAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionStrings.Normalize(connectionString));
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionStrings.Normalize(connectionString));
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(Ct))!;
    }
}
