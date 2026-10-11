using System.Net;
using System.Net.Http.Json;
using HelloPos.Api.Infrastructure.Identity.Email;
using HelloPos.Api.Tests.Infrastructure;
using HelloPos.Api.Tests.Infrastructure.Database;
using HelloPos.Api.Tests.Tenancy;
using static HelloPos.Api.Tests.Features.Owners.OwnerApi;

namespace HelloPos.Api.Tests.Features.Owners;

/// <summary>US1 password reset: FR-004a, FR-005; research R5.</summary>
[Collection(PostgresTests.Name)]
public sealed class PasswordResetTests(PostgresFixture postgres)
{
    private const string NewPassword = "a brand new long passphrase";

    [Fact]
    public async Task ForgotPassword_AlwaysAccepts_AndMailsOnlyRegisteredOwners()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        (await app.SignUpVerifiedOwnerAsync("asha@example.com")).Dispose();
        using var client = app.OwnerClient();

        using var known = await ForgotAsync(client, "asha@example.com");
        using var unknown = await ForgotAsync(client, "nobody@example.com");

        await AssertContractAsync(known, HttpStatusCode.Accepted, "ownerForgotPassword");
        await AssertContractAsync(unknown, HttpStatusCode.Accepted, "ownerForgotPassword");
        Assert.Equal(await known.Content.ReadAsStringAsync(Ct), await unknown.Content.ReadAsStringAsync(Ct));
        Assert.Single(app.Emails.To("asha@example.com"), m => m.Kind == OwnerEmails.PasswordResetKind);
        Assert.Empty(app.Emails.To("nobody@example.com"));
    }

    [Fact]
    public async Task ResetLink_WorksOnce_AndReplacesThePassword()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        (await app.SignUpVerifiedOwnerAsync("asha@example.com")).Dispose();
        using var client = app.OwnerClient();
        await ForgotAsync(client, "asha@example.com");
        var (owner, token) = InMemoryEmailSender.LinkOf(app.Emails.Last("asha@example.com", OwnerEmails.PasswordResetKind));

        await AssertContractAsync(await ResetAsync(client, owner, token, NewPassword), HttpStatusCode.NoContent, "ownerResetPassword");
        await AssertContractAsync(await ResetAsync(client, owner, token, "yet another long passphrase"), HttpStatusCode.BadRequest, "ownerResetPassword");

        Assert.Equal(HttpStatusCode.Unauthorized, (await SignInAsync(client, "asha@example.com")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SignInAsync(client, "asha@example.com", NewPassword)).StatusCode);
    }

    [Fact]
    public async Task ResetLink_ExpiresAfterOneHour()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        (await app.SignUpVerifiedOwnerAsync("asha@example.com")).Dispose();
        using var client = app.OwnerClient();
        await ForgotAsync(client, "asha@example.com");
        var (owner, token) = InMemoryEmailSender.LinkOf(app.Emails.Last("asha@example.com", OwnerEmails.PasswordResetKind));

        app.Clock.Advance(TimeSpan.FromMinutes(61));

        Assert.Equal(HttpStatusCode.BadRequest, (await ResetAsync(client, owner, token, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task Reset_EndsEveryExistingSession()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        using var laptop = await app.SignUpVerifiedOwnerAsync("asha@example.com");
        using var phone = app.OwnerClient();
        Assert.Equal(HttpStatusCode.NoContent, (await SignInAsync(phone, "asha@example.com")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await MeAsync(laptop)).StatusCode);

        using var anonymous = app.OwnerClient();
        await ForgotAsync(anonymous, "asha@example.com");
        var (owner, token) = InMemoryEmailSender.LinkOf(app.Emails.Last("asha@example.com", OwnerEmails.PasswordResetKind));
        Assert.Equal(HttpStatusCode.NoContent, (await ResetAsync(anonymous, owner, token, NewPassword)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await MeAsync(laptop)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await MeAsync(phone)).StatusCode);
    }

    [Fact]
    public async Task Reset_AppliesThePasswordRules()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        (await app.SignUpVerifiedOwnerAsync("asha@example.com")).Dispose();
        using var client = app.OwnerClient();
        await ForgotAsync(client, "asha@example.com");
        var (owner, token) = InMemoryEmailSender.LinkOf(app.Emails.Last("asha@example.com", OwnerEmails.PasswordResetKind));

        var body = await AssertContractAsync(await ResetAsync(client, owner, token, "too short"), HttpStatusCode.BadRequest, "ownerResetPassword");

        Assert.Contains("newPassword", ErrorFields(body));
        Assert.Equal(HttpStatusCode.NoContent, (await ResetAsync(client, owner, token, NewPassword)).StatusCode);
    }

    private static Task<HttpResponseMessage> ForgotAsync(HttpClient client, string email) =>
        client.PostAsJsonAsync("/owner/forgot-password", new { email }, Ct);

    private static Task<HttpResponseMessage> ResetAsync(HttpClient client, Guid ownerId, string token, string newPassword) =>
        client.PostAsJsonAsync("/owner/reset-password", new { ownerId, token, newPassword }, Ct);
}
