using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Kernel.Staff;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HelloPos.Api.Features.Staff;

/// <summary>
/// POS usernames and PINs (feature 006, FR-008–FR-010, research R5). PINs are hashed with Identity's
/// password hasher (PBKDF2, per-hash salt) and never returned or logged. <see cref="VerifyPinAsync"/>
/// and <see cref="ChangeOwnPinAsync"/> are the operations POS sign-in (#7) builds on; brute-force
/// protection (registered devices, attempt limits) is #7's job.
/// </summary>
public sealed class StaffCredentials(IPasswordHasher<StaffMember> hasher, TimeProvider clock)
{
    /// <summary>Sets or resets the PIN (and username); the caller validated both and saves.</summary>
    public void SetPin(StaffMember staff, string posUsername, string pin)
    {
        ArgumentNullException.ThrowIfNull(staff);
        staff.PosUsername = StaffRules.NormalizePosUsername(posUsername);
        staff.PinHash = hasher.HashPassword(staff, pin);
        staff.PinChangedAt = clock.GetUtcNow();
    }

    /// <summary>The active staff member of the current business with this username and PIN, or <c>null</c>.</summary>
    public async Task<StaffMember?> VerifyPinAsync(HelloPosDbContext db, string posUsername, string pin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(posUsername);
        var username = StaffRules.NormalizePosUsername(posUsername);
        var staff = await db.StaffMembers
            .SingleOrDefaultAsync(s => s.PosUsername == username, cancellationToken)
            .ConfigureAwait(false);
        if (staff is not { Active: true, PinHash: { } hash })
        {
            hasher.HashPassword(new StaffMember { DisplayName = string.Empty }, pin); // same work either way
            return null;
        }

        return hasher.VerifyHashedPassword(staff, hash, pin) == PasswordVerificationResult.Failed ? null : staff;
    }

    /// <summary>
    /// A staff member changes their own PIN by giving the current one (FR-010). Returns the field error,
    /// or <c>null</c> when changed (the caller saves).
    /// </summary>
    public string? ChangeOwnPin(StaffMember staff, string currentPin, string newPin)
    {
        ArgumentNullException.ThrowIfNull(staff);
        if (staff.PinHash is null || hasher.VerifyHashedPassword(staff, staff.PinHash, currentPin) == PasswordVerificationResult.Failed)
        {
            return "The current PIN is not correct.";
        }

        if (PinRules.Validate(newPin) is { } invalid)
        {
            return invalid;
        }

        SetPin(staff, staff.PosUsername!, newPin);
        return null;
    }
}
