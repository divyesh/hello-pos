using HelloPos.Api.Infrastructure;
using HelloPos.Api.Infrastructure.Identity;
using HelloPos.Api.Infrastructure.Identity.Email;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace HelloPos.Api.Features.Owners;

/// <summary>
/// <c>POST /owner/sign-up</c> (FR-001, FR-003, FR-005, FR-007). Valid requests always get 202: a new
/// address receives a verification link, a registered one an "already registered" notice.
/// </summary>
public sealed class SignUp : IEndpointMapper
{
    public sealed record Request(string? DisplayName, string? Email, string? Password);

    public void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/owner/sign-up", HandleAsync).WithName("ownerSignUp").AsOwnerAuthEndpoint();

    public static async Task<Results<Accepted, ValidationProblem>> HandleAsync(
        Request request, UserManager<OwnerAccount> users, IEmailSender sender, OwnerEmails emails,
        TimeProvider clock, ILoggerFactory loggers, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(emails);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(loggers);
        var logger = loggers.CreateLogger<SignUp>();
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var displayName = request.DisplayName?.Trim() ?? string.Empty;
        if (displayName.Length is 0 or > OwnerAccount.DisplayNameMaxLength)
        {
            errors["displayName"] = [$"Enter your name, 1 to {OwnerAccount.DisplayNameMaxLength} characters."];
        }

        OwnerEndpoints.ValidateEmail(request.Email, "email", errors);
        var email = request.Email?.Trim() ?? string.Empty;
        var account = new OwnerAccount { DisplayName = displayName, Email = email, UserName = email, CreatedAt = clock.GetUtcNow() };
        await OwnerEndpoints.ValidatePasswordAsync(users, account, request.Password, "password", errors).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return OwnerEndpoints.Invalid(errors);
        }

        var existing = await users.FindByEmailAsync(email).ConfigureAwait(false);
        if (existing is null)
        {
            var created = await users.CreateAsync(account, request.Password!).ConfigureAwait(false);
            if (created.Succeeded)
            {
                var token = await users.GenerateEmailConfirmationTokenAsync(account).ConfigureAwait(false);
                await OwnerEndpoints.SendSafelyAsync(sender, emails.Verification(email, displayName, account.Id, token), logger, cancellationToken)
                    .ConfigureAwait(false);
                return TypedResults.Accepted((string?)null);
            }

            // Lost a race with a concurrent sign-up for the same address: treat it as registered.
            existing = await users.FindByEmailAsync(email).ConfigureAwait(false);
            if (existing is null)
            {
                throw new InvalidOperationException(
                    "Creating the owner account failed: " + string.Join(", ", created.Errors.Select(e => e.Code)));
            }
        }
        else
        {
            // Same work as a new account, so timing does not tell the two apart.
            users.PasswordHasher.HashPassword(account, request.Password!);
        }

        await OwnerEndpoints.SendSafelyAsync(sender, emails.AlreadyRegistered(existing.Email!, existing.DisplayName), logger, cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.Accepted((string?)null);
    }
}
