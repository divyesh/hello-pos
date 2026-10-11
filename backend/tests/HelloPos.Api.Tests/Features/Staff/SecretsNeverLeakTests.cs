using System.Net;
using System.Text.Json.Nodes;
using HelloPos.Api.Tests.Infrastructure.Database;
using HelloPos.Api.Tests.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Sinks.InMemory;
using static HelloPos.Api.Tests.Features.Businesses.OpenAndEditTests;
using static HelloPos.Api.Tests.Features.Owners.OwnerApi;
using static HelloPos.Api.Tests.Features.Staff.StaffApi;

namespace HelloPos.Api.Tests.Features.Staff;

/// <summary>
/// Feature 006 SC-005 (and 005's "never log passwords, tokens or links"): a whole staff flow with every
/// log event captured; no PIN, password or invitation token appears in any response or log event.
/// </summary>
[Collection(PostgresTests.Name)]
public sealed class SecretsNeverLeakTests(PostgresFixture postgres)
{
    private const string Pin = "482613"; // 6 digits: a 4-digit PIN could match random hex in ids by chance
    private const string NewPin = "907153";
    private const string ChangedPin = "593718";
    private const string Password = "a long passphrase nobody uses";

    [Fact]
    public async Task StaffFlow_NeverExposesPinsPasswordsOrTokens()
    {
        var sink = new InMemorySink();
        await using var app = await TenancyApp.CreateAsync(postgres,
            configureServices: (services, _) => services.AddSingleton<ILogEventSink>(sink));
        var bodies = new List<string>();
        async Task<HttpResponseMessage> Keep(Task<HttpResponseMessage> call)
        {
            var response = await call;
            bodies.Add(await response.Content.ReadAsStringAsync(Ct));
            return response;
        }

        using var owner = await app.SignUpVerifiedOwnerAsync("owner@example.com", Password);
        var shop = await CreateIdAsync(owner, "Shop");
        await OpenAsync(owner, shop);
        var added = await Keep(AddAsync(owner, shop, NewStaff("Ravi", "cashier", "ravi", Pin)));
        var ravi = Guid.Parse((string)JsonNode.Parse(bodies[^1])!["id"]!);
        await Keep(SetPinAsync(owner, shop, ravi, "ravi", NewPin, 1));
        await Keep(SetPinAsync(owner, shop, ravi, "ravi", "111111", 2)); // refused: the error must not echo it
        await Keep(InviteAsync(owner, shop, "mina@example.com"));
        var token = InvitationTokenFor(app, "mina@example.com");
        using var mina = await app.SignUpVerifiedOwnerAsync("mina@example.com", Password);
        await Keep(PreviewAsync(mina, token));
        await Keep(AcceptAsync(mina, token));
        await Keep(AcceptAsync(mina, token)); // used: refused
        await Keep(PreviewAsync(mina, token + "x"));
        await Keep(StaffApi.ListAsync(owner, shop, "all"));
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        Assert.True(await PinVerifiesAsync(app, shop, "ravi", NewPin));
        Assert.True(await ChangeOwnPinAsync(app, shop, ravi, NewPin, ChangedPin));

        var logged = string.Join('\n', sink.LogEvents.Select(e =>
            e.RenderMessage(System.Globalization.CultureInfo.InvariantCulture) + " " + string.Join(' ', e.Properties.Select(p => p.Value.ToString())) + " " + e.Exception));
        Assert.NotEmpty(sink.LogEvents);
        foreach (var secret in new[] { Pin, NewPin, ChangedPin, "111111", Password, token })
        {
            Assert.DoesNotContain(secret, logged, StringComparison.Ordinal);
            Assert.All(bodies, body => Assert.DoesNotContain(secret, body, StringComparison.Ordinal));
        }
    }

    private static async Task<bool> ChangeOwnPinAsync(TenancyApp app, Guid shop, Guid staffId, string current, string next)
    {
        await using var db = app.Db(shop);
        var credentials = new HelloPos.Api.Features.Staff.StaffCredentials(
            new Microsoft.AspNetCore.Identity.PasswordHasher<HelloPos.Api.Features.Staff.StaffMember>(), app.Clock);
        var me = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(db.StaffMembers, s => s.Id == staffId, Ct);
        var refused = credentials.ChangeOwnPin(me, current, next);
        await db.SaveChangesAsync(Ct);
        return refused is null;
    }
}
