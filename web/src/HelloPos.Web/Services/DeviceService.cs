using HelloPos.Web.ApiClient.Models;

namespace HelloPos.Web.Services;

/// <summary>A POS device in the list.</summary>
public sealed record DeviceItem(
    Guid Id, string Name, string RegisteredBy, DateTimeOffset RegisteredAt, DateTimeOffset? LastUsedAt, bool Active, int Version);

/// <summary>A one-time code shown while a device is being connected.</summary>
public sealed record PairingCode(string Code, DateTimeOffset ExpiresAt);

public interface IDeviceService
{
    Task<LoadResult<IReadOnlyList<DeviceItem>>> ListAsync(Guid pageBusiness, CancellationToken cancellationToken = default);

    Task<LoadResult<PairingCode>> CreatePairingAsync(Guid pageBusiness, CancellationToken cancellationToken = default);

    Task<OwnerActionResult> RenameAsync(Guid pageBusiness, DeviceItem device, string name, CancellationToken cancellationToken = default);

    Task<OwnerActionResult> RevokeAsync(Guid pageBusiness, DeviceItem device, CancellationToken cancellationToken = default);
}

/// <summary>POS device calls (feature 007, US1) through the client generated from the contract.</summary>
public sealed class DeviceService(HttpClient http, ILogger<DeviceService> logger) : IDeviceService
{
    public Task<LoadResult<IReadOnlyList<DeviceItem>>> ListAsync(Guid pageBusiness, CancellationToken cancellationToken = default) =>
        StaffService.LoadAsync<IReadOnlyList<DeviceItem>>(http, logger, "list-devices", async () =>
        {
            var devices = await ApiClients.Create(http).Devices
                .GetAsync(c => c.Headers.Add(BusinessService.BusinessHeader, pageBusiness.ToString()), cancellationToken)
                .ConfigureAwait(false) ?? [];
            return devices.Where(d => d.Id is not null).Select(d => new DeviceItem(
                d.Id!.Value, d.Name ?? string.Empty, d.RegisteredBy ?? string.Empty, d.RegisteredAt ?? default,
                d.LastUsedAt, d.Active ?? false, d.Version ?? 0)).ToList();
        });

    public Task<LoadResult<PairingCode>> CreatePairingAsync(Guid pageBusiness, CancellationToken cancellationToken = default) =>
        StaffService.LoadAsync(http, logger, "create-pairing", async () =>
        {
            var pairing = await ApiClients.Create(http).Devices.Pairings
                .PostAsync(c => c.Headers.Add(BusinessService.BusinessHeader, pageBusiness.ToString()), cancellationToken)
                .ConfigureAwait(false);
            return new PairingCode(pairing?.Code ?? string.Empty, pairing?.ExpiresAt ?? default);
        });

    public Task<OwnerActionResult> RenameAsync(Guid pageBusiness, DeviceItem device, string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        return OwnerAccountService.RunAsync(http, logger, "rename-device", () => ApiClients.Create(http).Devices[device.Id]
            .PutAsync(new DeviceRename { Name = name, Version = device.Version },
                c => c.Headers.Add(BusinessService.BusinessHeader, pageBusiness.ToString()), cancellationToken));
    }

    public Task<OwnerActionResult> RevokeAsync(Guid pageBusiness, DeviceItem device, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        return OwnerAccountService.RunAsync(http, logger, "revoke-device", () => ApiClients.Create(http).Devices[device.Id].Revoke
            .PostAsync(new DeviceVersion { Version = device.Version },
                c => c.Headers.Add(BusinessService.BusinessHeader, pageBusiness.ToString()), cancellationToken));
    }
}
