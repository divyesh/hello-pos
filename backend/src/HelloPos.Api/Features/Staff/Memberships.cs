using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Kernel.Staff;

namespace HelloPos.Api.Features.Staff;

/// <summary>
/// The businesses a signed-in back-office account may open: its active owner and manager staff rows
/// (feature 006, research R1/R2). Spans businesses by design, before one is open.
/// </summary>
public static class Memberships
{
    /// <summary>
    /// Reviewed bypass of the business scope: filtered to <paramref name="accountId"/> here, and row-level
    /// security (<c>owner_read</c>) shows the session only this account's staff rows anyway.
    /// </summary>
    public static IQueryable<StaffMember> BackOfficeMemberships(this HelloPosDbContext db, Guid accountId)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.StaffMembers.AcrossAllBusinesses()
            .Where(s => s.AccountId == accountId && s.Active && s.Role != StaffRole.Cashier);
    }
}
