using Bunit;
using HelloPos.Kernel.Staff;
using HelloPos.Web.Pages.Invitations;
using HelloPos.Web.Services;
using HelloPos.Web.Tests.Fakes;
using HelloPos.Web.Tests.Pages.Owners;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace HelloPos.Web.Tests.Pages.Invitations;

/// <summary>Feature 006 US1 web: the invitation link page.</summary>
public sealed class AcceptInvitationTests : BunitContext
{
    private readonly FakeStaffService staff = new();

    public AcceptInvitationTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton<IStaffService>(staff);
        Services.AddSingleton<IOwnerAccountService>(new FakeOwnerAccountService());
        Services.AddSingleton<IBusinessService>(new FakeBusinessService());
        Services.AddSingleton<OwnerAuthenticationStateProvider>();
        Services.AddSingleton<OpenBusinessState>();
    }

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    [Fact]
    public void SignedOut_ShowsTheOffer_AndSignInComesBackHere()
    {
        AddAuthorization();
        Navigation.NavigateTo("accept-invitation?token=abc");

        var page = Render<AcceptInvitation>();

        Assert.Contains("Patel Grocers", page.Find("[data-testid=offer]").TextContent, StringComparison.Ordinal);
        Assert.Contains("manager", page.Find("[data-testid=offer]").TextContent, StringComparison.Ordinal);
        Assert.Equal("sign-in?returnUrl=accept-invitation%3Ftoken%3Dabc", page.Find("[data-testid=sign-in]").GetAttribute("href"));
        Assert.Empty(page.FindAll("[data-testid=accept]"));
    }

    [Fact]
    public void SignedIn_Accepts_AndGoesToBusinesses()
    {
        AddAuthorization().SetAuthorized("Mina");
        Navigation.NavigateTo("accept-invitation?token=abc");
        var page = Render<AcceptInvitation>();

        page.Find("[data-testid=accept]").Click();

        Assert.Equal(["abc"], staff.Accepted);
        Assert.EndsWith("businesses", Navigation.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void WrongAccount_SaysSo()
    {
        AddAuthorization().SetAuthorized("Stranger");
        staff.NextAccept = new OwnerActionResult.Failed("Not Found");
        Navigation.NavigateTo("accept-invitation?token=abc");
        var page = Render<AcceptInvitation>();

        page.Find("[data-testid=accept]").Click();

        Assert.Contains("different email address", page.Find("[data-testid=invitation-error]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidLink_SaysSo()
    {
        AddAuthorization();
        staff.NextPreview = new LoadResult<InvitationOffer>.Failed(StaffService.InvalidInvitation);
        Navigation.NavigateTo("accept-invitation?token=used");

        var page = Render<AcceptInvitation>();

        Assert.Contains("invalid, already used or expired", page.Find("[data-testid=invitation-error]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void NoToken_SaysSo_WithoutCallingTheApi()
    {
        AddAuthorization();
        Navigation.NavigateTo("accept-invitation");
        staff.NextPreview = new LoadResult<InvitationOffer>.Loaded(new InvitationOffer("X", StaffRole.Owner, "Y"));

        var page = Render<AcceptInvitation>();

        Assert.NotNull(page.Find("[data-testid=invitation-error]"));
    }
}
