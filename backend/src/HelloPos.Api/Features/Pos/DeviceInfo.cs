using HelloPos.Api.Infrastructure;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Infrastructure.Pos;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace HelloPos.Api.Features.Pos;

public sealed record PosDeviceInfo(Guid DeviceId, string DeviceName, Guid BusinessId, string BusinessName);

/// <summary>
/// <c>GET /pos/device</c> (feature 007): the calling device's registration. The app checks it at start; a
/// revoked or unknown device gets 401 and asks to be registered again.
/// </summary>
public sealed class DeviceInfo : IEndpointMapper
{
    public void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/pos/device", HandleAsync)
            .WithName("posDevice")
            .WithTags(PairDevice.Tag)
            .RequireAuthorization(PosAuthentication.DevicePolicy);

    public static async Task<Ok<PosDeviceInfo>> HandleAsync(HttpContext context, HelloPosDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(db);
        var deviceId = Guid.Parse(context.User.FindFirst(PosAuthentication.DeviceIdClaim)!.Value);
        var device = await db.Devices.AsNoTracking().SingleAsync(d => d.Id == deviceId, cancellationToken).ConfigureAwait(false);
        var business = await db.Businesses.AsNoTracking().SingleAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new PosDeviceInfo(device.Id, device.Name, business.Id, business.Name));
    }
}
