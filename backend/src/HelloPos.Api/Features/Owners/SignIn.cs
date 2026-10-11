using HelloPos.Api.Infrastructure;
using HelloPos.Api.Infrastructure.Identity;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace HelloPos.Api.Features.Owners;

/// <summary>
/// <c>POST /owner/sign-in</c> (FR-002, FR-005, FR-006, SC-003, SC-004). Unknown email, wrong password
/// and locked account all get the same 401; an unverified account gets 403 only with the right
/// password. Success sets the session cookie with claim <c>owner_id</c>.
/// </summary>
public sealed class SignIn : IEndpointMapper
{
    public sealed record Request(string? Email, string? Password);

    public void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/owner/sign-in", HandleAsync).WithName("ownerSignIn").AsOwnerAuthEndpoint();

    public static async Task<Results<NoContent, ProblemHttpResult, ValidationProblem>> HandleAsync(
        Request request, UserManager<OwnerAccount> users, SignInManager<OwnerAccount> signIn, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(signIn);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            errors["email"] = ["Enter your email address."];
        }

        if (string.IsNullOrEmpty(request.Password))
        {
            errors["password"] = ["Enter your password."];
        }

        if (errors.Count > 0)
        {
            return OwnerEndpoints.Invalid(errors);
        }

        var account = await users.FindByEmailAsync(request.Email!.Trim()).ConfigureAwait(false);
        if (account is null)
        {
            // Same work as checking a real password, so timing does not reveal unknown emails.
            users.PasswordHasher.HashPassword(new OwnerAccount { DisplayName = string.Empty }, request.Password!);
            return OwnerEndpoints.InvalidCredentials();
        }

        var result = await signIn.CheckPasswordSignInAsync(account, request.Password!, lockoutOnFailure: true).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return OwnerEndpoints.InvalidCredentials();
        }

        if (!account.EmailConfirmed)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                type: OwnerEndpoints.VerificationRequiredType,
                title: "Confirm your email address first. We can send you a new link.");
        }

        await signIn.SignInWithClaimsAsync(account, isPersistent: false, [IdentitySetup.SessionStarted(clock)]).ConfigureAwait(false);
        return TypedResults.NoContent();
    }
}
