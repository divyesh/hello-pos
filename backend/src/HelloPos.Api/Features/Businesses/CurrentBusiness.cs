using HelloPos.Api.Features.Catalog;
using HelloPos.Api.Features.Owners;
using HelloPos.Api.Infrastructure;
using HelloPos.Api.Infrastructure.Identity;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Http.HttpResults;
using HelloPos.Kernel.Staff;
using Microsoft.EntityFrameworkCore;

namespace HelloPos.Api.Features.Businesses;

/// <summary>
/// <c>GET</c> and <c>PUT /business</c> (FR-014): settings of the business open in this session. The
/// business comes only from the session; the stale-tab header can refuse a request, never redirect it.
/// Owners and managers read the settings; only owners change them (feature 006).
/// </summary>
public sealed class CurrentBusiness : IEndpointMapper
{
    public const string Tag = "Business";

    public void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/business", GetAsync)
            .WithName("getCurrentBusiness")
            .WithTags(Tag)
            .RequireRole(StaffRole.Owner, StaffRole.Manager)
            .RequireSameBusiness();

        app.MapPut("/business", UpdateAsync)
            .WithName("updateCurrentBusiness")
            .WithTags(Tag)
            .RequireClientHeader()
            .RequireRole(StaffRole.Owner)
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
        BusinessSettingsRequest request, ICurrentBusiness current, ICurrentStaff staff, HelloPosDbContext db, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(staff);
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

        // Absent means unchanged, so an older client cannot switch it off by omission (feature #11, FR-016).
        if (request.DepositsTaxable is { } depositsTaxable && depositsTaxable != business.DepositsTaxable)
        {
            var actor = (await staff.GetAsync(cancellationToken).ConfigureAwait(false))!;
            db.RecordChange(clock, actor.Id, CatalogEntityNames.Business, business.Id, CatalogActions.Updated,
                new Dictionary<string, object?[]> { ["depositsTaxable"] = [business.DepositsTaxable, depositsTaxable] });
            business.DepositsTaxable = depositsTaxable;
        }

        business.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(BusinessContracts.ToResponse(business));
    }
}
