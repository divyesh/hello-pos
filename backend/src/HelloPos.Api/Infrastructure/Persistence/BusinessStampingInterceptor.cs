using HelloPos.Api.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HelloPos.Api.Infrastructure.Persistence;

/// <summary>
/// On save: new business-owned records are stamped with the current business, whatever the caller
/// set (spec FR-010); existing ones can never move to another business; nothing business-owned is
/// written without a current business (FR-008).
/// </summary>
public sealed class BusinessStampingInterceptor : SaveChangesInterceptor
{
    public static readonly BusinessStampingInterceptor Instance = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Stamp(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Stamp(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private static void Stamp(DbContext? context)
    {
        if (context is not HelloPosDbContext db)
        {
            return;
        }

        db.ChangeTracker.DetectChanges();
        foreach (var entry in db.ChangeTracker.Entries<IBusinessOwned>().ToList())
        {
            if (entry.State is EntityState.Unchanged or EntityState.Detached)
            {
                continue;
            }

            var business = db.CurrentBusinessId ?? throw new NoCurrentBusinessException();
            var property = entry.Property(e => e.BusinessId);
            if (entry.State == EntityState.Added)
            {
                property.CurrentValue = business;
            }
            else if (property.IsModified || property.OriginalValue != business)
            {
                throw new BusinessReassignmentException();
            }
        }
    }
}
