using HelloPos.Web.Services;

namespace HelloPos.Web.Tests.Fakes;

/// <summary>In-memory POS devices for page tests; codes expire 5 minutes after <see cref="Now"/>.</summary>
public sealed class FakeDeviceService : IDeviceService
{
    public List<DeviceItem> Devices { get; } = [];

    public List<string> Changes { get; } = [];

    public Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.UtcNow;

    public int CodesIssued { get; private set; }

    public OwnerActionResult NextChange { get; set; } = OwnerActionResult.Success;

    public Task<LoadResult<IReadOnlyList<DeviceItem>>> ListAsync(Guid pageBusiness, CancellationToken cancellationToken = default) =>
        Task.FromResult<LoadResult<IReadOnlyList<DeviceItem>>>(new LoadResult<IReadOnlyList<DeviceItem>>.Loaded(Devices.ToList()));

    public Task<LoadResult<PairingCode>> CreatePairingAsync(Guid pageBusiness, CancellationToken cancellationToken = default)
    {
        CodesIssued++;
        var code = CodesIssued == 1 ? "ABCD-EFGH" : "JKMN-PQRS";
        return Task.FromResult<LoadResult<PairingCode>>(new LoadResult<PairingCode>.Loaded(new PairingCode(code, Now() + TimeSpan.FromMinutes(5))));
    }

    public Task<OwnerActionResult> RenameAsync(Guid pageBusiness, DeviceItem device, string name, CancellationToken cancellationToken = default) =>
        Change(device, $"rename {name} v{device.Version}", d => d with { Name = name });

    public Task<OwnerActionResult> RevokeAsync(Guid pageBusiness, DeviceItem device, CancellationToken cancellationToken = default) =>
        Change(device, $"revoke v{device.Version}", d => d with { Active = false });

    private Task<OwnerActionResult> Change(DeviceItem device, string what, Func<DeviceItem, DeviceItem> apply)
    {
        if (NextChange is OwnerActionResult.Succeeded)
        {
            Changes.Add(what);
            var index = Devices.FindIndex(d => d.Id == device.Id);
            Devices[index] = apply(Devices[index]) with { Version = device.Version + 1 };
        }

        return Task.FromResult(NextChange);
    }
}
