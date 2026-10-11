using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Tests.Infrastructure.Database;
using HelloPos.Api.Tests.Tenancy.Samples;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HelloPos.Api.Tests.Tenancy;

/// <summary>
/// FR-014 / SC-007: the database alone keeps businesses apart. Raw SQL as the app login, with no
/// application scoping at all, sees and changes only the session's business.
/// </summary>
[Collection(PostgresTests.Name)]
public sealed class RowLevelSecurityTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RawSql_AsAppLogin_IsConfinedToTheSessionBusiness()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        var a = await app.CreateBusinessAsync("A");
        var b = await app.CreateBusinessAsync("B");
        var categoryOfA = await StampingAndReferencesTests.CreateCategoryAsync(app, a, "A1");
        var categoryOfB = await StampingAndReferencesTests.CreateCategoryAsync(app, b, "B1");

        await using var session = await OpenAsAsync(app, a);
        Assert.Equal(1L, await ScalarAsync<long>(session, "select count(*) from pos.sample_categories"));
        Assert.Equal(1L, await ScalarAsync<long>(session, "select count(*) from pos.businesses"));
        Assert.Equal(0, await ExecAsync(session, $"update pos.sample_categories set name = 'hacked' where id = '{categoryOfB}'"));
        Assert.Equal(0, await ExecAsync(session, $"delete from pos.sample_categories where id = '{categoryOfB}'"));
        var insert = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecAsync(session, $"insert into pos.sample_categories (business_id, id, name) values ('{b}', '{Guid.CreateVersion7()}', 'planted')"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, insert.SqlState); // RLS WITH CHECK
        Assert.Equal(1, await ExecAsync(session, $"update pos.sample_categories set name = 'mine' where id = '{categoryOfA}'"));

        await using var owner = app.OwnerDb();
        Assert.Equal("B1", (await owner.SampleCategories.AcrossAllBusinessesForTests().SingleAsync(c => c.Id == categoryOfB, Ct)).Name);
    }

    [Fact]
    public async Task WithoutASessionBusiness_NothingIsVisibleOrWritable()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        var a = await app.CreateBusinessAsync("A");
        await StampingAndReferencesTests.CreateCategoryAsync(app, a, "A1");

        await using var session = await OpenAsAsync(app, null);
        Assert.Equal(0L, await ScalarAsync<long>(session, "select count(*) from pos.sample_categories"));
        Assert.Equal(0L, await ScalarAsync<long>(session, "select count(*) from pos.businesses"));
        await Assert.ThrowsAsync<PostgresException>(() =>
            ExecAsync(session, $"insert into pos.sample_categories (business_id, id, name) values ('{a}', '{Guid.CreateVersion7()}', 'x')"));
    }

    [Fact]
    public async Task EvenWithTheApplicationScopeBypassed_TheDatabaseReturnsOnlyTheSessionBusiness()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        var a = await app.CreateBusinessAsync("A");
        var b = await app.CreateBusinessAsync("B");
        await StampingAndReferencesTests.CreateCategoryAsync(app, a, "A1");
        await StampingAndReferencesTests.CreateCategoryAsync(app, b, "B1");

        await using var db = app.Db(a);
        var names = await db.SampleCategories.AcrossAllBusinesses().Select(c => c.Name).ToListAsync(Ct);

        Assert.Equal(["A1"], names);
    }

    private static async Task<NpgsqlConnection> OpenAsAsync(TenancyApp app, Guid? business)
    {
        var connection = new NpgsqlConnection(ConnectionStrings.Normalize(app.AppConnectionString));
        await connection.OpenAsync(Ct);
        await using var set = new NpgsqlCommand("select set_config('pos.business_id', @id, false)", connection);
        set.Parameters.AddWithValue("id", business?.ToString() ?? string.Empty);
        await set.ExecuteNonQueryAsync(Ct);
        return connection;
    }

    private static async Task<int> ExecAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteNonQueryAsync(Ct);
    }

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(Ct))!;
    }
}
