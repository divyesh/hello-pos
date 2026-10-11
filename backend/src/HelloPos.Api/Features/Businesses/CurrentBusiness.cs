using HelloPos.Api.Features.Owners;
using HelloPos.Api.Infrastructure;
using HelloPos.Api.Infrastructure.Identity;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace HelloPos.Api.Features.Businesses;

/// <summary>
/// <c>GET</c> and <c>PUT /business</c> (FR-014): settings of the business open in this session. The
/// business comes only from the session; the stale-tab header can refuse a request, never redirect it.
/// </summary>
public sealed class CurrentBusiness : IEndpointMapper
{
    public const string Tag = "Business";

    public void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/business", GetAsync)
            .WithName("getCurrentBusiness")
            .WithTags(Tag)
            .RequireCurrentBusiness()
            .RequireSameBusiness();

        app.MapPut("/business", UpdateAsync)
            .WithName("updateCurrentBusiness")
            .WithTags(Tag)
            .RequireClientHeader()
            .RequireCurrentBusiness()
            .RequireSameBusiness();
    }

    public static async Task<Results<Ok<BusinessResponse>, ProblemHttpResult>> GetAsync(
        ICurrentBusiness current, HelloPosDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(db);
        var business = await db.Businesses.AsNoTracking()
            .SingleOrDefaultAsync(b => b.Id == current.BusinessId, cancellationToken)
            .ConfigureAwait(false);
        return business is null
            ? TypedResults.Problem(statusCode: StatusCodes.Status404NotFound)
            : TypedResults.Ok(BusinessContracts.ToResponse(business));
    }

    public static async Task<Results<Ok<BusinessResponse>, ValidationProblem, ProblemHttpResult>> UpdateAsync(
        BusinessSettingsRequest request, ICurrentBusiness current, HelloPosDbContext db, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(clock);
        var (settings, errors) = BusinessContracts.Validate(request);
        if (settings is null)
        {
            return OwnerEndpoints.Invalid(errors);
        }

        var business = await db.Businesses
            .SingleOrDefaultAsync(b => b.Id == current.BusinessId, cancellationToken)
            .ConfigureAwait(false);
        if (business is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound);
        }

        BusinessContracts.Apply(settings, business);
        business.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(BusinessContracts.ToResponse(business));
    }
}
