using HelloPos.Api.Infrastructure;
using HelloPos.Api.Infrastructure.Identity;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace HelloPos.Api.Features.Owners;

/// <summary>
/// <c>POST /owner/reset-password</c> (FR-004a). Resetting renews the security stamp: the link cannot be
/// reused and every existing session ends on its next request. It also lifts a lockout.
/// </summary>
public sealed class ResetPassword : IEndpointMapper
{
    public sealed record Request(Guid OwnerId, string? Token, string? NewPassword);

    public void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/owner/reset-password", HandleAsync).WithName("ownerResetPassword").AsOwnerAuthEndpoint();

    public static async Task<Results<NoContent, ProblemHttpResult, ValidationProblem>> HandleAsync(
        Request request, UserManager<OwnerAccount> users)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(users);
        var account = await users.FindByIdAsync(request.OwnerId.ToString()).ConfigureAwait(false);
        if (account is null || string.IsNullOrEmpty(request.Token))
        {
            return OwnerEndpoints.InvalidLink();
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        await OwnerEndpoints.ValidatePasswordAsync(users, account, request.NewPassword, "newPassword", errors).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return OwnerEndpoints.Invalid(errors);
        }

        var reset = await users.ResetPasswordAsync(account, request.Token, request.NewPassword!).ConfigureAwait(false);
        if (!reset.Succeeded)
        {
            return OwnerEndpoints.InvalidLink();
        }

        await users.ResetAccessFailedCountAsync(account).ConfigureAwait(false);
        await users.SetLockoutEndDateAsync(account, null).ConfigureAwait(false);
        return TypedResults.NoContent();
    }
}
