using System.Text.Json.Serialization;
using HelloPos.Api.Features.Owners;
using HelloPos.Api.Features.Staff;
using HelloPos.Api.Infrastructure;
using HelloPos.Api.Infrastructure.Identity;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Infrastructure.Pos;
using HelloPos.Api.Infrastructure.Tenancy;
using HelloPos.Kernel.Staff;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace HelloPos.Api.Features.Devices;

/// <summary>Contract <c>Device</c>.</summary>
public sealed record DeviceResponse(
    Guid Id,
    string Name,
    string RegisteredBy,
    DateTimeOffset RegisteredAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? LastUsedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? RevokedAt,
    bool Active,
    int Version);

public sealed record DevicePairingResponse(string Code, DateTimeOffset ExpiresAt);

public sealed record DeviceRenameRequest(string? Name, int Version);

public sealed record DeviceVersionRequest(int Version);

/// <summary>
/// POS devices in the back office (feature 007, US1): owners and managers of the open business create a
/// one-time pairing code, list, rename and revoke devices. Revoking ends every session on the device.
/// </summary>
public sealed class DeviceEndpoints : IEndpointMapper
{
    public const string Tag = "Devices";
    public const string DeviceChangedType = "urn:hellopos:problem:device-changed";

    public void Map(IEndpointRouteBuilder app)
    {
        var devices = app.MapGroup("/devices")
            .WithTags(Tag)
            .RequireRole(StaffRole.Owner, StaffRole.Manager)
            .RequireSameBusiness();
        devices.MapGet("", ListAsync).WithName("listDevices");
        devices.MapGet("/{deviceId:guid}", GetAsync).WithName("getDevice");
        devices.MapPost("/pairings", CreatePairingAsync).WithName("createDevicePairing").RequireClientHeader();
        devices.MapPut("/{deviceId:guid}", RenameAsync).WithName("renameDevice").RequireClientHeader();
        devices.MapPost("/{deviceId:guid}/revoke", RevokeAsync).WithName("revokeDevice").RequireClientHeader();
    }

    public static async Task<Ok<List<DeviceResponse>>> ListAsync(HelloPosDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var devices = await db.Devices.AsNoTracking()
            .OrderBy(d => d.RevokedAt == null ? 0 : 1).ThenBy(d => d.Name).ThenBy(d => d.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var names = await NamesAsync(db, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(devices.Select(d => ToResponse(d, names)).ToList());
    }

    public static async Task<Results<Ok<DeviceResponse>, ProblemHttpResult>> GetAsync(
        Guid deviceId, HelloPosDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var device = await db.Devices.AsNoTracking().SingleOrDefaultAsync(d => d.Id == deviceId, cancellationToken).ConfigureAwait(false);
        return device is null
            ? StaffProblems.NotFound()
            : TypedResults.Ok(ToResponse(device, await NamesAsync(db, cancellationToken).ConfigureAwait(false)));
    }

    public static async Task<Created<DevicePairingResponse>> CreatePairingAsync(
        ICurrentStaff current, HelloPosDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(clock);
        var actor = (await current.GetAsync(cancellationToken).ConfigureAwait(false))!;
        var now = clock.GetUtcNow();
        var (code, hash) = PairingCodes.Issue();
        var pairing = new DevicePairing
        {
            CodeHash = hash,
            ExpiresAt = now + DevicePairing.Lifetime,
            CreatedByStaffId = actor.Id,
            CreatedAt = now,
        };
        db.DevicePairings.Add(pairing);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Created((string?)null, new DevicePairingResponse(code, pairing.ExpiresAt));
    }

    public static async Task<Results<Ok<DeviceResponse>, ValidationProblem, ProblemHttpResult>> RenameAsync(
        Guid deviceId, DeviceRenameRequest request, ICurrentStaff current, HelloPosDbContext db, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(clock);
        if (ValidateName(request.Name) is { } invalid)
        {
            return invalid;
        }

        var actor = (await current.GetAsync(cancellationToken).ConfigureAwait(false))!;
        var device = await db.Devices.SingleOrDefaultAsync(d => d.Id == deviceId, cancellationToken).ConfigureAwait(false);
        if (device is null)
        {
            return StaffProblems.NotFound();
        }

        if (device.Version != request.Version)
        {
            return DeviceChanged();
        }

        var name = request.Name!.Trim();
        if (name != device.Name)
        {
            db.Record(clock, actor.Id, StaffChangeActions.DeviceRenamed, oldValue: device.Name, newValue: name, deviceId: device.Id);
            device.Name = name;
        }

        return await SaveAsync(db, device, request.Version, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<Results<Ok<DeviceResponse>, ValidationProblem, ProblemHttpResult>> RevokeAsync(
        Guid deviceId, DeviceVersionRequest request, ICurrentStaff current, HelloPosDbContext db, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(clock);
        var actor = (await current.GetAsync(cancellationToken).ConfigureAwait(false))!;
        var device = await db.Devices.SingleOrDefaultAsync(d => d.Id == deviceId, cancellationToken).ConfigureAwait(false);
        if (device is null)
        {
            return StaffProblems.NotFound();
        }

        if (device.Version != request.Version)
        {
            return DeviceChanged();
        }

        if (device.RevokedAt is null)
        {
            var now = clock.GetUtcNow();
            device.RevokedAt = now;
            device.RevokedByStaffId = actor.Id;
            await db.StaffSessions
                .Where(s => s.DeviceId == device.Id && s.EndedAt == null)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(s => s.EndedAt, now)
                    .SetProperty(s => s.EndReason, SessionEndReasons.DeviceRevoked), cancellationToken)
                .ConfigureAwait(false);
            db.Record(clock, actor.Id, StaffChangeActions.DeviceRevoked, deviceId: device.Id);
        }

        return await SaveAsync(db, device, request.Version, cancellationToken).ConfigureAwait(false);
    }

    internal static ValidationProblem? ValidateName(string? name, string field = "name") =>
        string.IsNullOrWhiteSpace(name) || name.Trim().Length > Device.NameMaxLength
            ? OwnerEndpoints.Invalid(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [field] = [$"Enter a device name of 1 to {Device.NameMaxLength} characters, for example Front till."],
            })
            : null;

    private static async Task<Results<Ok<DeviceResponse>, ValidationProblem, ProblemHttpResult>> SaveAsync(
        HelloPosDbContext db, Device device, int version, CancellationToken cancellationToken)
    {
        if (db.ChangeTracker.HasChanges())
        {
            db.Entry(device).Property(d => d.Version).OriginalValue = version;
            device.Version = version + 1;
            try
            {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                return DeviceChanged();
            }
        }

        return TypedResults.Ok(ToResponse(device, await NamesAsync(db, cancellationToken).ConfigureAwait(false)));
    }

    private static Task<Dictionary<Guid, string>> NamesAsync(HelloPosDbContext db, CancellationToken cancellationToken) =>
        db.StaffMembers.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.DisplayName, cancellationToken);

    private static DeviceResponse ToResponse(Device device, Dictionary<Guid, string> names) => new(
        device.Id, device.Name, names.GetValueOrDefault(device.RegisteredByStaffId, string.Empty), device.RegisteredAt,
        device.LastUsedAt, device.RevokedAt, device.RevokedAt is null, device.Version);

    private static ProblemHttpResult DeviceChanged() => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict, type: DeviceChangedType,
        title: "Someone else changed this device in the meantime. Reload and try again.");
}
