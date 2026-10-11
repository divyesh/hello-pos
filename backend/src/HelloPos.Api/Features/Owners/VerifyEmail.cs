using HelloPos.Api.Infrastructure;
using HelloPos.Api.Infrastructure.Identity;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace HelloPos.Api.Features.Owners;

/// <summary>
/// <c>POST /owner/verify-email</c> (FR-004, SC-006). Confirming renews the security stamp, so the link
/// (and any other outstanding link) cannot be used again.
/// </summary>
public sealed class VerifyEmail : IEndpointMapper
{
    public sealed record Request(Guid OwnerId, string? Token);

    public void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/owner/verify-email", HandleAsync).WithName("ownerVerifyEmail").AsOwnerAuthEndpoint();

    public static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(Request request, UserManager<OwnerAccount> users)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(users);
        var account = await users.FindByIdAsync(request.OwnerId.ToString()).ConfigureAwait(false);
        if (account is null || string.IsNullOrEmpty(request.Token))
        {
            return OwnerEndpoints.InvalidLink();
        }

        var confirmed = await users.ConfirmEmailAsync(account, request.Token).ConfigureAwait(false);
        if (!confirmed.Succeeded)
        {
            return OwnerEndpoints.InvalidLink();
        }

        await users.UpdateSecurityStampAsync(account).ConfigureAwait(false);
        return TypedResults.NoContent();
    }
}
