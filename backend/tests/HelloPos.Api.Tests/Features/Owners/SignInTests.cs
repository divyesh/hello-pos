using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using HelloPos.Api.Infrastructure.Identity;
using HelloPos.Api.Tests.Infrastructure.Database;
using HelloPos.Api.Tests.Tenancy;
using static HelloPos.Api.Tests.Features.Owners.OwnerApi;

namespace HelloPos.Api.Tests.Features.Owners;

/// <summary>US1 sign-in and sessions: FR-002, FR-005, FR-006, FR-008, SC-003, SC-004; research R2, R3.</summary>
[Collection(PostgresTests.Name)]
public sealed class SignInTests(PostgresFixture postgres)
{
    [Fact]
    public async Task SignIn_SetsAnHttpOnlyStrictCookie_AndMeDescribesTheOwner()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        using var client = await app.SignUpVerifiedOwnerAsync("asha@example.com");
        using var other = app.OwnerClient();

        using var response = await SignInAsync(other, "Asha@Example.com");

        await AssertContractAsync(response, HttpStatusCode.NoContent, "ownerSignIn");
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith(IdentitySetup.CookieName + "=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);

        var me = JsonNode.Parse(await AssertContractAsync(await MeAsync(other), HttpStatusCode.OK, "ownerMe"))!;
        Assert.Equal("asha@example.com", (string?)me["email"]);
        Assert.Equal("Owner", (string?)me["displayName"]);
        Assert.True((bool?)me["emailVerified"]);
        Assert.Null(me["currentBusinessId"]);
    }

    [Fact]
    public async Task EveryFailure_GetsTheSameAnswer()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        (await app.SignUpVerifiedOwnerAsync("asha@example.com")).Dispose();
        (await app.SignUpVerifiedOwnerAsync("locked@example.com")).Dispose();
        using var client = app.OwnerClient();
        for (var i = 0; i < 5; i++)
        {
            await SignInAsync(client, "locked@example.com", "a wrong long password");
        }

        var wrongPassword = await AssertContractAsync(await SignInAsync(client, "asha@example.com", "a wrong long password"), HttpStatusCode.Unauthorized, "ownerSignIn");
        var unknownEmail = await AssertContractAsync(await SignInAsync(client, "nobody@example.com"), HttpStatusCode.Unauthorized, "ownerSignIn");
        var locked = await AssertContractAsync(await SignInAsync(client, "locked@example.com"), HttpStatusCode.Unauthorized, "ownerSignIn");

        Assert.Equal(WithoutTraceId(wrongPassword), WithoutTraceId(unknownEmail));
        Assert.Equal(WithoutTraceId(wrongPassword), WithoutTraceId(locked));
    }

    [Fact]
    public async Task FiveFailures_LockTheAccount_EvenForTheRightPassword()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        (await app.SignUpVerifiedOwnerAsync("asha@example.com")).Dispose();
        using var client = app.OwnerClient();

        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await SignInAsync(client, "asha@example.com", "a wrong long password")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.NoContent, (await SignInAsync(client, "asha@example.com")).StatusCode); // success resets the count
        for (var i = 0; i < 5; i++)
        {
            await SignInAsync(client, "asha@example.com", "a wrong long password");
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await SignInAsync(client, "asha@example.com")).StatusCode);
    }

    [Fact]
    public async Task TooManyRequestsFromOneAddress_AreRateLimited()
    {
        await using var app = await TenancyApp.CreateAsync(postgres,
            settings: new Dictionary<string, string?> { ["RateLimits:OwnerAuthPerMinute"] = "10" });
        using var client = app.OwnerClient();

        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await SignInAsync(client, $"nobody{i}@example.com")).StatusCode);
        }

        await AssertContractAsync(await SignInAsync(client, "nobody@example.com"), HttpStatusCode.TooManyRequests, "ownerSignIn");
        await AssertContractAsync(await SignUpAsync(client, "new@example.com"), HttpStatusCode.TooManyRequests, "ownerSignUp");
    }

    [Fact]
    public async Task SignOut_EndsTheSession()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        using var client = await app.SignUpVerifiedOwnerAsync("asha@example.com");
        Assert.Equal(HttpStatusCode.OK, (await MeAsync(client)).StatusCode);

        await AssertContractAsync(await client.PostAsync(new Uri("/owner/sign-out", UriKind.Relative), null, Ct), HttpStatusCode.NoContent, "ownerSignOut");

        Assert.Equal(HttpStatusCode.Unauthorized, (await MeAsync(client)).StatusCode);
    }

    [Fact]
    public async Task Session_EndsAfterInactivity_AndAfterSevenDaysRegardless()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        using var idle = await app.SignUpVerifiedOwnerAsync("idle@example.com");
        using var busy = await app.SignUpVerifiedOwnerAsync("busy@example.com");

        app.Clock.Advance(TimeSpan.FromHours(7));
        Assert.Equal(HttpStatusCode.OK, (await MeAsync(busy)).StatusCode);
        app.Clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(HttpStatusCode.Unauthorized, (await MeAsync(idle)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await MeAsync(busy)).StatusCode);

        // Active every 3 hours (never close to 8 idle) until 165 hours after signing in: still signed in.
        for (var i = 0; i < 52; i++)
        {
            app.Clock.Advance(TimeSpan.FromHours(3));
            Assert.Equal(HttpStatusCode.OK, (await MeAsync(busy)).StatusCode);
        }

        app.Clock.Advance(TimeSpan.FromHours(4)); // 169 hours: past 7 days, though active recently
        Assert.Equal(HttpStatusCode.Unauthorized, (await MeAsync(busy)).StatusCode);
    }

    [Fact]
    public async Task Me_WithoutASession_Is401()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        using var client = app.OwnerClient();

        await AssertContractAsync(await MeAsync(client), HttpStatusCode.Unauthorized, "ownerMe");
    }
}
