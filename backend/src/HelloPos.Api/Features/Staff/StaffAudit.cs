using HelloPos.Api.Infrastructure.Persistence;

namespace HelloPos.Api.Features.Staff;

/// <summary>
/// Adds audit entries to the caller's unit of work, so each is saved in the same transaction as the
/// change it records (feature 006, FR-013). Values are roles, names, usernames or states: never a PIN
/// or a token.
/// </summary>
public static class StaffAudit
{
    public static void Record(
        this HelloPosDbContext db, TimeProvider clock, Guid actorStaffId, string action,
        Guid? staffMemberId = null, Guid? invitationId = null, string? oldValue = null, string? newValue = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(clock);
        db.StaffChanges.Add(new StaffChange
        {
            ActorStaffId = actorStaffId,
            Action = action,
            StaffMemberId = staffMemberId,
            InvitationId = invitationId,
            OldValue = oldValue,
            NewValue = newValue,
            At = clock.GetUtcNow(),
        });
    }
}
