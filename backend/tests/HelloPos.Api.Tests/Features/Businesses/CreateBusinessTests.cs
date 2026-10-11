using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using HelloPos.Api.Tests.Infrastructure.Database;
using HelloPos.Api.Tests.Tenancy;
using Npgsql;
using static HelloPos.Api.Tests.Features.Owners.OwnerApi;

namespace HelloPos.Api.Tests.Features.Businesses;

/// <summary>US2 create businesses: FR-009–FR-012, FR-016, SC-005; research R6, R8.</summary>
[Collection(PostgresTests.Name)]
public sealed class CreateBusinessTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData("IN", "INR", "tax_inclusive", 4, "Asia/Kolkata", 100)]
    [InlineData("US", "USD", "tax_exclusive", 1, "America/New_York", 0)]
    [InlineData("CA", "CAD", "tax_exclusive", 1, "America/Toronto", 5)]
    public async Task Create_StoresTheSettings_AndMakesTheCreatorItsOwner(
        string country, string currency, string pricing, int month, string zone, long rounding)
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        using var owner = await app.SignUpVerifiedOwnerAsync("asha@example.com");
        var settings = Settings("Patel Grocers", country, currency, pricing, month, zone, rounding);

        var created = JsonNode.Parse(await AssertContractAsync(await CreateAsync(owner, settings), HttpStatusCode.Created, "ownerCreateBusiness"))!;

        foreach (var (key, value) in settings)
        {
            Assert.Equal(value!.ToJsonString(), created[key]!.ToJsonString());
        }

        var id = Guid.Parse((string)created["id"]!);
        Assert.Equal(await OwnerIdAsync(owner), await ScalarAsync<Guid>(app,
            $"select owner_id from pos.business_owners where business_id = '{id}'"));
        Assert.Equal(rounding / 100m, await ScalarAsync<decimal>(app,
            $"select cash_rounding_step from pos.businesses where id = '{id}'"));
    }

    [Theory]
    [InlineData("name", "")]
    [InlineData("countryCode", "FR")]
    [InlineData("currencyCode", "rupee")]
    [InlineData("pricingMode", "gross")]
    [InlineData("financialYearStartMonth", 13)]
    [InlineData("timeZone", "America/Toronto")]
    [InlineData("cashRoundingStepMinor", 25)]
    public async Task InvalidSettings_AreRejectedByField_AndNothingIsStored(string field, object value)
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        using var owner = await app.SignUpVerifiedOwnerAsync("asha@example.com");
        var settings = Settings("Patel Grocers", "IN", "INR", "tax_inclusive", 4, "Asia/Kolkata", 100);
        settings[field] = value switch
        {
            int number => JsonValue.Create(number),
            string text => JsonValue.Create(text),
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };

        var body = await AssertContractAsync(await CreateAsync(owner, settings), HttpStatusCode.BadRequest, "ownerCreateBusiness");

        Assert.Contains(field, ErrorFields(body));
        Assert.Equal(0L, await ScalarAsync<long>(app, "select count(*) from pos.businesses"));
    }

    [Fact]
    public async Task AnIdInTheRequest_IsIgnored()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        using var owner = await app.SignUpVerifiedOwnerAsync("asha@example.com");
        var chosen = Guid.CreateVersion7();
        var settings = Settings("Patel Grocers", "IN", "INR", "tax_inclusive", 4, "Asia/Kolkata", 100);
        settings["id"] = chosen.ToString();

        using var response = await CreateAsync(owner, settings);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotEqual(chosen, Guid.Parse((string)JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!["id"]!));
        Assert.Equal(0L, await ScalarAsync<long>(app, $"select count(*) from pos.businesses where id = '{chosen}'"));
    }

    [Fact]
    public async Task List_ShowsOnlyTheOwnersBusinesses()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        using var asha = await app.SignUpVerifiedOwnerAsync("asha@example.com");
        using var ben = await app.SignUpVerifiedOwnerAsync("ben@example.com");
        await CreateAsync(asha, Settings("Zeta Mart", "IN", "INR", "tax_inclusive", 4, "Asia/Kolkata", 100));
        await CreateAsync(asha, Settings("Alpha Store", "CA", "CAD", "tax_exclusive", 1, "America/Toronto", 5));
        await CreateAsync(ben, Settings("Ben's Diner", "US", "USD", "tax_exclusive", 1, "America/Chicago", 0));

        var ashas = JsonNode.Parse(await AssertContractAsync(await ListAsync(asha), HttpStatusCode.OK, "ownerListBusinesses"))!.AsArray();
        var bens = JsonNode.Parse(await AssertContractAsync(await ListAsync(ben), HttpStatusCode.OK, "ownerListBusinesses"))!.AsArray();

        Assert.Equal(["Alpha Store", "Zeta Mart"], ashas.Select(b => (string)b!["name"]!));
        Assert.Equal(["Ben's Diner"], bens.Select(b => (string)b!["name"]!));
    }

    [Fact]
    public async Task NewOwner_HasAnEmptyList()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        using var owner = await app.SignUpVerifiedOwnerAsync("asha@example.com");

        Assert.Equal("[]", await AssertContractAsync(await ListAsync(owner), HttpStatusCode.OK, "ownerListBusinesses"));
    }

    [Fact]
    public async Task WithoutASession_BothAre401()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        using var anonymous = app.OwnerClient();

        await AssertContractAsync(await ListAsync(anonymous), HttpStatusCode.Unauthorized, "ownerListBusinesses");
        await AssertContractAsync(
            await CreateAsync(anonymous, Settings("X", "IN", "INR", "tax_inclusive", 4, "Asia/Kolkata", 100)),
            HttpStatusCode.Unauthorized, "ownerCreateBusiness");
    }

    [Fact]
    public async Task UnverifiedOwner_CannotCreate()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        using var owner = await app.SignUpVerifiedOwnerAsync("asha@example.com");
        await ExecAsMigratorAsync(app, "update identity.owner_accounts set email_confirmed = false");

        await AssertContractAsync(
            await CreateAsync(owner, Settings("X", "IN", "INR", "tax_inclusive", 4, "Asia/Kolkata", 100)),
            HttpStatusCode.Forbidden, "ownerCreateBusiness");
        Assert.Equal(0L, await ScalarAsync<long>(app, "select count(*) from pos.businesses"));
    }

    [Fact]
    public async Task WithoutTheClientHeader_IsRefused()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        using var owner = await app.SignUpVerifiedOwnerAsync("asha@example.com");
        owner.DefaultRequestHeaders.Remove("X-HelloPos-Client");

        await AssertContractAsync(
            await CreateAsync(owner, Settings("X", "IN", "INR", "tax_inclusive", 4, "Asia/Kolkata", 100)),
            HttpStatusCode.Forbidden, "ownerCreateBusiness");
    }

    internal static JsonObject Settings(string name, string country, string currency, string pricing, int month, string zone, long rounding) =>
        new()
        {
            ["name"] = name,
            ["countryCode"] = country,
            ["currencyCode"] = currency,
            ["pricingMode"] = pricing,
            ["financialYearStartMonth"] = month,
            ["timeZone"] = zone,
            ["cashRoundingStepMinor"] = rounding,
        };

    internal static Task<HttpResponseMessage> CreateAsync(HttpClient client, JsonObject settings) =>
        client.PostAsJsonAsync("/owner/businesses", settings, Ct);

    internal static Task<HttpResponseMessage> ListAsync(HttpClient client) =>
        client.GetAsync(new Uri("/owner/businesses", UriKind.Relative), Ct);

    private static async Task<Guid> OwnerIdAsync(HttpClient owner) =>
        Guid.Parse((string)JsonNode.Parse(await (await MeAsync(owner)).Content.ReadAsStringAsync(Ct))!["ownerId"]!);

    /// <summary>A scalar read as the migrator (table owner: row-level security does not apply).</summary>
    private static async Task<T> ScalarAsync<T>(TenancyApp app, string sql)
    {
        await using var connection = new NpgsqlConnection(app.Logins.Migrator);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(Ct))!;
    }

    private static async Task ExecAsMigratorAsync(TenancyApp app, string sql)
    {
        await using var connection = new NpgsqlConnection(app.Logins.Migrator);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }
}
