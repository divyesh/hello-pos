using System.Net;
using HelloPos.Api.Infrastructure.Identity.Email;
using HelloPos.Api.Tests.Infrastructure.Database;
using HelloPos.Api.Tests.Tenancy;
using static HelloPos.Api.Tests.Features.Owners.OwnerApi;

namespace HelloPos.Api.Tests.Features.Owners;

/// <summary>US1 sign-up: FR-001, FR-003, FR-005, FR-007; research R2, R3.</summary>
[Collection(PostgresTests.Name)]
public sealed class SignUpTests(PostgresFixture postgres)
{
    [Fact]
    public async Task SignUp_Accepts_AndSendsAVerificationLink()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        using var client = app.OwnerClient();

        await AssertContractAsync(await SignUpAsync(client, "asha@example.com"), HttpStatusCode.Accepted, "ownerSignUp");

        var email = app.Emails.Last("asha@example.com", OwnerEmails.VerificationKind);
        Assert.Contains("/verify-email?owner=", email.TextBody, StringComparison.Ordinal);
        Assert.Contains("Asha Patel", email.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignUp_WithARegisteredEmail_LooksTheSame_AndNotifiesTheOwner()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        using var client = app.OwnerClient();

        using var first = await SignUpAsync(client, "asha@example.com");
        using var second = await SignUpAsync(client, "ASHA@example.com", "another long passphrase");

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(Ct), await second.Content.ReadAsStringAsync(Ct));
        Assert.Equal(
            [OwnerEmails.VerificationKind, OwnerEmails.AlreadyRegisteredKind],
            app.Emails.To("asha@example.com").Select(m => m.Kind));
    }

    [Theory]
    [InlineData("password", "elevenchars")]
    [InlineData("password", "123qweasdzxc")]
    [InlineData("password", "PASSWORDPASSWORD")]
    [InlineData("email", "not-an-email")]
    [InlineData("displayName", "")]
    public async Task SignUp_RejectsInvalidFields_ByName(string field, string value)
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        using var client = app.OwnerClient();

        using var response = field switch
        {
            "password" => await SignUpAsync(client, "asha@example.com", value),
            "email" => await SignUpAsync(client, value),
            _ => await SignUpAsync(client, "asha@example.com", displayName: value),
        };

        var body = await AssertContractAsync(response, HttpStatusCode.BadRequest, "ownerSignUp");
        Assert.Contains(field, ErrorFields(body));
        Assert.Empty(app.Emails.Sent);
    }

    [Fact]
    public async Task MalformedJson_Is400_NotAServerError()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        using var client = app.OwnerClient();
        using var body = new StringContent("{\"email\":\"a\rb\"}", System.Text.Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(new Uri("/owner/sign-up", UriKind.Relative), body, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("a\rb", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignUp_WithoutTheClientHeader_IsRefused()
    {
        await using var app = await TenancyApp.CreateAsync(postgres);
        using var client = app.OwnerClient(withClientHeader: false);

        await AssertContractAsync(await SignUpAsync(client, "asha@example.com"), HttpStatusCode.Forbidden, "ownerSignUp");
        Assert.Empty(app.Emails.Sent);
    }
}
