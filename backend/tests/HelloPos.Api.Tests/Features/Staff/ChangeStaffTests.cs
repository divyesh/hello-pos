using System.Net;
using System.Text.Json.Nodes;
using HelloPos.Api.Features.Staff;
using HelloPos.Api.Tests.Infrastructure.Database;
using HelloPos.Api.Tests.Tenancy;
using HelloPos.Kernel.Staff;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static HelloPos.Api.Tests.Features.Businesses.OpenAndEditTests;
using static HelloPos.Api.Tests.Features.Owners.OwnerApi;
using static HelloPos.Api.Tests.Features.Staff.StaffApi;

namespace HelloPos.Api.Tests.Features.Staff;

/// <summary>US2: change roles, rename, deactivate, reactivate, PINs; FR-003, FR-004, FR-007, FR-009, FR-010, FR-013; SC-004.</summary>
[Collection(PostgresTests.Name)]
public sealed class ChangeStaffTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Owner_ChangesRole_AndRenames_WithAudit()
    {
        var (app, owner, shop, ravi) = await WithCashierAsync();
        await using var _ = app;
        using var __ = owner;

        var body = await AssertContractAsync(await UpdateAsync(owner, shop, ravi, "Ravi Kumar", "manager", 1), HttpStatusCode.OK, "updateStaff");

        var saved = JsonNode.Parse(body)!;
        Assert.Equal("manager", (string?)saved["role"]);
        Assert.Equal("Ravi Kumar", (string?)saved["displayName"]);
        Assert.Equal(2, (int?)saved["version"]);
        Assert.Equal(["added", "renamed", "role_changed"], await AuditActionsAsync(app, ravi));
    }

    [Fact]
    public async Task Deactivate_StopsThePinAtOnce_ReactivateRestoresIt()
    {
        var (app, owner, shop, ravi) = await WithCashierAsync();
        await using var _ = app;
        using var __ = owner;
        Assert.True(await PinVerifiesAsync(app, shop, "ravi", "4826"));

        var off = JsonNode.Parse(await AssertContractAsync(await DeactivateAsync(owner, shop, ravi, 1), HttpStatusCode.OK, "deactivateStaff"))!;
        Assert.False((bool?)off["active"]);
        Assert.False(await PinVerifiesAsync(app, shop, "ravi", "4826"));
        Assert.Contains(ravi.ToString(), await (await ListAsync(owner, shop, "inactive")).Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        var on = JsonNode.Parse(await AssertContractAsync(await ReactivateAsync(owner, shop, ravi, 2), HttpStatusCode.OK, "reactivateStaff"))!;
        Assert.True((bool?)on["active"]);
        Assert.Equal(ravi.ToString(), (string?)on["id"]);
        Assert.True(await PinVerifiesAsync(app, shop, "ravi", "4826"));
    }

    [Fact]
    public async Task PinReset_ReplacesThePin_AndCanRenameTheUsername()
    {
        var (app, owner, shop, ravi) = await WithCashierAsync();
        await using var _ = app;
        using var __ = owner;

        var body = await AssertContractAsync(await SetPinAsync(owner, shop, ravi, "ravi.k", "9071", 1), HttpStatusCode.OK, "setStaffPin");

        Assert.DoesNotContain("9071", body, StringComparison.Ordinal);
        Assert.False(await PinVerifiesAsync(app, shop, "ravi", "4826"));
        Assert.False(await PinVerifiesAsync(app, shop, "ravi.k", "4826"));
        Assert.True(await PinVerifiesAsync(app, shop, "ravi.k", "9071"));
        var weak = await AssertContractAsync(await SetPinAsync(owner, shop, ravi, "ravi.k", "1111", 2), HttpStatusCode.BadRequest, "setStaffPin");
        Assert.Contains("pin", ErrorFields(weak));
    }

    [Fact]
    public async Task StaleVersion_IsRefused_AndNothingChanges()
    {
        var (app, owner, shop, ravi) = await WithCashierAsync();
        await using var _ = app;
        using var __ = owner;
        Assert.Equal(HttpStatusCode.OK, (await UpdateAsync(owner, shop, ravi, "Ravi K", "cashier", 1)).StatusCode);

        foreach (var stale in new[]
                 {
                     await UpdateAsync(owner, shop, ravi, "Other edit", "cashier", 1),
                     await DeactivateAsync(owner, shop, ravi, 1),
                     await SetPinAsync(owner, shop, ravi, "ravi", "9071", 1),
                 })
        {
            var body = await stale.Content.ReadAsStringAsync(Ct);
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Equal(StaffProblems.StaffChangedType, (string?)JsonNode.Parse(body)!["type"]);
        }

        var now = JsonNode.Parse(await (await GetAsync(owner, shop, ravi)).Content.ReadAsStringAsync(Ct))!;
        Assert.Equal("Ravi K", (string?)now["displayName"]);
        Assert.True((bool?)now["active"]);
        Assert.True(await PinVerifiesAsync(app, shop, "ravi", "4826"));
    }

    [Fact]
    public async Task OwnerRole_NeedsABackOfficeAccount()
    {
        var (app, owner, shop, ravi) = await WithCashierAsync();
        await using var _ = app;
        using var __ = owner;

        var body = await AssertContractAsync(await UpdateAsync(owner, shop, ravi, "Ravi", "owner", 1), HttpStatusCode.BadRequest, "updateStaff");

        Assert.Contains("role", ErrorFields(body));
    }

    [Fact]
    public async Task ThereIsNoDelete_AndTheAuditNeverHoldsAPin()
    {
        var (app, owner, shop, ravi) = await WithCashierAsync();
        await using var _ = app;
        using var __ = owner;
        await SetPinAsync(owner, shop, ravi, "ravi", "9071", 1);
        await DeactivateAsync(owner, shop, ravi, 2);

        using var delete = await SendAsync(owner, HttpMethod.Delete, $"/staff/{ravi}", shop);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, delete.StatusCode);
        Assert.Equal(["added", "pin_set", "deactivated"], await AuditActionsAsync(app, ravi));
        var values = await ScalarAsync<string>(app, $"select coalesce(string_agg(coalesce(old_value, '') || '|' || coalesce(new_value, ''), ';'), '') from pos.staff_changes where staff_member_id = '{ravi}'");
        Assert.DoesNotContain("4826", values, StringComparison.Ordinal);
        Assert.DoesNotContain("9071", values, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cashier_ChangesOwnPin_OnlyWithTheCurrentOne()
    {
        var (app, owner, shop, ravi) = await WithCashierAsync();
        await using var _ = app;
        using var __ = owner;
        await using var db = app.Db(shop);
        var credentials = new StaffCredentials(new PasswordHasher<StaffMember>(), app.Clock);
        var me = await db.StaffMembers.SingleAsync(s => s.Id == ravi, Ct);

        Assert.NotNull(credentials.ChangeOwnPin(me, "0000", "5937"));
        Assert.NotNull(credentials.ChangeOwnPin(me, "4826", "1234"));
        Assert.Null(credentials.ChangeOwnPin(me, "4826", "5937"));
        await db.SaveChangesAsync(Ct);

        Assert.True(await PinVerifiesAsync(app, shop, "ravi", "5937"));
        Assert.False(await PinVerifiesAsync(app, shop, "ravi", "4826"));
    }

    private async Task<(TenancyApp App, HttpClient Owner, Guid Shop, Guid Ravi)> WithCashierAsync()
    {
        var app = await TenancyApp.CreateAsync(postgres);
        var owner = await app.SignUpVerifiedOwnerAsync("owner@example.com");
        var shop = await CreateIdAsync(owner, "Shop");
        await OpenAsync(owner, shop);
        using var added = await AddAsync(owner, shop, NewStaff("Ravi", "cashier", "ravi", "4826"));
        var ravi = Guid.Parse((string)JsonNode.Parse(await added.Content.ReadAsStringAsync(Ct))!["id"]!);
        return (app, owner, shop, ravi);
    }

    internal static async Task<string[]> AuditActionsAsync(TenancyApp app, Guid staffId)
    {
        await using var connection = new NpgsqlConnection(app.Logins.Migrator);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            $"select action from pos.staff_changes where staff_member_id = '{staffId}' order by at, id", connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var actions = new List<string>();
        while (await reader.ReadAsync(Ct))
        {
            actions.Add(reader.GetString(0));
        }

        return [.. actions];
    }

    private static async Task<T> ScalarAsync<T>(TenancyApp app, string sql)
    {
        await using var connection = new NpgsqlConnection(app.Logins.Migrator);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(Ct))!;
    }
}
