using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using HelloPos.Api.Tests.Features.Staff;
using HelloPos.Api.Tests.Infrastructure.Database;
using HelloPos.Api.Tests.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Sinks.InMemory;
using static HelloPos.Api.Tests.Features.Businesses.OpenAndEditTests;
using static HelloPos.Api.Tests.Features.Pos.PosApi;

namespace HelloPos.Api.Tests.Features.Pos;

/// <summary>
/// Feature 007 SC-006: pair, sign in (right and wrong), refresh, change PIN, offline access and sign out
/// with every log event captured. No PIN, pairing code, device credential or token appears in any log,
/// and each secret appears only in the one response that issued it.
/// </summary>
[Collection(PostgresTests.Name)]
public sealed class PosSecretsNeverLeakTests(PostgresFixture postgres)
{
    private const string Pin = "482613"; // 6 digits: a 4-digit PIN could match random hex in ids by chance
    private const string WrongPin = "907153";
    private const string NewPin = "593718";

    [Fact]
    public async Task PosFlow_NeverExposesPinsCodesCredentialsOrTokens()
    {
        var sink = new InMemorySink();
        await using var app = await TenancyApp.CreateAsync(postgres,
            configureServices: (services, _) => services.AddSingleton<ILogEventSink>(sink));
        var bodies = new List<(string Step, string Body)>();
        async Task<JsonNode?> Keep(string step, Task<HttpResponseMessage> call)
        {
            using var response = await call;
            var body = await response.Content.ReadAsStringAsync(Ct);
            bodies.Add((step, body));
            return body.Length > 0 ? JsonNode.Parse(body) : null;
        }

        using var owner = await app.SignUpVerifiedOwnerAsync("owner@example.com");
        var shop = await CreateIdAsync(owner, "Shop");
        await OpenAsync(owner, shop);
        Assert.Equal(HttpStatusCode.Created, (await StaffApi.AddAsync(owner, shop, StaffApi.NewStaff("Ravi", "cashier", "ravi", Pin))).StatusCode);

        var code = (string)(await Keep("pairing", CreatePairingAsync(owner, shop)))!["code"]!;
        using var key = RSA.Create(3072);
        using var anonymous = app.OwnerClient(withClientHeader: false);
        await Keep("wrong-code", PairAsync(anonymous, "ZZZZ-ZZZZ", "Till", PublicKeyOf(key)));
        var credential = (string)(await Keep("pair", PairAsync(anonymous, code, "Till", PublicKeyOf(key))))!["deviceCredential"]!;
        using var device = DeviceClient(app, credential);

        await Keep("wrong-pin", PosSignInAsync(device, "ravi", WrongPin));
        var signIn = (await Keep("sign-in", PosSignInAsync(device, "ravi", Pin)))!["tokens"]!;
        var refreshed = (await Keep("refresh", PosRefreshAsync(device, (string)signIn["refreshToken"]!)))!;
        using var session = SessionClient(app, (string)refreshed["accessToken"]!);
        await Keep("wrong-current", PosChangePinAsync(session, WrongPin, NewPin));
        var changed = (await Keep("change-pin", PosChangePinAsync(session, Pin, NewPin)))!;
        await Keep("offline-access", OfflineAccessAsync(device));
        await Keep("reused-refresh", PosRefreshAsync(device, (string)signIn["refreshToken"]!));
        using var renewed = SessionClient(app, (string)changed["accessToken"]!);
        await Keep("me", PosMeAsync(renewed));
        await Keep("sign-out", PosSignOutAsync(renewed));

        var logged = string.Join('\n', sink.LogEvents.Select(e =>
            e.RenderMessage(System.Globalization.CultureInfo.InvariantCulture) + " "
            + string.Join(' ', e.Properties.Select(p => p.Value.ToString())) + " " + e.Exception));
        Assert.NotEmpty(sink.LogEvents);

        var issued = new (string Secret, string IssuedBy)[]
        {
            (code, "pairing"),
            (code.Replace("-", "", StringComparison.Ordinal), "pairing"),
            (credential, "pair"),
            (SecretPart(credential), "pair"),
            ((string)signIn["accessToken"]!, "sign-in"),
            ((string)signIn["refreshToken"]!, "sign-in"),
            ((string)refreshed["accessToken"]!, "refresh"),
            ((string)refreshed["refreshToken"]!, "refresh"),
            ((string)changed["accessToken"]!, "change-pin"),
            ((string)changed["refreshToken"]!, "change-pin"),
        };
        foreach (var (secret, issuedBy) in issued)
        {
            Assert.DoesNotContain(secret, logged, StringComparison.Ordinal);
            Assert.All(bodies.Where(b => b.Step != issuedBy), b => Assert.DoesNotContain(secret, b.Body, StringComparison.Ordinal));
        }

        foreach (var pin in new[] { Pin, WrongPin, NewPin })
        {
            Assert.DoesNotContain(pin, logged, StringComparison.Ordinal);
            Assert.All(bodies, b => Assert.DoesNotContain(pin, b.Body, StringComparison.Ordinal));
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The random part of an opaque secret (after the last dot): never logged even without its prefix.</summary>
    private static string SecretPart(string secret) => secret[(secret.LastIndexOf('.') + 1)..];
}
