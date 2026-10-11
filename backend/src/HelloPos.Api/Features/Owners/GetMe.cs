using System.Text.Json.Serialization;
using HelloPos.Api.Infrastructure;
using HelloPos.Api.Infrastructure.Identity;
using HelloPos.Api.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace HelloPos.Api.Features.Owners;

/// <summary><c>GET /owner/me</c>: the signed-in owner and the business open in this session.</summary>
public sealed class GetMe : IEndpointMapper
{
    public sealed record OwnerProfile(
        Guid OwnerId,
        string DisplayName,
        string Email,
        bool EmailVerified,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? CurrentBusinessId);

    public void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/owner/me", HandleAsync).WithName("ownerMe").WithTags(OwnerEndpoints.Tag).RequireAuthorization();

    public static async Task<Results<Ok<OwnerProfile>, UnauthorizedHttpResult>> HandleAsync(
        ICurrentOwner owner, ICurrentBusiness business, UserManager<OwnerAccount> users)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(business);
        ArgumentNullException.ThrowIfNull(users);
        var account = owner.OwnerId is { } id ? await users.FindByIdAsync(id.ToString()).ConfigureAwait(false) : null;
        if (account is null)
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(new OwnerProfile(
            account.Id, account.DisplayName, account.Email!, account.EmailConfirmed, business.BusinessId));
    }
}
