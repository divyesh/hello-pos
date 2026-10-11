using System.Security.Claims;
using HelloPos.Api.Features.Owners;
using HelloPos.Api.Infrastructure;
using HelloPos.Api.Infrastructure.Identity;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HelloPos.Api.Features.Businesses;

/// <summary>
/// <c>POST /owner/businesses/{businessId}/open</c> (FR-012, FR-013, FR-015). After checking that the
/// signed-in owner owns the business, re-issues the session with claim <c>business_id</c>, which
/// <see cref="ClaimsCurrentBusiness"/> reads from then on. Opening another business switches. A business
/// the owner does not own answers exactly like one that does not exist.
/// </summary>
public sealed class OpenBusiness : IEndpointMapper
{
    public void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/owner/businesses/{businessId:guid}/open", HandleAsync)
            .WithName("ownerOpenBusiness")
            .WithTags(OwnerEndpoints.Tag)
            .RequireAuthorization()
            .RequireClientHeader();

    public static async Task<Results<NoContent, ProblemHttpResult, UnauthorizedHttpResult>> HandleAsync(
        Guid businessId, HttpContext context, ICurrentOwner owner, HelloPosDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(db);
        if (owner.OwnerId is not { } ownerId)
        {
            return TypedResults.Unauthorized();
        }

        // Reviewed bypass of the business scope: ownership is checked before a business is open; the
        // owner filter here and the owner_read policy keep it to this owner's links.
        var owns = await db.BusinessOwners.AcrossAllBusinesses()
            .AnyAsync(o => o.OwnerId == ownerId && o.BusinessId == businessId, cancellationToken)
            .ConfigureAwait(false);
        if (!owns)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound);
        }

        var identity = new ClaimsIdentity(
            context.User.Claims.Where(c => c.Type != ClaimsCurrentBusiness.ClaimType),
            IdentityConstants.ApplicationScheme);
        identity.AddClaim(new Claim(ClaimsCurrentBusiness.ClaimType, businessId.ToString()));
        await context.SignInAsync(IdentityConstants.ApplicationScheme, new ClaimsPrincipal(identity)).ConfigureAwait(false);
        return TypedResults.NoContent();
    }
}
