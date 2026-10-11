using Bunit;
using HelloPos.Web.Pages.Owners;
using HelloPos.Web.Services;

namespace HelloPos.Web.Tests.Pages.Owners;

public sealed class SignInPageTests : OwnerPageTests
{
    [Fact]
    public void Success_GoesToTheBusinessesPage()
    {
        Navigation.NavigateTo("sign-in");
        var page = Render<SignIn>();
        Type(page, "email", "asha@example.com");
        Type(page, "password", "a long passphrase nobody uses");

        Click(page, "submit");

        Assert.Equal(FakeOwnerAccountService.Describe("sign-in", "asha@example.com", "a long passphrase nobody uses"), Accounts.Describe(0));
        Assert.Equal(Navigation.BaseUri + "businesses", Navigation.Uri);
    }

    [Fact]
    public void WrongCredentials_ShowTheServerMessage_AndStay()
    {
        Navigation.NavigateTo("sign-in");
        Accounts.Next("sign-in", new OwnerActionResult.Failed("Email or password is incorrect."));
        var page = Render<SignIn>();

        Click(page, "submit");

        Assert.Contains("Email or password is incorrect.", page.Find("[data-testid=form-error]").TextContent, StringComparison.Ordinal);
        Assert.EndsWith("sign-in", Navigation.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void Unverified_OffersANewLink()
    {
        Accounts.Next("sign-in", new OwnerActionResult.VerificationRequired());
        var page = Render<SignIn>();
        Type(page, "email", "asha@example.com");
        Click(page, "submit");

        Click(page, "resend");

        Assert.Equal(FakeOwnerAccountService.Describe("resend-verification", "asha@example.com"), Accounts.Describe(1));
        Assert.NotNull(page.Find("[data-testid=verification-resent]"));
    }
}
