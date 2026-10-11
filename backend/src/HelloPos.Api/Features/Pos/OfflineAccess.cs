using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using HelloPos.Api.Features.Staff;
using HelloPos.Api.Infrastructure;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Infrastructure.Pos;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace HelloPos.Api.Features.Pos;

public sealed record PosOfflineAccess(string Version, DateTimeOffset GeneratedAt, byte[] Bundle);

/// <summary>One person the device can sign in offline (bundle plaintext; never a plain PIN).</summary>
public sealed record OfflineStaff(
    Guid StaffId, string Username, string DisplayName, string Role, string PinHash, DateTimeOffset? PinChangedAt);

public sealed record OfflineBundle(Guid BusinessId, DateTimeOffset GeneratedAt, IReadOnlyList<OfflineStaff> Staff);

/// <summary>
/// <c>GET /pos/offline-access</c> (feature 007, clarification Q1, research R4): the active staff of this
/// business with a PIN, encrypted to the calling device's RSA key so only that device can read it.
/// </summary>
public sealed class OfflineAccess : IEndpointMapper
{
    public const int WrappedKeyLengthBytes = 2;
    public const int NonceBytes = 12;
    public const int TagBytes = 16;

    /// <summary>
    /// OAEP with SHA-1: the Android Keystore supports only SHA-1 for OAEP's mask function before Android 14,
    /// and .NET uses the same digest for both. OAEP-SHA1 remains sound for key wrapping.
    /// </summary>
    public static readonly RSAEncryptionPadding KeyWrapPadding = RSAEncryptionPadding.OaepSHA1;

    internal static readonly JsonSerializerOptions BundleJson = new(JsonSerializerDefaults.Web);

    public void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/pos/offline-access", HandleAsync)
            .WithName("posOfflineAccess")
            .WithTags(PairDevice.Tag)
            .RequireAuthorization(PosAuthentication.DevicePolicy);

    public static async Task<Ok<PosOfflineAccess>> HandleAsync(
        HttpContext context, HelloPosDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(clock);
        var deviceId = PosSessionEndpoints.DeviceIdOf(context);
        var device = await db.Devices.AsNoTracking().SingleAsync(d => d.Id == deviceId, cancellationToken).ConfigureAwait(false);
        var staff = await db.StaffMembers.AsNoTracking()
            .Where(s => s.Active && s.PinHash != null && s.PosUsername != null)
            .OrderBy(s => s.PosUsername)
            .Select(s => new { s.Id, s.PosUsername, s.DisplayName, s.Role, s.PinHash, s.PinChangedAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var people = staff
            .Select(s => new OfflineStaff(s.Id, s.PosUsername!, s.DisplayName, StaffRoleNames.ToWire(s.Role), s.PinHash!, s.PinChangedAt))
            .ToList();

        var now = clock.GetUtcNow();
        var version = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(people, BundleJson)))[..16];
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(new OfflineBundle(device.BusinessId, now, people), BundleJson);
        return TypedResults.Ok(new PosOfflineAccess(version, now, Seal(plaintext, device.PublicKey)));
    }

    /// <summary>[2-byte wrapped-key length][RSA-wrapped AES-256 key][12-byte nonce][AES-GCM ciphertext][16-byte tag].</summary>
    public static byte[] Seal(byte[] plaintext, byte[] subjectPublicKeyInfo)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        var key = RandomNumberGenerator.GetBytes(32);
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out _);
            var wrapped = rsa.Encrypt(key, KeyWrapPadding);
            var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
            var output = new byte[WrappedKeyLengthBytes + wrapped.Length + NonceBytes + plaintext.Length + TagBytes];
            BinaryPrimitives.WriteUInt16BigEndian(output, checked((ushort)wrapped.Length));
            wrapped.CopyTo(output, WrappedKeyLengthBytes);
            var offset = WrappedKeyLengthBytes + wrapped.Length;
            nonce.CopyTo(output, offset);
            using var aes = new AesGcm(key, TagBytes);
            aes.Encrypt(
                nonce,
                plaintext,
                output.AsSpan(offset + NonceBytes, plaintext.Length),
                output.AsSpan(offset + NonceBytes + plaintext.Length, TagBytes));
            return output;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>The inverse of <see cref="Seal"/> (used by tests; the device does this in Kotlin).</summary>
    public static byte[] Open(byte[] sealedBundle, RSA privateKey)
    {
        ArgumentNullException.ThrowIfNull(sealedBundle);
        ArgumentNullException.ThrowIfNull(privateKey);
        var wrappedLength = BinaryPrimitives.ReadUInt16BigEndian(sealedBundle);
        var key = privateKey.Decrypt(sealedBundle.AsSpan(WrappedKeyLengthBytes, wrappedLength).ToArray(), KeyWrapPadding);
        var offset = WrappedKeyLengthBytes + wrappedLength;
        var cipherLength = sealedBundle.Length - offset - NonceBytes - TagBytes;
        var plaintext = new byte[cipherLength];
        using var aes = new AesGcm(key, TagBytes);
        aes.Decrypt(
            sealedBundle.AsSpan(offset, NonceBytes),
            sealedBundle.AsSpan(offset + NonceBytes, cipherLength),
            sealedBundle.AsSpan(offset + NonceBytes + cipherLength, TagBytes),
            plaintext);
        return plaintext;
    }
}
