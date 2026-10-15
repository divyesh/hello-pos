using HelloPos.Api.Features.Catalog;
using HelloPos.Api.Features.Catalog.Import;
using HelloPos.Api.Infrastructure;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Infrastructure.Tenancy;
using HelloPos.Kernel.Staff;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace HelloPos.Api.Features.Settings;

public sealed record SettingsChangeResponse(DateTimeOffset At, string Actor, string Entity, Guid EntityId, string Action, List<CatalogFieldChange> Changes);

/// <summary>
/// The settings history (feature #12, US5, FR-016): who changed which setting, when, and each field from
/// and to, newest first; owners and managers read it, nobody edits it.
/// </summary>
public sealed class SettingsHistory : IEndpointMapper
{
    public const int PageSize = 50;

    public void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/settings/changes", HandleAsync).WithName("listSettingsChanges").WithTags(SettingsSupport.Tag)
            .RequireRole(StaffRole.Owner, StaffRole.Manager).RequireSameBusiness();

    public static async Task<Results<Ok<List<SettingsChangeResponse>>, ValidationProblem>> HandleAsync(
        Guid? entityId, int? page, HelloPosDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var pageNumber = page ?? 1;
        if (pageNumber < 1)
        {
            return CatalogSupport.Invalid("page", "Use page 1 or more.");
        }

        var changes = await db.SettingsChanges.AsNoTracking()
            .Where(c => entityId == null || c.EntityId == entityId)
            .OrderByDescending(c => c.At).ThenByDescending(c => c.Id)
            .Skip((pageNumber - 1) * PageSize).Take(PageSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var actors = changes.Select(c => c.ActorStaffId).Distinct().ToList();
        var names = await db.StaffMembers.AsNoTracking().Where(s => actors.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.DisplayName, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(changes.Select(c => new SettingsChangeResponse(
            c.At, names.GetValueOrDefault(c.ActorStaffId, string.Empty), c.Entity, c.EntityId, c.Action,
            CatalogExportEndpoints.Fields(c.Changes))).ToList());
    }
}
