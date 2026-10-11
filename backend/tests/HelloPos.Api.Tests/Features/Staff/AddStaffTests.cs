using System.Net;
using System.Text.Json.Nodes;
using HelloPos.Api.Tests.Infrastructure.Database;
using HelloPos.Api.Tests.Tenancy;
using HelloPos.Kernel.Staff;
using static HelloPos.Api.Tests.Features.Businesses.OpenAndEditTests;
using static HelloPos.Api.Tests.Features.Owners.OwnerApi;
using static HelloPos.Api.Tests.Features.Staff.StaffApi;

namespace HelloPos.Api.Tests.Features.Staff;

/// <summary>US1 add POS staff: FR-001, FR-002, FR-008, FR-009; SC-005.</summary>
[Collection(PostgresTests.Name)]
public sealed class AddStaffTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Owner_AddsACashier_WhoseStoredPinVerifies_AndIsNeverShown()
    {
        var (app, owner, shop) = await OwnerWithShopAsync();
        await using var _ = app;
        using var __ = owner;

        var body = await AssertContractAsync(await AddAsync(owner, shop, NewStaff("Ravi", "cashier", "Ravi", "4826")), HttpStatusCode.Created, "addStaff");

        var added = JsonNode.Parse(body)!;
        Assert.Equal("ravi", (string?)added["posUsername"]);
        Assert.Equal("cashier", (string?)added["role"]);
        Assert.True((bool?)added["active"]);
        Assert.True((bool?)added["hasPin"]);
        Assert.False((bool?)added["hasBackOfficeAccess"]);
        Assert.DoesNotContain("4826", body, StringComparison.Ordinal);
        Assert.True(await PinVerifiesAsync(app, shop, "RAVI", "4826"));
        Assert.False(await PinVerifiesAsync(app, shop, "ravi", "4827"));

        var list = JsonNode.Parse(await AssertContractAsync(await ListAsync(owner, shop), HttpStatusCode.OK, "listStaff"))!.AsArray();
        Assert.Equal(["Owner", "Ravi"], list.Select(s => (string)s!["displayName"]!).Order());
        Assert.DoesNotContain("4826", list.ToJsonString(), StringComparison.Ordinal);
        await AssertContractAsync(await GetAsync(owner, shop, Guid.Parse((string)added["id"]!)), HttpStatusCode.OK, "getStaff");
    }

    [Fact]
    public async Task Owner_AddsAPosManager_ManagerAddsOnlyCashiers()
    {
        var (app, owner, shop) = await OwnerWithShopAsync();
        await using var _ = app;
        using var __ = owner;
        Assert.Equal(HttpStatusCode.Created, (await AddAsync(owner, shop, NewStaff("Mina", "manager", "mina"))).StatusCode);

        var (manager, _) = await app.SignedInStaffClientAsync("manager@example.com", shop, StaffRole.Manager);
        using (manager)
        {
            await AssertContractAsync(await AddAsync(manager, shop, NewStaff("Ravi", "cashier", "ravi")), HttpStatusCode.Created, "addStaff");
            await AssertContractAsync(await AddAsync(manager, shop, NewStaff("Boss", "manager", "boss")), HttpStatusCode.Forbidden, "addStaff");
        }

        await AssertContractAsync(await AddAsync(owner, shop, NewStaff("Co", "owner", "coowner")), HttpStatusCode.Forbidden, "addStaff");
    }

    [Fact]
    public async Task UsernameIsUniquePerBusiness_CaseInsensitive()
    {
        var (app, owner, shop) = await OwnerWithShopAsync();
        await using var _ = app;
        using var __ = owner;
        await AddAsync(owner, shop, NewStaff("Ravi", "cashier", "ravi"));

        var body = await AssertContractAsync(await AddAsync(owner, shop, NewStaff("Other Ravi", "cashier", "RAVI")), HttpStatusCode.BadRequest, "addStaff");
        Assert.Contains("posUsername", ErrorFields(body));

        var other = await CreateIdAsync(owner, "Second shop");
        await OpenAsync(owner, other);
        Assert.Equal(HttpStatusCode.Created, (await AddAsync(owner, other, NewStaff("Ravi", "cashier", "ravi"))).StatusCode);
    }

    [Theory]
    [InlineData("displayName", "")]
    [InlineData("posUsername", "ab")]
    [InlineData("pin", "123")]
    [InlineData("pin", "1234")]
    [InlineData("pin", "0000")]
    [InlineData("role", "boss")]
    public async Task InvalidFields_AreReportedByName_AndNothingIsAdded(string field, string value)
    {
        var (app, owner, shop) = await OwnerWithShopAsync();
        await using var _ = app;
        using var __ = owner;
        var request = NewStaff("Ravi", "cashier", "ravi");
        request[field] = value;

        var body = await AssertContractAsync(await AddAsync(owner, shop, request), HttpStatusCode.BadRequest, "addStaff");

        Assert.Contains(field, ErrorFields(body));
        Assert.Single(JsonNode.Parse(await (await ListAsync(owner, shop, "all")).Content.ReadAsStringAsync(Ct))!.AsArray());
    }

    [Fact]
    public async Task List_FiltersByStatus()
    {
        var (app, owner, shop) = await OwnerWithShopAsync();
        await using var _ = app;
        using var __ = owner;
        await app.AddStaffAsync(shop, StaffRole.Cashier, displayName: "Gone", posUsername: "gone", active: false);

        async Task<string[]> NamesAsync(string status) =>
            JsonNode.Parse(await (await ListAsync(owner, shop, status)).Content.ReadAsStringAsync(Ct))!.AsArray()
                .Select(s => (string)s!["displayName"]!).Order().ToArray();

        Assert.Equal(["Owner"], await NamesAsync("active"));
        Assert.Equal(["Gone"], await NamesAsync("inactive"));
        Assert.Equal(["Gone", "Owner"], await NamesAsync("all"));
    }

    [Fact]
    public async Task WithoutAnOpenBusiness_StaffIs401()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        using var owner = await app.SignUpVerifiedOwnerAsync("owner@example.com");

        using var response = await owner.GetAsync(new Uri("/staff", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<(TenancyApp App, HttpClient Owner, Guid Shop)> OwnerWithShopAsync()
    {
        var app = await TenancyApp.CreateAsync(postgres);
        var owner = await app.SignUpVerifiedOwnerAsync("owner@example.com");
        var shop = await CreateIdAsync(owner, "Shop");
        await OpenAsync(owner, shop);
        return (app, owner, shop);
    }
}
