using Bunit;
using HelloPos.Kernel.Staff;
using HelloPos.Web.Pages.Devices;
using HelloPos.Web.Services;
using HelloPos.Web.Tests.Fakes;
using HelloPos.Web.Tests.Pages.Owners;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MudBlazor.Services;

namespace HelloPos.Web.Tests.Pages.Devices;

/// <summary>Feature 007 US1 web: registering a device with a 5-minute countdown, renaming, revoking.</summary>
public sealed class DevicesPageTests : BunitContext
{
    private static readonly Guid Shop = Guid.Parse("0199d1c4-8000-7000-8000-0000000000cc");

    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 10, 11, 9, 0, 0, TimeSpan.Zero));
    private readonly FakeDeviceService devices = new();
    private readonly FakeBusinessService businesses = new();

    public DevicesPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton<TimeProvider>(clock);
        Services.AddSingleton<IDeviceService>(devices);
        Services.AddSingleton<IBusinessService>(businesses);
        Services.AddSingleton<IOwnerAccountService>(new FakeOwnerAccountService());
        Services.AddSingleton<OpenBusinessState>();
        devices.Now = clock.GetUtcNow;
        devices.Devices.Add(new DeviceItem(Guid.NewGuid(), "Front till", "Asha", clock.GetUtcNow(), null, true, 1));
    }

    [Fact]
    public async Task Cashiers_AreNotAllowed()
    {
        await OpenAsync(StaffRole.Cashier);

        var page = Render<DevicesPage>();

        Assert.NotNull(page.Find("[data-testid=not-allowed]"));
        Assert.Empty(page.FindAll("[data-testid=register-device]"));
    }

    [Fact]
    public async Task Registering_ShowsTheCode_AndCountsDownFiveMinutes_ThenOffersANewCode()
    {
        await OpenAsync(StaffRole.Manager);
        var page = Render<DevicesPage>();

        page.Find("[data-testid=register-device]").Click();

        Assert.Equal("ABCD-EFGH", page.Find("[data-testid=pairing-code]").TextContent);
        Assert.Equal("5:00", page.Find("[data-testid=countdown]").TextContent);

        clock.Advance(TimeSpan.FromSeconds(61));
        page.WaitForAssertion(() => Assert.Equal("3:59", page.Find("[data-testid=countdown]").TextContent));

        clock.Advance(TimeSpan.FromMinutes(4));
        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid=pairing-expired]")));
        Assert.Empty(page.FindAll("[data-testid=pairing-code]"));

        page.Find("[data-testid=new-code]").Click();
        Assert.Equal("JKMN-PQRS", page.Find("[data-testid=pairing-code]").TextContent);
        Assert.Equal("5:00", page.Find("[data-testid=countdown]").TextContent);
    }

    [Fact]
    public void Format_RoundsUp_SoTheLastSecondIsNotZero()
    {
        Assert.Equal("0:01", PairingCountdown.Format(TimeSpan.FromMilliseconds(200)));
        Assert.Equal("0:00", PairingCountdown.Format(TimeSpan.FromSeconds(-3)));
        Assert.Equal("4:30", PairingCountdown.Format(TimeSpan.FromSeconds(270)));
    }

    [Fact]
    public async Task Rename_SendsTheVersion_AndRefreshes()
    {
        await OpenAsync(StaffRole.Owner);
        var page = Render<DevicesPage>();

        page.Find("[data-testid=rename]").Click();
        var field = page.Find("[data-testid=rename-name]");
        (field.TagName.Equals("INPUT", StringComparison.OrdinalIgnoreCase) ? field : field.QuerySelector("input")!).Change("Back till");
        page.Find("[data-testid=rename-save]").Click();

        Assert.Equal("rename Back till v1", Assert.Single(devices.Changes));
        Assert.Contains("Back till", page.Find("[data-testid=device]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Revoke_NeedsASecondClick()
    {
        await OpenAsync(StaffRole.Owner);
        var page = Render<DevicesPage>();

        page.Find("[data-testid=revoke]").Click();
        Assert.Empty(devices.Changes);
        page.Find("[data-testid=confirm-revoke]").Click();

        Assert.Equal("revoke v1", Assert.Single(devices.Changes));
        Assert.Contains("Revoked", page.Find("[data-testid=device]").TextContent, StringComparison.Ordinal);
        Assert.Contains("signed out", page.Find("[data-testid=device-message]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AConflict_IsShown()
    {
        await OpenAsync(StaffRole.Owner);
        devices.NextChange = new OwnerActionResult.Failed("Someone else changed this device. Reload and try again.");
        var page = Render<DevicesPage>();

        page.Find("[data-testid=revoke]").Click();
        page.Find("[data-testid=confirm-revoke]").Click();

        Assert.Contains("Someone else changed", page.Find("[data-testid=device-message]").TextContent, StringComparison.Ordinal);
    }

    private async Task OpenAsync(StaffRole role) =>
        await Services.GetRequiredService<OpenBusinessState>().OpenAsync(Shop, "Patel Grocers", role);
}
