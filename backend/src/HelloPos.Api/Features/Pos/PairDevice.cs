using System.Security.Cryptography;
using HelloPos.Api.Features.Devices;
using HelloPos.Api.Features.Owners;
using HelloPos.Api.Features.Staff;
using HelloPos.Api.Infrastructure;
using HelloPos.Api.Infrastructure.Identity;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Infrastructure.Pos;
using HelloPos.Api.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace HelloPos.Api.Features.Pos;

public sealed record PosPairRequest(string? Code, string? DeviceName, string? PublicKey);

public sealed record PosPairResult(Guid DeviceId, string DeviceCredential, Guid BusinessId, string BusinessName);

/// <summary>
/// <c>POST /pos/pair</c> (feature 007, US1, research R1): a device connects to a business with a one-time
/// code. The business is found from the code (<c>pos.pairing_business</c>), never from the request; the
/// code is used once (conditional update), and the device credential is answered only here.
/// </summary>
public sealed class PairDevice : IEndpointMapper
{
    public const string Tag = "Pos";
    public const int MinimumKeyBits = 2048;

    public void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/pos/pair", HandleAsync)
            .WithName("posPair")
            .WithTags(Tag)
            .AllowAnonymous()
            .RequireRateLimiting(IdentitySetup.OwnerAuthRateLimit);

    public static async Task<Results<Ok<PosPairResult>, ValidationProblem, ProblemHttpResult>> HandleAsync(
        PosPairRequest request, HelloPosDbContext anyContext, DbContextOptions<HelloPosDbContext> options, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(anyContext);
        ArgumentNullException.ThrowIfNull(clock);
        if (DeviceEndpoints.ValidateName(request.DeviceName, "deviceName") is { } badName)
        {
            return badName;
        }

        if (PublicKeyOf(request.PublicKey) is not { } publicKey)
        {
            return OwnerEndpoints.Invalid(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["publicKey"] = [$"Send an RSA public key of at least {MinimumKeyBits} bits (X.509, base64)."],
            });
        }

        if (PairingCodes.HashOf(request.Code) is not { } codeHash)
        {
            return InvalidCode();
        }

        var business = await anyContext.Database
            .SqlQuery<Guid?>($"SELECT pos.pairing_business({codeHash}) AS \"Value\"")
            .SingleAsync(cancellationToken).ConfigureAwait(false);
        if (business is not { } businessId)
        {
            return InvalidCode();
        }

        await using var db = new HelloPosDbContext(options, new FixedCurrentBusiness(businessId));
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        var pairing = await db.DevicePairings
            .SingleOrDefaultAsync(p => p.CodeHash == codeHash && p.UsedAt == null && p.ExpiresAt > now, cancellationToken)
            .ConfigureAwait(false);

        // Single use even when two devices race: only one conditional update can claim the code.
        var claimed = pairing is null ? 0 : await db.DevicePairings
            .Where(p => p.Id == pairing.Id && p.UsedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(p => p.UsedAt, now), cancellationToken)
            .ConfigureAwait(false);
        if (claimed == 0)
        {
            return InvalidCode();
        }

        var device = new Device
        {
            Name = request.DeviceName!.Trim(),
            CredentialHash = [],
            PublicKey = publicKey,
            RegisteredByStaffId = pairing!.CreatedByStaffId,
            RegisteredAt = now,
        };
        var (credential, credentialHash) = PosSecrets.Issue(PosSecretKind.DeviceCredential, businessId, device.Id);
        device.CredentialHash = credentialHash;
        db.Devices.Add(device);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await db.DevicePairings.Where(p => p.Id == pairing.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(p => p.DeviceId, device.Id), cancellationToken).ConfigureAwait(false);
        db.Record(clock, pairing.CreatedByStaffId, StaffChangeActions.DeviceRegistered, newValue: device.Name, deviceId: device.Id);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var businessName = await db.Businesses.Select(b => b.Name).SingleAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(new PosPairResult(device.Id, credential, businessId, businessName));
    }

    private static byte[]? PublicKeyOf(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return null;
        }

        try
        {
            var bytes = Convert.FromBase64String(base64);
            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(bytes, out _);
            return rsa.KeySize >= MinimumKeyBits ? bytes : null;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return null;
        }
    }

    private static ProblemHttpResult InvalidCode() => TypedResults.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "This code is wrong, already used or expired. Ask for a new code in the back office.");
}
