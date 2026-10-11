using HelloPos.Api.Features.Owners;
using HelloPos.Api.Features.Staff;
using HelloPos.Api.Infrastructure;
using HelloPos.Api.Infrastructure.Identity;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HelloPos.Api.Features.Businesses;

/// <summary>
/// <c>POST /owner/businesses</c> (FR-009–FR-012). The server generates the id, then, in one
/// transaction acting as that new business (research R6), inserts the business and the creator as its
/// first owner staff member (feature 006). Nothing in the request can choose or reach another business.
/// </summary>
public sealed class CreateBusiness : IEndpointMapper
{
    public void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/owner/businesses", HandleAsync)
            .WithName("ownerCreateBusiness")
            .WithTags(OwnerEndpoints.Tag)
            .RequireAuthorization()
            .RequireClientHeader();

    public static async Task<Results<Created<BusinessResponse>, ValidationProblem, ProblemHttpResult, UnauthorizedHttpResult>> HandleAsync(
        BusinessSettingsRequest request,
        ICurrentOwner owner,
        UserManager<OwnerAccount> users,
        DbContextOptions<HelloPosDbContext> databaseOptions,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(clock);
        var account = owner.OwnerId is { } ownerId ? await users.FindByIdAsync(ownerId.ToString()).ConfigureAwait(false) : null;
        if (account is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!account.EmailConfirmed)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                type: OwnerEndpoints.VerificationRequiredType,
                title: "Confirm your email address before creating a business.");
        }

        var (settings, errors) = BusinessContracts.Validate(request);
        if (settings is null)
        {
            return OwnerEndpoints.Invalid(errors);
        }

        var now = clock.GetUtcNow();
        var business = new Business
        {
            Id = Guid.CreateVersion7(),
            Name = settings.Name,
            CountryCode = settings.CountryCode,
            CurrencyCode = settings.CurrencyCode,
            TimeZone = settings.TimeZone,
            CreatedAt = now,
            UpdatedAt = now,
        };
        BusinessContracts.Apply(settings, business);

        await using var asNewBusiness = new HelloPosDbContext(databaseOptions, new FixedCurrentBusiness(business.Id));
        asNewBusiness.Businesses.Add(business);
        var creator = new StaffMember
        {
            DisplayName = account.DisplayName,
            Role = Kernel.Staff.StaffRole.Owner,
            AccountId = account.Id,
            CreatedAt = now,
            UpdatedAt = now,
        };
        asNewBusiness.StaffMembers.Add(creator);
        asNewBusiness.StaffChanges.Add(new StaffChange
        {
            StaffMemberId = creator.Id,
            ActorStaffId = creator.Id,
            Action = StaffChangeActions.Added,
            NewValue = StaffRoleNames.Owner,
            At = now,
        });
        await asNewBusiness.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return TypedResults.Created((string?)null, BusinessContracts.ToResponse(business));
    }
}
