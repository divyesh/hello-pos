using System.Net.Mail;
using HelloPos.Api.Infrastructure.Identity;
using HelloPos.Api.Infrastructure.Identity.Email;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace HelloPos.Api.Features.Owners;

/// <summary>Conventions shared by the owner account slices (feature 005, US1).</summary>
public static partial class OwnerEndpoints
{
    public const string Tag = "Owner";
    public const string VerificationRequiredType = "urn:hellopos:problem:verification-required";
    public const int EmailMaxLength = 256;

    /// <summary>Anonymous, CSRF-guarded and rate limited (sign-up, sign-in, links): research R2, R3.</summary>
    public static RouteHandlerBuilder AsOwnerAuthEndpoint(this RouteHandlerBuilder builder) =>
        builder.WithTags(Tag).AllowAnonymous().RequireClientHeader().RequireRateLimiting(IdentitySetup.OwnerAuthRateLimit);

    /// <summary>The one answer for every failed sign-in (FR-005, SC-003).</summary>
    public static ProblemHttpResult InvalidCredentials() =>
        TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Email or password is incorrect.");

    public static ProblemHttpResult InvalidLink() =>
        TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest,
            title: "This link is invalid, already used or expired. Ask for a new one.");

    public static ValidationProblem Invalid(Dictionary<string, string[]> errors) => TypedResults.ValidationProblem(errors);

    /// <summary>Adds an error for <paramref name="email"/> unless it is a plain email address.</summary>
    public static void ValidateEmail(string? email, string field, Dictionary<string, string[]> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var trimmed = email?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > EmailMaxLength
            || !MailAddress.TryCreate(trimmed, out var address) || address.Address != trimmed)
        {
            errors[field] = ["Enter a valid email address."];
        }
    }

    /// <summary>Runs the password rules (length, common passwords) and adds their messages under <paramref name="field"/>.</summary>
    public static async Task ValidatePasswordAsync(
        UserManager<OwnerAccount> users, OwnerAccount account, string? password, string field, Dictionary<string, string[]> errors)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(errors);
        if (string.IsNullOrEmpty(password))
        {
            errors[field] = ["Enter a password."];
            return;
        }

        var messages = new List<string>();
        foreach (var validator in users.PasswordValidators)
        {
            var result = await validator.ValidateAsync(users, account, password).ConfigureAwait(false);
            messages.AddRange(result.Errors.Select(e => e.Code == nameof(IdentityErrorDescriber.PasswordTooShort)
                ? $"Use at least {users.Options.Password.RequiredLength} characters."
                : e.Description));
        }

        if (messages.Count > 0)
        {
            errors[field] = [.. messages.Distinct(StringComparer.Ordinal)];
        }
    }

    /// <summary>
    /// Sends an owner email. A delivery failure is logged (kind only) and never changes the response,
    /// so responses cannot reveal anything about the account (FR-005).
    /// </summary>
    public static async Task SendSafelyAsync(IEmailSender sender, EmailMessage message, ILogger logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(message);
        try
        {
            await sender.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Any delivery failure must leave the response unchanged.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogSendFailed(logger, message.Kind, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not send {EmailKind} email")]
    private static partial void LogSendFailed(ILogger logger, string emailKind, Exception exception);
}
