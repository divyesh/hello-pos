using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Tests.Infrastructure.Database;
using HelloPos.Api.Tests.Tenancy;
using Npgsql;

namespace HelloPos.Api.Tests.Persistence;

/// <summary>SC-001 / FR-020: the schema follows the conventions, and each rule really fires.</summary>
[Collection(PostgresTests.Name)]
public sealed class ConventionTests(PostgresFixture postgres)
{
    [Fact]
    public async Task MigratedSchema_IncludingSampleTables_FollowsEveryConvention()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);

        var violations = await SchemaConventions.CheckAsync(app.Logins.Migrator);

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Theory]
    [InlineData("no_business", "id uuid primary key", "business-id")]
    [InlineData("wrong_key", "business_id uuid not null, id uuid, primary key (id, business_id)", "composite-key")]
    [InlineData("no_rls", "business_id uuid not null, id uuid, primary key (business_id, id)", "row-security")]
    [InlineData("naive_time", "business_id uuid not null, id uuid, at timestamp, primary key (business_id, id)", "utc-time")]
    [InlineData("float_money", "business_id uuid not null, id uuid, price double precision, primary key (business_id, id)", "exact-money")]
    [InlineData("loose_numeric", "business_id uuid not null, id uuid, price numeric, primary key (business_id, id)", "exact-money")]
    [InlineData("int_id", "business_id uuid not null, id int, primary key (business_id, id)", "uuid-id")]
    public async Task EachRule_FiresOnATableThatBreaksIt(string table, string definition, string rule)
    {
        var db = await postgres.CreateEmptyDatabaseAsync();
        var withRowSecurity = rule != "row-security";
        await ExecAsync(db.Admin, $"""
            create schema broken;
            create table broken.{table} ({definition});
            {(withRowSecurity ? $"alter table broken.{table} enable row level security; create policy {RowSecurityMigrationExtensions.PolicyName} on broken.{table} using (true);" : string.Empty)}
            """);

        var violations = await SchemaConventions.CheckAsync(db.Admin, "broken");

        Assert.Contains(violations, v => v.Contains($"broken.{table}", StringComparison.Ordinal) && v.Contains($"[{rule}]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ForeignKeyWithoutBusinessId_Fires()
    {
        var db = await postgres.CreateEmptyDatabaseAsync();
        await ExecAsync(db.Admin, $"""
            create schema broken;
            create table broken.parent (business_id uuid not null, id uuid not null, primary key (business_id, id), unique (id));
            create table broken.child (business_id uuid not null, id uuid not null, parent_id uuid references broken.parent (id), primary key (business_id, id));
            """);

        var violations = await SchemaConventions.CheckAsync(db.Admin, "broken");

        Assert.Contains(violations, v => v.Contains("broken.child", StringComparison.Ordinal) && v.Contains("[composite-link]", StringComparison.Ordinal));
    }

    private static async Task ExecAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
