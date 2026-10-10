using Bunit;
using HelloPos.Web.Pages;
using HelloPos.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace HelloPos.Web.Tests.Pages;

/// <summary>Start screen states from data-model.md: Checking, Available, Unavailable (FR-008).</summary>
public sealed class StartPageTests : BunitContext
{
    private readonly FakeHealthService health = new();

    public StartPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton<IHealthService>(health);
    }

    [Fact]
    public void WhileChecking_ShowsProgress()
    {
        health.Next = new TaskCompletionSource<HealthCheckResult>();

        var page = Render<Start>();

        Assert.NotNull(page.Find("[data-testid=health-checking]"));
    }

    [Fact]
    public void WhenAvailable_ShowsStatusAndVersion()
    {
        health.Next = Completed(new HealthCheckResult.Available("available", "0.1.0-dev"));

        var page = Render<Start>();

        var panel = page.Find("[data-testid=health-available]");
        Assert.Contains("available", panel.TextContent, StringComparison.Ordinal);
        Assert.Contains("0.1.0-dev", panel.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void WhenUnavailable_ShowsMessageAndRetryChecksAgain()
    {
        health.Next = Completed(new HealthCheckResult.Unavailable("timeout"));
        var page = Render<Start>();

        var panel = page.Find("[data-testid=health-unavailable]");
        Assert.Contains("API unavailable", panel.TextContent, StringComparison.Ordinal);

        health.Next = Completed(new HealthCheckResult.Available("available", "0.1.0-dev"));
        page.Find("[data-testid=health-retry]").Click();

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid=health-available]")));
        Assert.Equal(2, health.Calls);
    }

    private static TaskCompletionSource<HealthCheckResult> Completed(HealthCheckResult result)
    {
        var source = new TaskCompletionSource<HealthCheckResult>();
        source.SetResult(result);
        return source;
    }

    private sealed class FakeHealthService : IHealthService
    {
        public TaskCompletionSource<HealthCheckResult> Next { get; set; } = new();

        public int Calls { get; private set; }

        public Task<HealthCheckResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Next.Task;
        }
    }
}
