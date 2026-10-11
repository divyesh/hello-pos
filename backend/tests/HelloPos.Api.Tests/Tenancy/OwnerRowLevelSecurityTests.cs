using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Tests.Infrastructure.Database;
using Npgsql;

namespace HelloPos.Api.Tests.Tenancy;

/// <summary>
/// Feature 005, research R6: a signed-in owner (session setting <c>pos.owner_id</c>) can read exactly
/// the businesses and ownership links that are theirs, and cannot write anything without acting as
/// the business. Raw SQL as the app login, with no application code involved.
/// </summary>
[Collection(PostgresTests.Name)]
public sealed class OwnerRowLevelSecurityTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Owner_SeesOnlyTheirBusinessesAndLinks_AndCannotWrite()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        var a = await app.CreateBusinessAsync("A");
        var b = await app.CreateBusinessAsync("B");
        var ownerOfA = Guid.CreateVersion7();
        var ownerOfB = Guid.CreateVersion7();
        await LinkAsync(app, a, ownerOfA);
        await LinkAsync(app, b, ownerOfB);

        await using var session = await OpenAsAsync(app, business: null, owner: ownerOfA);

        Assert.Equal([a], await IdsAsync(session, "select id from pos.businesses"));
        Assert.Equal(1L, await ScalarAsync<long>(session, "select count(*) from pos.business_owners"));
        Assert.Equal(0, await ExecAsync(session, $"update pos.businesses set name = 'x' where id = '{a}'"));
        Assert.Equal(0, await ExecAsync(session, $"update pos.businesses set name = 'x' where id = '{b}'"));
        var plant = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(session,
            $"insert into pos.business_owners (business_id, id, owner_id) values ('{b}', '{Guid.CreateVersion7()}', '{ownerOfA}')"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, plant.SqlState);
        Assert.Equal(0, await ExecAsync(session, $"delete from pos.business_owners where owner_id = '{ownerOfA}'"));
    }

    [Fact]
    public async Task NoOwnerAndNoBusiness_SeesNothing()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        var a = await app.CreateBusinessAsync("A");
        await LinkAsync(app, a, Guid.CreateVersion7());

        await using var session = await OpenAsAsync(app, business: null, owner: null);

        Assert.Equal(0L, await ScalarAsync<long>(session, "select count(*) from pos.businesses"));
        Assert.Equal(0L, await ScalarAsync<long>(session, "select count(*) from pos.business_owners"));
    }

    [Fact]
    public async Task ActingAsTheBusiness_CanLinkAnOwner_AndOtherBusinessesStayHidden()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        var a = await app.CreateBusinessAsync("A");
        var b = await app.CreateBusinessAsync("B");
        await LinkAsync(app, b, Guid.CreateVersion7());

        await using var session = await OpenAsAsync(app, business: a, owner: null);
        await ExecAsync(session,
            $"insert into pos.business_owners (business_id, id, owner_id) values ('{a}', '{Guid.CreateVersion7()}', '{Guid.CreateVersion7()}')");

        Assert.Equal(1L, await ScalarAsync<long>(session, "select count(*) from pos.business_owners"));
        Assert.Equal([a], await IdsAsync(session, "select id from pos.businesses"));
    }

    internal static async Task LinkAsync(TenancyApp app, Guid business, Guid owner)
    {
        await using var session = await OpenAsAsync(app, business, owner: null);
        await ExecAsync(session,
            $"insert into pos.business_owners (business_id, id, owner_id) values ('{business}', '{Guid.CreateVersion7()}', '{owner}')");
    }

    private static async Task<NpgsqlConnection> OpenAsAsync(TenancyApp app, Guid? business, Guid? owner)
    {
        var connection = new NpgsqlConnection(ConnectionStrings.Normalize(app.AppConnectionString));
        await connection.OpenAsync(Ct);
        await using var set = new NpgsqlCommand(
            "select set_config('pos.business_id', @business, false), set_config('pos.owner_id', @owner, false)", connection);
        set.Parameters.AddWithValue("business", business?.ToString() ?? string.Empty);
        set.Parameters.AddWithValue("owner", owner?.ToString() ?? string.Empty);
        await set.ExecuteNonQueryAsync(Ct);
        return connection;
    }

    private static async Task<List<Guid>> IdsAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var ids = new List<Guid>();
        while (await reader.ReadAsync(Ct))
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
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
