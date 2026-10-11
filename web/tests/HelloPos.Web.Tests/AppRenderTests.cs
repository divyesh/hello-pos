using Bunit;
using HelloPos.Kernel.Businesses;
using HelloPos.Web.Layout;
using HelloPos.Web.Services;
using HelloPos.Web.Tests.Fakes;
using HelloPos.Web.Tests.Pages.Owners;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace HelloPos.Web.Tests;

public sealed class AppRenderTests : BunitContext
{
    private readonly FakeOwnerAccountService accounts = new();
    private readonly FakeBusinessService businesses = new();

    public AppRenderTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton<IOwnerAccountService>(accounts);
        Services.AddSingleton<IBusinessService>(businesses);
        Services.AddSingleton<OwnerAuthenticationStateProvider>();
        Services.AddSingleton<OpenBusinessState>();
    }

    [Fact]
    public void MainLayout_RendersWithBodyContent()
    {
        AddAuthorization();

        var layout = RenderLayout();

        Assert.Contains("Hello POS", layout.Markup, StringComparison.Ordinal);
        Assert.Contains("page content", layout.Markup, StringComparison.Ordinal);
        Assert.Empty(layout.FindAll("[data-testid=sign-out]"));
    }

    [Fact]
    public void MainLayout_SignedIn_ShowsTheOwner_AndSignsOut()
    {
        AddAuthorization().SetAuthorized("Asha Patel");
        var layout = RenderLayout();

        Assert.Contains("Asha Patel", layout.Find("[data-testid=signed-in-as]").TextContent, StringComparison.Ordinal);
        layout.Find("[data-testid=sign-out]").Click();

        Assert.Equal("sign-out", accounts.Calls.Single().Operation);
        Assert.EndsWith("sign-in", Services.GetRequiredService<NavigationManager>().Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void MainLayout_ShowsTheOpenBusiness_FromTheSession()
    {
        var shop = Guid.NewGuid();
        accounts.Session = new OwnerSession(Guid.NewGuid(), "Asha Patel", "asha@example.com", shop);
        businesses.Settings[shop] = new BusinessSettings("Patel Grocers", "IN", "INR", PricingMode.TaxInclusive, 4, "Asia/Kolkata", 1m);
        AddAuthorization().SetAuthorized("Asha Patel");

        var layout = RenderLayout();

        layout.WaitForAssertion(() =>
            Assert.Contains("Patel Grocers", layout.Find("[data-testid=open-business]").TextContent, StringComparison.Ordinal));
        Assert.NotNull(layout.Find("[data-testid=switch-business]"));
    }

    [Fact]
    public void MainLayout_WithNoOpenBusiness_ShowsNone()
    {
        accounts.Session = new OwnerSession(Guid.NewGuid(), "Asha Patel", "asha@example.com", null);
        AddAuthorization().SetAuthorized("Asha Patel");

        var layout = RenderLayout();

        Assert.Empty(layout.FindAll("[data-testid=open-business]"));
    }

    private IRenderedComponent<MainLayout> RenderLayout()
    {
        RenderFragment body = builder => builder.AddContent(0, "page content");
        return Render<MainLayout>(parameters => parameters.Add(p => p.Body, body));
    }
}
