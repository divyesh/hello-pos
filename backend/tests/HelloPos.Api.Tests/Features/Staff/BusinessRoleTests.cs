using System.Net;
using System.Text.Json.Nodes;
using HelloPos.Api.Tests.Infrastructure.Database;
using HelloPos.Api.Tests.Tenancy;
using HelloPos.Kernel.Staff;
using Npgsql;
using static HelloPos.Api.Tests.Features.Businesses.CreateBusinessTests;
using static HelloPos.Api.Tests.Features.Businesses.OpenAndEditTests;
using static HelloPos.Api.Tests.Features.Owners.OwnerApi;

namespace HelloPos.Api.Tests.Features.Staff;

/// <summary>Feature 006 research R3: roles on the 005 business endpoints, read from the database per request.</summary>
[Collection(PostgresTests.Name)]
public sealed class BusinessRoleTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Manager_ReadsSettings_ButCannotChangeThem()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        using var owner = await app.SignUpVerifiedOwnerAsync("owner@example.com");
        var shop = await CreateIdAsync(owner, "Shop");
        var (manager, _) = await app.SignedInStaffClientAsync("manager@example.com", shop, StaffRole.Manager);
        using (manager)
        {
            Assert.Equal(HttpStatusCode.OK, (await GetCurrentAsync(manager, shop)).StatusCode);
            await AssertContractAsync(
                await UpdateAsync(manager, Settings("Taken over", "IN", "INR", "tax_inclusive", 4, "Asia/Kolkata", 100), shop),
                HttpStatusCode.Forbidden, "updateCurrentBusiness");
            Assert.Equal("manager", (string?)JsonNode.Parse(await AssertContractAsync(await MeAsync(manager), HttpStatusCode.OK, "ownerMe"))!["currentRole"]);
        }

        await OpenAsync(owner, shop);
        Assert.Equal("Shop", (string?)JsonNode.Parse(await (await GetCurrentAsync(owner, shop)).Content.ReadAsStringAsync(Ct))!["name"]);
    }

    [Fact]
    public async Task Owner_HasRoleOwner_AndTheListShowsRoles()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        using var owner = await app.SignUpVerifiedOwnerAsync("owner@example.com");
        var shop = await CreateIdAsync(owner, "Shop");
        await OpenAsync(owner, shop);

        Assert.Equal("owner", (string?)JsonNode.Parse(await (await MeAsync(owner)).Content.ReadAsStringAsync(Ct))!["currentRole"]);
        var (manager, _) = await app.SignedInStaffClientAsync("manager@example.com", shop, StaffRole.Manager);
        using (manager)
        {
            var list = JsonNode.Parse(await AssertContractAsync(await ListAsync(manager), HttpStatusCode.OK, "ownerListBusinesses"))!.AsArray();
            Assert.Equal("manager", (string?)Assert.Single(list)!["role"]);
        }
    }

    [Fact]
    public async Task DeactivatedManager_IsRefusedOnTheNextRequest_AndCannotOpenAgain()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        using var owner = await app.SignUpVerifiedOwnerAsync("owner@example.com");
        var shop = await CreateIdAsync(owner, "Shop");
        var (manager, staffId) = await app.SignedInStaffClientAsync("manager@example.com", shop, StaffRole.Manager);
        using (manager)
        {
            Assert.Equal(HttpStatusCode.OK, (await GetCurrentAsync(manager, shop)).StatusCode);

            await using (var migrator = new NpgsqlConnection(app.Logins.Migrator))
            {
                await migrator.OpenAsync(Ct);
                await using var command = new NpgsqlCommand($"update pos.staff_members set active = false where id = '{staffId}'", migrator);
                await command.ExecuteNonQueryAsync(Ct);
            }

            Assert.Equal(HttpStatusCode.Forbidden, (await GetCurrentAsync(manager, shop)).StatusCode);
            Assert.Null(JsonNode.Parse(await (await MeAsync(manager)).Content.ReadAsStringAsync(Ct))!["currentRole"]);
            Assert.Equal(HttpStatusCode.NotFound, (await OpenAsync(manager, shop)).StatusCode);
            Assert.Equal("[]", await (await ListAsync(manager)).Content.ReadAsStringAsync(Ct));
        }
    }

    [Fact]
    public async Task CashierAccount_HasNoBackOffice()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        using var owner = await app.SignUpVerifiedOwnerAsync("owner@example.com");
        var shop = await CreateIdAsync(owner, "Shop");
        using var cashier = await app.SignUpVerifiedOwnerAsync("cashier@example.com");
        await app.AddStaffAsync(shop, StaffRole.Cashier, await TenancyApp.AccountIdAsync(cashier));

        Assert.Equal("[]", await (await ListAsync(cashier)).Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.NotFound, (await OpenAsync(cashier, shop)).StatusCode);
    }
}
