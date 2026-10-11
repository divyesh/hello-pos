using System.Security.Cryptography;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Tests.Infrastructure.Database;
using Npgsql;

namespace HelloPos.Api.Tests.Tenancy;

/// <summary>
/// Feature 006 (research R2, R7, R8), raw SQL as the app login with no application code: a signed-in
/// account (<c>pos.owner_id</c>) reads only its own staff rows and the businesses where it is an active
/// owner or manager; it writes nothing without acting as the business; the change log is append-only;
/// the invitation lookup function reveals a business only for a valid pending token.
/// </summary>
[Collection(PostgresTests.Name)]
public sealed class StaffRowLevelSecurityTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Account_SeesBusinessesWhereItIsAnActiveOwnerOrManager_Only()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        var owned = await app.CreateBusinessAsync("Owned");
        var managed = await app.CreateBusinessAsync("Managed");
        var asCashier = await app.CreateBusinessAsync("Cashier only");
        var inactive = await app.CreateBusinessAsync("Inactive manager");
        var others = await app.CreateBusinessAsync("Someone else's");
        var account = Guid.CreateVersion7();
        await AddStaffAsync(app, owned, "owner", account);
        await AddStaffAsync(app, managed, "manager", account);
        await AddStaffAsync(app, asCashier, "cashier", account);
        await AddStaffAsync(app, inactive, "manager", account, active: false);
        await AddStaffAsync(app, others, "owner", Guid.CreateVersion7());

        await using var session = await OpenAsAsync(app, business: null, owner: account);

        Assert.Equal([owned, managed], (await IdsAsync(session, "select id from pos.businesses")).Order());
        Assert.Equal(4L, await ScalarAsync<long>(session, "select count(*) from pos.staff_members"));
        Assert.Equal(0, await ExecAsync(session, $"update pos.businesses set name = 'x' where id = '{owned}'"));
        Assert.Equal(0, await ExecAsync(session, $"update pos.staff_members set role = 'owner' where account_id = '{account}'"));
        Assert.Equal(0, await ExecAsync(session, $"delete from pos.staff_members where account_id = '{account}'"));
    }

    [Fact]
    public async Task Account_CannotPlantStaffInABusinessItIsNotActingAs()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        var theirs = await app.CreateBusinessAsync("Theirs");
        var account = Guid.CreateVersion7();

        await using var session = await OpenAsAsync(app, business: null, owner: account);
        var plant = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(session,
            $"insert into pos.staff_members (business_id, id, display_name, role, account_id) values ('{theirs}', '{Guid.CreateVersion7()}', 'Me', 'owner', '{account}')"));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, plant.SqlState);
    }

    [Fact]
    public async Task NoAccountAndNoBusiness_SeesNothing()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        var a = await app.CreateBusinessAsync("A");
        await AddStaffAsync(app, a, "owner", Guid.CreateVersion7());

        await using var session = await OpenAsAsync(app, business: null, owner: null);

        Assert.Equal(0L, await ScalarAsync<long>(session, "select count(*) from pos.businesses"));
        Assert.Equal(0L, await ScalarAsync<long>(session, "select count(*) from pos.staff_members"));
    }

    [Fact]
    public async Task StaffChanges_AreAppendOnly()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        var a = await app.CreateBusinessAsync("A");
        var staff = await AddStaffAsync(app, a, "owner", Guid.CreateVersion7());
        await using var session = await OpenAsAsync(app, business: a, owner: null);

        await ExecAsync(session,
            $"insert into pos.staff_changes (business_id, id, staff_member_id, actor_staff_id, action) values ('{a}', '{Guid.CreateVersion7()}', '{staff}', '{staff}', 'added')");

        foreach (var sql in new[] { "update pos.staff_changes set action = 'x'", "delete from pos.staff_changes" })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(session, sql));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
        }
    }

    [Fact]
    public async Task InvitationLookup_RevealsTheBusiness_OnlyForAPendingUnexpiredToken()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        var a = await app.CreateBusinessAsync("A");
        var inviter = await AddStaffAsync(app, a, "owner", Guid.CreateVersion7());
        var pending = await InviteAsync(app, a, inviter, "pending", TimeSpan.FromDays(7));
        var revoked = await InviteAsync(app, a, inviter, "revoked", TimeSpan.FromDays(7));
        var expired = await InviteAsync(app, a, inviter, "pending", TimeSpan.FromDays(-1));

        await using var session = await OpenAsAsync(app, business: null, owner: null);

        Assert.Equal(a, await LookupAsync(session, pending));
        Assert.Null(await LookupAsync(session, revoked));
        Assert.Null(await LookupAsync(session, expired));
        Assert.Null(await LookupAsync(session, RandomNumberGenerator.GetBytes(32)));
        Assert.Equal(0L, await ScalarAsync<long>(session, "select count(*) from pos.staff_invitations"));
    }

    internal static async Task<Guid> AddStaffAsync(TenancyApp app, Guid business, string role, Guid? account, bool active = true)
    {
        var id = Guid.CreateVersion7();
        await using var session = await OpenAsAsync(app, business, owner: null);
        var accountSql = account is { } a ? $"'{a}'" : "null";
        var username = account is null ? $"'u{id.ToString("N")[..8]}'" : "null";
        var pin = account is null ? "'hash'" : "null";
        await ExecAsync(session, $"""
            insert into pos.staff_members (business_id, id, display_name, role, active, account_id, pos_username, pin_hash)
            values ('{business}', '{id}', 'Person', '{role}', {active}, {accountSql}, {username}, {pin})
            """);
        return id;
    }

    private static async Task<byte[]> InviteAsync(TenancyApp app, Guid business, Guid inviter, string status, TimeSpan expiresIn)
    {
        var token = RandomNumberGenerator.GetBytes(32);
        var hash = SHA256.HashData(token);
        await using var session = await OpenAsAsync(app, business, owner: null);
        await using var command = new NpgsqlCommand("""
            insert into pos.staff_invitations (business_id, id, email, normalized_email, role, display_name, token_hash, status, expires_at, invited_by_staff_id)
            values (@business, @id, @email, upper(@email), 'manager', 'Invited', @hash, @status, now() + @expires, @inviter)
            """, session);
        command.Parameters.AddWithValue("business", business);
        command.Parameters.AddWithValue("id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("email", $"{status}{Guid.NewGuid():N}@example.com");
        command.Parameters.AddWithValue("hash", hash);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("expires", expiresIn);
        command.Parameters.AddWithValue("inviter", inviter);
        await command.ExecuteNonQueryAsync(Ct);
        return token;
    }

    private static async Task<Guid?> LookupAsync(NpgsqlConnection session, byte[] token)
    {
        await using var command = new NpgsqlCommand("select pos.invitation_business(@hash)", session);
        command.Parameters.AddWithValue("hash", SHA256.HashData(token));
        return await command.ExecuteScalarAsync(Ct) is Guid id ? id : null;
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
