using Bunit;
using HelloPos.Web.Layout;
using Microsoft.AspNetCore.Components;
using MudBlazor.Services;

namespace HelloPos.Web.Tests;

public sealed class AppRenderTests : BunitContext
{
    public AppRenderTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
    }

    [Fact]
    public void MainLayout_RendersWithBodyContent()
    {
        RenderFragment body = builder => builder.AddContent(0, "page content");

        var layout = Render<MainLayout>(parameters => parameters.Add(p => p.Body, body));

        Assert.Contains("Hello POS", layout.Markup, StringComparison.Ordinal);
        Assert.Contains("page content", layout.Markup, StringComparison.Ordinal);
    }
}
