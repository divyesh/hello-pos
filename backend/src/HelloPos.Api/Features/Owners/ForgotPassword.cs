using HelloPos.Api.Infrastructure;
using HelloPos.Api.Infrastructure.Identity;
using HelloPos.Api.Infrastructure.Identity.Email;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace HelloPos.Api.Features.Owners;

/// <summary><c>POST /owner/forgot-password</c> (FR-004a, FR-005): always 202; mails only existing accounts.</summary>
public sealed class ForgotPassword : IEndpointMapper
{
    public sealed record Request(string? Email);

    public void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/owner/forgot-password", HandleAsync).WithName("ownerForgotPassword").AsOwnerAuthEndpoint();

    public static async Task<Results<Accepted, ValidationProblem>> HandleAsync(
        Request request, UserManager<OwnerAccount> users, IEmailSender sender, OwnerEmails emails,
        ILoggerFactory loggers, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(emails);
        ArgumentNullException.ThrowIfNull(loggers);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        OwnerEndpoints.ValidateEmail(request.Email, "email", errors);
        if (errors.Count > 0)
        {
            return OwnerEndpoints.Invalid(errors);
        }

        var account = await users.FindByEmailAsync(request.Email!.Trim()).ConfigureAwait(false);
        if (account is not null)
        {
            var token = await users.GeneratePasswordResetTokenAsync(account).ConfigureAwait(false);
            await OwnerEndpoints.SendSafelyAsync(sender, emails.PasswordReset(account.Email!, account.DisplayName, account.Id, token),
                loggers.CreateLogger<ForgotPassword>(), cancellationToken).ConfigureAwait(false);
        }

        return TypedResults.Accepted((string?)null);
    }
}
