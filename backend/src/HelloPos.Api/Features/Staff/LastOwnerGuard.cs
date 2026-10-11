using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Kernel.Staff;
using Microsoft.EntityFrameworkCore;

namespace HelloPos.Api.Features.Staff;

/// <summary>
/// Keeps at least one active owner per business (feature 006, FR-005, research R6). Call inside the
/// transaction that may remove an owner: it locks the business row first, so two owners deactivating
/// each other at the same moment are serialised and the second one sees the first one's change.
/// </summary>
public static class LastOwnerGuard
{
    /// <summary>Locks the current business row until the transaction ends.</summary>
    public static async Task LockBusinessAsync(this HelloPosDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var business = db.CurrentBusinessId ?? throw new InvalidOperationException("No current business.");
        await db.Database
            .ExecuteSqlAsync($"SELECT 1 FROM pos.businesses WHERE id = {business} FOR UPDATE", cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Whether the business would still have an active owner if <paramref name="staffId"/> stopped
    /// being one. Call after <see cref="LockBusinessAsync"/>.
    /// </summary>
    public static async Task<bool> HasAnotherActiveOwnerAsync(this HelloPosDbContext db, Guid staffId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        return await db.StaffMembers
            .AnyAsync(s => s.Id != staffId && s.Active && s.Role == StaffRole.Owner, cancellationToken)
            .ConfigureAwait(false);
    }
}
