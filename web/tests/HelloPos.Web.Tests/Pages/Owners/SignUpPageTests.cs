using Bunit;
using HelloPos.Web.Pages.Owners;
using HelloPos.Web.Services;

namespace HelloPos.Web.Tests.Pages.Owners;

public sealed class SignUpPageTests : OwnerPageTests
{
    [Fact]
    public void Success_SendsTheForm_AndAsksToCheckEmail()
    {
        var page = Render<SignUp>();
        Type(page, "display-name", "Asha Patel");
        Type(page, "email", "asha@example.com");
        Type(page, "password", "a long passphrase nobody uses");

        Click(page, "submit");

        Assert.Equal(FakeOwnerAccountService.Describe("sign-up", "Asha Patel", "asha@example.com", "a long passphrase nobody uses"), Accounts.Describe(0));
        Assert.Contains("asha@example.com", page.Find("[data-testid=sign-up-done]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void FieldErrors_AreShownNextToTheirFields()
    {
        Accounts.Next("sign-up", Fields("password", "This password is too common."));
        var page = Render<SignUp>();

        Click(page, "submit");

        Assert.Contains("This password is too common.", page.Markup, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("[data-testid=sign-up-done]"));
    }

    [Fact]
    public void OtherFailures_AreShownAsAFormError()
    {
        Accounts.Next("sign-up", new OwnerActionResult.Failed("Too many attempts. Wait a minute and try again."));
        var page = Render<SignUp>();

        Click(page, "submit");

        Assert.Contains("Too many attempts", page.Find("[data-testid=form-error]").TextContent, StringComparison.Ordinal);
    }
}
