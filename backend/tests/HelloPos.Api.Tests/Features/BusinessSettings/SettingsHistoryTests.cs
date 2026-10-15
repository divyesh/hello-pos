using System.Net;
using System.Text.Json.Nodes;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Tests.Infrastructure.Database;
using HelloPos.Kernel.Staff;
using Npgsql;
using static HelloPos.Api.Tests.Features.BusinessSettings.SettingsApi;
using static HelloPos.Api.Tests.Features.Catalog.CatalogApi;
using static HelloPos.Api.Tests.Features.Owners.OwnerApi;

namespace HelloPos.Api.Tests.Features.BusinessSettings;

/// <summary>Feature #12 US5: the settings history (FR-016, SC-004, Constitution IV).</summary>
[Collection(PostgresTests.Name)]
public sealed class SettingsHistoryTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EveryChange_IsListedNewestFirst_WithWhoAndEachFieldFromAndTo()
    {
        await using var shop = await ShopAsync(postgres);
        var (upi, version) = await PaymentTypeAsync(shop.Owner, shop.Id, "UPI");
        shop.App.Clock.Advance(TimeSpan.FromMinutes(1));
        using (await UpdatePaymentTypeAsync(shop.Owner, shop.Id, upi, "UPI / QR", "digitalWallet", false, true, version))
        {
        }

        shop.App.Clock.Advance(TimeSpan.FromMinutes(1));
        var limit = SellingBody(await JsonAsync(await GetSellingAsync(shop.Owner, shop.Id)));
        limit["discountLimitPercent"] = "10";
        using (await UpdateSellingAsync(shop.Owner, shop.Id, limit))
        {
        }

        shop.App.Clock.Advance(TimeSpan.FromMinutes(1));
        var tables = SellingBody(await JsonAsync(await GetSellingAsync(shop.Manager, shop.Id)));
        tables["usesTables"] = true;
        using (await UpdateSellingAsync(shop.Manager, shop.Id, tables))
        {
        }

        // Managers read the history too.
        var history = (await JsonAsync(await AssertContractAsync(await ListChangesAsync(shop.Manager, shop.Id), HttpStatusCode.OK, "listSettingsChanges")))
            .AsArray();

        Assert.Equal(["selling:updated", "selling:updated", "paymentType:updated", "paymentType:created"],
            history.Select(c => $"{(string)c!["entity"]!}:{(string)c["action"]!}"));
        Assert.Equal(["manager", "Owner", "Owner", "Owner"], history.Select(c => (string)c!["actor"]!));
        Assert.Equal(shop.App.Clock.GetUtcNow().UtcDateTime,
            DateTimeOffset.Parse((string)history[0]!["at"]!, System.Globalization.CultureInfo.InvariantCulture).UtcDateTime, TimeSpan.FromSeconds(30));
        Assert.Equal(("usesTables", "false", "true"), Field(history[0]!, "usesTables"));
        Assert.Equal(("discountLimitPercent", "0", "10"), Field(history[1]!, "discountLimitPercent"));
        Assert.Single(history[1]!["changes"]!.AsArray());
        Assert.Equal(("name", "UPI", "UPI / QR"), Field(history[2]!, "name"));
        Assert.Equal(upi, Guid.Parse((string)history[2]!["entityId"]!));
        Assert.Null(history[3]!["changes"]!.AsArray().Single(c => (string)c!["field"]! == "name")!["old"]);
    }

    [Fact]
    public async Task OneItemsHistory_IsListedByItsId()
    {
        await using var shop = await ShopAsync(postgres);
        var (upi, version) = await PaymentTypeAsync(shop.Owner, shop.Id, "UPI");
        await PaymentTypeAsync(shop.Owner, shop.Id, "Cheque", "cheque");
        shop.App.Clock.Advance(TimeSpan.FromMinutes(1));
        using (await SetPaymentTypeActiveAsync(shop.Owner, shop.Id, upi, false, version))
        {
        }

        var history = (await JsonAsync(await AssertContractAsync(
            await ListChangesAsync(shop.Owner, shop.Id, $"?entityId={upi}"), HttpStatusCode.OK, "listSettingsChanges"))).AsArray();

        Assert.Equal(["deactivated", "created"], history.Select(c => (string)c!["action"]!));
        Assert.All(history, c => Assert.Equal(upi, Guid.Parse((string)c!["entityId"]!)));
        Assert.Empty((await JsonAsync(await ListChangesAsync(shop.Owner, shop.Id, $"?entityId={Guid.CreateVersion7()}"))).AsArray());
    }

    [Fact]
    public async Task History_ComesInPagesOfFifty()
    {
        await using var shop = await ShopAsync(postgres);
        await using (var connection = new NpgsqlConnection(shop.App.Logins.Migrator))
        {
            await connection.OpenAsync(Ct);
            await using var insert = new NpgsqlCommand("""
                insert into pos.settings_changes (business_id, id, at, actor_staff_id, entity, entity_id, action, changes)
                select @b, gen_random_uuid(), now() - i * interval '1 minute',
                       (select id from pos.staff_members where business_id = @b order by id limit 1),
                       'tableArea', gen_random_uuid(), 'created', jsonb_build_object('name', jsonb_build_array(null, 'Area ' || i))
                from generate_series(1, 55) as i
                """, connection);
            insert.Parameters.AddWithValue("b", shop.Id);
            await insert.ExecuteNonQueryAsync(Ct);
        }

        var first = (await JsonAsync(await ListChangesAsync(shop.Owner, shop.Id))).AsArray();
        var second = (await JsonAsync(await ListChangesAsync(shop.Owner, shop.Id, "?page=2"))).AsArray();
        var past = (await JsonAsync(await ListChangesAsync(shop.Owner, shop.Id, "?page=3"))).AsArray();
        var invalid = await JsonAsync(await AssertContractAsync(await ListChangesAsync(shop.Owner, shop.Id, "?page=0"), HttpStatusCode.BadRequest, "listSettingsChanges"));

        Assert.Equal(50, first.Count);
        Assert.Equal(("name", null, "Area 1"), Field(first[0]!, "name"));
        Assert.Equal(["Area 51", "Area 52", "Area 53", "Area 54", "Area 55"], second.Select(c => Field(c!, "name").New));
        Assert.Empty(past);
        Assert.NotNull(invalid["errors"]!["page"]);
    }

    [Fact]
    public async Task Cashiers_CannotReachTheHistory()
    {
        await using var shop = await ShopAsync(postgres);
        using var cashier = await shop.App.SignUpVerifiedOwnerAsync("cashier@example.com");
        await shop.App.AddStaffAsync(shop.Id, StaffRole.Cashier, await Tenancy.TenancyApp.AccountIdAsync(cashier), "Cash");

        Assert.Equal(HttpStatusCode.Unauthorized, (await ListChangesAsync(cashier, shop.Id)).StatusCode);
    }

    [Fact]
    public async Task TheHistory_IsAppendOnly_ForTheAppLogin()
    {
        await using var shop = await ShopAsync(postgres);
        await PaymentTypeAsync(shop.Owner, shop.Id, "UPI");
        await using var session = new NpgsqlConnection(ConnectionStrings.Normalize(shop.App.AppConnectionString));
        await session.OpenAsync(Ct);
        await ExecAsync(session, $"select set_config('pos.business_id', '{shop.Id}', false)");

        Assert.Equal(1L, await ScalarAsync(session, "select count(*) from pos.settings_changes"));
        foreach (var sql in new[] { "update pos.settings_changes set action = 'updated'", "delete from pos.settings_changes" })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(session, sql));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
        }

        Assert.Single((await JsonAsync(await ListChangesAsync(shop.Owner, shop.Id))).AsArray());
    }

    private static (string Field, string? Old, string? New) Field(JsonNode change, string field)
    {
        var entry = change["changes"]!.AsArray().Single(c => (string)c!["field"]! == field)!;
        return (field, (string?)entry["old"], (string?)entry["new"]);
    }

    private static async Task ExecAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }

    private static async Task<long> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }
}
