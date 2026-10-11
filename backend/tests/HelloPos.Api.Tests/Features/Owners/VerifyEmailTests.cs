using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using HelloPos.Api.Infrastructure.Identity.Email;
using HelloPos.Api.Tests.Infrastructure;
using HelloPos.Api.Tests.Infrastructure.Database;
using HelloPos.Api.Tests.Tenancy;
using static HelloPos.Api.Tests.Features.Owners.OwnerApi;

namespace HelloPos.Api.Tests.Features.Owners;

/// <summary>US1 email verification: FR-004, SC-006; research R5.</summary>
[Collection(PostgresTests.Name)]
public sealed class VerifyEmailTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Link_VerifiesOnce()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        using var client = app.OwnerClient();
        var (owner, token) = await SignUpUnverifiedAsync(app, client, "asha@example.com");

        await AssertContractAsync(await VerifyAsync(client, owner, token), HttpStatusCode.NoContent, "ownerVerifyEmail");
        await AssertContractAsync(await VerifyAsync(client, owner, token), HttpStatusCode.BadRequest, "ownerVerifyEmail");
        Assert.Equal(HttpStatusCode.NoContent, (await SignInAsync(client, "asha@example.com")).StatusCode);
    }

    [Fact]
    public async Task Link_ExpiresAfter24Hours()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        using var client = app.OwnerClient();
        var (owner, token) = await SignUpUnverifiedAsync(app, client, "asha@example.com");

        app.Clock.Advance(TimeSpan.FromHours(24) + TimeSpan.FromMinutes(1));

        await AssertContractAsync(await VerifyAsync(client, owner, token), HttpStatusCode.BadRequest, "ownerVerifyEmail");
    }

    [Fact]
    public async Task TamperedLink_IsRejected()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        using var client = app.OwnerClient();
        var (owner, token) = await SignUpUnverifiedAsync(app, client, "asha@example.com");

        Assert.Equal(HttpStatusCode.BadRequest, (await VerifyAsync(client, owner, token[..^4] + "AAAA")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await VerifyAsync(client, Guid.CreateVersion7(), token)).StatusCode);
    }

    [Fact]
    public async Task Unverified_SignIn_IsVerificationRequired_OnlyWithTheRightPassword()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        using var client = app.OwnerClient();
        await SignUpUnverifiedAsync(app, client, "asha@example.com");

        var right = await AssertContractAsync(await SignInAsync(client, "asha@example.com"), HttpStatusCode.Forbidden, "ownerSignIn");
        Assert.Equal("urn:hellopos:problem:verification-required", (string?)JsonNode.Parse(right)?["type"]);
        await AssertContractAsync(await SignInAsync(client, "asha@example.com", "a wrong long password"), HttpStatusCode.Unauthorized, "ownerSignIn");
        Assert.Equal(HttpStatusCode.Unauthorized, (await MeAsync(client)).StatusCode);
    }

    [Fact]
    public async Task Resend_SendsANewLink_OnlyToUnverifiedAccounts()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        using var client = app.OwnerClient();
        await SignUpUnverifiedAsync(app, client, "asha@example.com");

        await AssertContractAsync(
            await client.PostAsJsonAsync("/owner/resend-verification", new { email = "asha@example.com" }, Ct),
            HttpStatusCode.Accepted, "ownerResendVerification");
        await AssertContractAsync(
            await client.PostAsJsonAsync("/owner/resend-verification", new { email = "nobody@example.com" }, Ct),
            HttpStatusCode.Accepted, "ownerResendVerification");

        Assert.Equal(2, app.Emails.To("asha@example.com").Count(m => m.Kind == OwnerEmails.VerificationKind));
        Assert.Empty(app.Emails.To("nobody@example.com"));
        var (owner, token) = InMemoryEmailSender.LinkOf(app.Emails.Last("asha@example.com", OwnerEmails.VerificationKind));
        Assert.Equal(HttpStatusCode.NoContent, (await VerifyAsync(client, owner, token)).StatusCode);

        await client.PostAsJsonAsync("/owner/resend-verification", new { email = "asha@example.com" }, Ct);
        Assert.Equal(2, app.Emails.To("asha@example.com").Count(m => m.Kind == OwnerEmails.VerificationKind));
    }
}
