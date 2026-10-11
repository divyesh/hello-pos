using HelloPos.Api.Features.Owners;
using HelloPos.Api.Infrastructure;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace HelloPos.Api.Features.Businesses;

/// <summary>
/// <c>GET /owner/businesses</c> (FR-012): the businesses the signed-in owner owns, by name. Scoped
/// twice: the query filters on the owner, and row-level security (<c>owner_read</c>) shows the
/// session only its owner's links and businesses.
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

        // Reviewed bypass of the business scope: ownership links span businesses by design (the owner
        // has not opened one); the owner filter here and the owner_read policy keep it to this owner.
        var owned = db.BusinessOwners.AcrossAllBusinesses().Where(o => o.OwnerId == ownerId).Select(o => o.BusinessId);
        var businesses = await db.Businesses
            .Where(b => owned.Contains(b.Id))
            .OrderBy(b => b.Name)
            .ThenBy(b => b.Id)
            .Select(b => new BusinessSummaryResponse(b.Id, b.Name, b.CountryCode, b.CurrencyCode))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.Ok(businesses);
    }
}
