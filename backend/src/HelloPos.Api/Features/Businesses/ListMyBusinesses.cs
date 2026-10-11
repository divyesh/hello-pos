using HelloPos.Api.Features.Owners;
using HelloPos.Api.Features.Staff;
using HelloPos.Api.Infrastructure;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace HelloPos.Api.Features.Businesses;

/// <summary>
/// <c>GET /owner/businesses</c> (FR-012): the businesses the signed-in account may open (active owner
/// or manager, feature 006), by name, with the account's role. Scoped twice: the query filters on the
/// account, and row-level security (<c>owner_read</c>) shows the session only its own staff rows.
/// </summary>
public sealed class ListMyBusinesses : IEndpointMapper
{
    public void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/owner/businesses", HandleAsync)
            .WithName("ownerListBusinesses")
            .WithTags(OwnerEndpoints.Tag)
            .RequireAuthorization();

    public static async Task<Results<Ok<List<BusinessSummaryResponse>>, UnauthorizedHttpResult>> HandleAsync(
        ICurrentOwner owner, HelloPosDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(db);
        if (owner.OwnerId is not { } ownerId)
        {
            return TypedResults.Unauthorized();
        }

        var memberships = await db.BackOfficeMemberships(ownerId)
            .Select(s => new { s.BusinessId, s.Role })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var roles = memberships.ToDictionary(m => m.BusinessId, m => m.Role);
        var ids = roles.Keys.ToList();
        var businesses = await db.Businesses
            .Where(b => ids.Contains(b.Id))
            .OrderBy(b => b.Name)
            .ThenBy(b => b.Id)
            .Select(b => new { b.Id, b.Name, b.CountryCode, b.CurrencyCode })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.Ok(businesses
            .Select(b => new BusinessSummaryResponse(b.Id, b.Name, b.CountryCode, b.CurrencyCode, StaffRoleNames.ToWire(roles[b.Id])))
            .ToList());
    }
}
