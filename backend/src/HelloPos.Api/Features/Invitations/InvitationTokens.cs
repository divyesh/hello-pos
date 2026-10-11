using System.Security.Cryptography;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Infrastructure.Tenancy;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace HelloPos.Api.Features.Invitations;

/// <summary>
/// Invitation tokens (feature 006, research R7): 32 random bytes sent only in the email link; the
/// database keeps their SHA-256.
/// </summary>
public static class InvitationTokens
{
    private const int TokenBytes = 32;

    public static (string Token, byte[] Hash) Create()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenBytes);
        return (WebEncoders.Base64UrlEncode(bytes), SHA256.HashData(bytes));
    }

    /// <summary>The hash of a well-formed token, or <c>null</c>.</summary>
    public static byte[]? HashOf(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        try
        {
            var bytes = WebEncoders.Base64UrlDecode(token);
            return bytes.Length == TokenBytes ? SHA256.HashData(bytes) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Finds a pending, unexpired invitation from its token, for a caller with no business open. The
    /// business comes from the token itself (through <c>pos.invitation_business</c>), never from the
    /// request; the returned context acts as that business. Dispose the context.
    /// </summary>
    public static async Task<(HelloPosDbContext Db, StaffInvitation Invitation)?> FindPendingAsync(
        string? token, HelloPosDbContext anyContext, DbContextOptions<HelloPosDbContext> options, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(anyContext);
        ArgumentNullException.ThrowIfNull(clock);
        if (HashOf(token) is not { } hash)
        {
            return null;
        }

        var business = await anyContext.Database
            .SqlQuery<Guid?>($"SELECT pos.invitation_business({hash}) AS \"Value\"")
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
        if (business is not { } businessId)
        {
            return null;
        }

        var db = new HelloPosDbContext(options, new FixedCurrentBusiness(businessId));
        var invitation = await db.StaffInvitations
            .SingleOrDefaultAsync(i => i.TokenHash == hash && i.Status == InvitationStatus.Pending, cancellationToken)
            .ConfigureAwait(false);
        if (invitation is null || invitation.ExpiresAt <= clock.GetUtcNow())
        {
            await db.DisposeAsync().ConfigureAwait(false);
            return null;
        }

        return (db, invitation);
    }
}
