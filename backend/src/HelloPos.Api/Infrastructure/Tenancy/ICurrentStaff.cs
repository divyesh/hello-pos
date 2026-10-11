using HelloPos.Api.Features.Staff;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Kernel.Staff;
using Microsoft.EntityFrameworkCore;

namespace HelloPos.Api.Infrastructure.Tenancy;

/// <summary>
/// The caller's staff membership in the current business (feature 006, research R3): read from the
/// database once per request, so a role change or deactivation applies to the very next request. Never
/// from a claim or the request.
/// </summary>
public interface ICurrentStaff
{
    /// <summary>The active staff row of the signed-in account in the current business, or <c>null</c>.</summary>
    Task<StaffMember?> GetAsync(CancellationToken cancellationToken = default);
}

public sealed class CurrentStaff(ICurrentBusiness business, ICurrentOwner owner, HelloPosDbContext db) : ICurrentStaff
{
    private Task<StaffMember?>? loaded;

    public Task<StaffMember?> GetAsync(CancellationToken cancellationToken = default) =>
        loaded ??= LoadAsync(cancellationToken);

    private async Task<StaffMember?> LoadAsync(CancellationToken cancellationToken)
    {
        if (business.BusinessId is null || owner.OwnerId is not { } account)
        {
            return null;
        }

        // The business scope filter limits this to the current business.
        return await db.StaffMembers.AsNoTracking()
            .SingleOrDefaultAsync(s => s.AccountId == account && s.Active, cancellationToken)
            .ConfigureAwait(false);
    }
}

public static class RequireRoleExtensions
{
    /// <summary>
    /// Only active staff of the current business with one of <paramref name="roles"/> get through:
    /// <c>401</c> without a session or open business, <c>403</c> for anyone else (FR-006).
    /// </summary>
    public static TBuilder RequireRole<TBuilder>(this TBuilder builder, params StaffRole[] roles)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(roles);
        return builder.RequireCurrentBusiness().AddEndpointFilter(async (context, next) =>
        {
            var staff = await context.HttpContext.RequestServices.GetRequiredService<ICurrentStaff>()
                .GetAsync(context.HttpContext.RequestAborted).ConfigureAwait(false);
            return staff is not null && roles.Contains(staff.Role)
                ? await next(context).ConfigureAwait(false)
                : TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden,
                    title: "Your role in this business does not allow this.");
        });
    }
}
