using Bunit;
using HelloPos.Web.Pages.Owners;
using HelloPos.Web.Services;

namespace HelloPos.Web.Tests.Pages.Owners;

/// <summary>Pages opened from email links: verification and password reset; and forgot password.</summary>
public sealed class EmailLinkPageTests : OwnerPageTests
{
    private static readonly Guid OwnerId = Guid.Parse("0199d1c4-8000-7000-8000-000000000001");

    [Fact]
    public void VerifyEmail_PostsTheLink_AndConfirms()
    {
        Navigation.NavigateTo($"verify-email?owner={OwnerId}&token=abc-123");

        var page = Render<VerifyEmail>();

        Assert.Equal(FakeOwnerAccountService.Describe("verify-email", OwnerId.ToString(), "abc-123"), Accounts.Describe(0));
        Assert.NotNull(page.Find("[data-testid=verify-done]"));
    }

    [Fact]
    public void VerifyEmail_InvalidLink_OffersANewOne()
    {
        Navigation.NavigateTo($"verify-email?owner={OwnerId}&token=used");
        Accounts.Next("verify-email", new OwnerActionResult.Failed("This link is invalid, already used or expired. Ask for a new one."));
        var page = Render<VerifyEmail>();

        Type(page, "email", "asha@example.com");
        Click(page, "resend");

        Assert.Contains("invalid", page.Find("[data-testid=verify-failed]").TextContent, StringComparison.Ordinal);
        Assert.Equal("resend-verification", Accounts.Calls[^1].Operation);
        Assert.NotNull(page.Find("[data-testid=verification-resent]"));
    }

    [Fact]
    public void VerifyEmail_WithoutALink_DoesNotCallTheApi()
    {
        Navigation.NavigateTo("verify-email");

        var page = Render<VerifyEmail>();

        Assert.NotNull(page.Find("[data-testid=verify-failed]"));
        Assert.Empty(Accounts.Calls);
    }

    [Fact]
    public void ForgotPassword_AlwaysShowsTheSameConfirmation()
    {
        var page = Render<ForgotPassword>();
        Type(page, "email", "asha@example.com");

        Click(page, "submit");

        Assert.Contains("asha@example.com", page.Find("[data-testid=forgot-done]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void ForgotPassword_InvalidEmail_IsShownOnTheField()
    {
        Accounts.Next("forgot-password", Fields("email", "Enter a valid email address."));
        var page = Render<ForgotPassword>();

        Click(page, "submit");

        Assert.Contains("Enter a valid email address.", page.Markup, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("[data-testid=forgot-done]"));
    }

    [Fact]
    public void ResetPassword_Success_SaysSessionsEnded()
    {
        Navigation.NavigateTo($"reset-password?owner={OwnerId}&token=t1");
        var page = Render<ResetPassword>();
        Type(page, "password", "a brand new long passphrase");

        Click(page, "submit");

        Assert.Equal(FakeOwnerAccountService.Describe("reset-password", OwnerId.ToString(), "t1", "a brand new long passphrase"), Accounts.Describe(0));
        Assert.NotNull(page.Find("[data-testid=reset-done]"));
    }

    [Fact]
    public void ResetPassword_PasswordRules_AreShownOnTheField()
    {
        Navigation.NavigateTo($"reset-password?owner={OwnerId}&token=t1");
        Accounts.Next("reset-password", Fields("newPassword", "Use at least 12 characters."));
        var page = Render<ResetPassword>();

        Click(page, "submit");

        Assert.Contains("Use at least 12 characters.", page.Markup, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("[data-testid=reset-done]"));
    }

    [Fact]
    public void ResetPassword_InvalidLink_OffersANewOne()
    {
        Navigation.NavigateTo($"reset-password?owner={OwnerId}&token=expired");
        Accounts.Next("reset-password", new OwnerActionResult.Failed("This link is invalid, already used or expired. Ask for a new one."));
        var page = Render<ResetPassword>();

        Click(page, "submit");

        Assert.NotNull(page.Find("[data-testid=reset-failed] a[href='forgot-password']"));
    }
}
