using System.Text.Json.Serialization;
using HelloPos.Api.Features.Staff;
using HelloPos.Api.Infrastructure;
using HelloPos.Api.Infrastructure.Identity;
using HelloPos.Api.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace HelloPos.Api.Features.Owners;

/// <summary>
/// <c>GET /owner/me</c>: the signed-in account, the business open in this session and the account's
/// role there (feature 006; absent when no business is open or the account is no longer active staff).
/// </summary>
public sealed class GetMe : IEndpointMapper
{
    public sealed record OwnerProfile(
        Guid OwnerId,
        string DisplayName,
        string Email,
        bool EmailVerified,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? CurrentBusinessId,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CurrentRole);

    public void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/owner/me", HandleAsync).WithName("ownerMe").WithTags(OwnerEndpoints.Tag).RequireAuthorization();

    public static async Task<Results<Ok<OwnerProfile>, UnauthorizedHttpResult>> HandleAsync(
        ICurrentOwner owner, ICurrentBusiness business, ICurrentStaff staff, UserManager<OwnerAccount> users,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(business);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(staff);
        var account = owner.OwnerId is { } id ? await users.FindByIdAsync(id.ToString()).ConfigureAwait(false) : null;
        if (account is null)
        {
            return TypedResults.Unauthorized();
        }

        var membership = await staff.GetAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new OwnerProfile(
            account.Id, account.DisplayName, account.Email!, account.EmailConfirmed, business.BusinessId,
            membership is { Role: not Kernel.Staff.StaffRole.Cashier } ? StaffRoleNames.ToWire(membership.Role) : null));
    }
}
