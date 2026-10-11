using System.Text.Encodings.Web;
using Microsoft.Extensions.Options;

namespace HelloPos.Api.Infrastructure.Identity.Email;

/// <summary>
/// The owner emails (feature 005): verification, "you already have an account" (no enumeration,
/// research R2) and password reset. Links point to the web back office (<c>Web:BaseUrl</c>).
/// </summary>
public sealed class OwnerEmails(IOptions<WebSettings> web)
{
    public const string VerificationKind = "verification";
    public const string AlreadyRegisteredKind = "already-registered";
    public const string PasswordResetKind = "password-reset";
    public const string InvitationKind = "invitation";

    public EmailMessage Verification(string to, string displayName, Guid ownerId, string token) =>
        Build(to, VerificationKind, "Confirm your email for Hello POS", displayName,
            "Confirm your email address to start using Hello POS. The link works once, for 24 hours.",
            "Confirm email", Link("verify-email", ownerId, token),
            "If you did not sign up, ignore this email.");

    public EmailMessage AlreadyRegistered(string to, string displayName) =>
        Build(to, AlreadyRegisteredKind, "You already have a Hello POS account", displayName,
            "Someone tried to sign up with this email address, which already has an account.",
            "Sign in", $"{BaseUrl}/sign-in",
            "If you forgot your password, choose Forgot password on the sign-in page. If it was not you, ignore this email.");

    public EmailMessage PasswordReset(string to, string displayName, Guid ownerId, string token) =>
        Build(to, PasswordResetKind, "Reset your Hello POS password", displayName,
            "Choose a new password for Hello POS. The link works once, for 1 hour, and signs you out everywhere.",
            "Reset password", Link("reset-password", ownerId, token),
            "If you did not ask for this, ignore this email; your password stays the same.");

    /// <summary>Feature 006: an invitation to a back-office role; the link works once, for 7 days.</summary>
    public EmailMessage Invitation(string to, string displayName, string businessName, string role, string inviterName, string token) =>
        Build(to, InvitationKind, $"{inviterName} invites you to {businessName} on Hello POS", displayName,
            $"{inviterName} invites you to join {businessName} as {role}. Sign in or create a Hello POS account with this email address to accept. The link works once, for 7 days.",
            "Accept invitation", $"{BaseUrl}/accept-invitation?token={UrlEncoder.Default.Encode(token)}",
            "If you were not expecting this, ignore this email.");

    private string BaseUrl => web.Value.BaseUrl.TrimEnd('/');

    private string Link(string page, Guid ownerId, string token) =>
        $"{BaseUrl}/{page}?owner={ownerId}&token={UrlEncoder.Default.Encode(token)}";

    private static EmailMessage Build(
        string to, string kind, string subject, string displayName, string intro, string action, string link, string footer)
    {
        var html = HtmlEncoder.Default;
        var text = $"""
            Hello {displayName},

            {intro}

            {action}: {link}

            {footer}
            """;
        var body = $"""
            <p>Hello {html.Encode(displayName)},</p>
            <p>{html.Encode(intro)}</p>
            <p><a href="{html.Encode(link)}">{html.Encode(action)}</a></p>
            <p>{html.Encode(footer)}</p>
            """;
        return new EmailMessage(to, subject, text, body, kind);
    }
}

public static class EmailSetup
{
    public static IServiceCollection AddOwnerEmail(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.Configure<EmailSettings>(configuration.GetSection(EmailSettings.Section));
        services.Configure<WebSettings>(configuration.GetSection(WebSettings.Section));
        services.AddSingleton<OwnerEmails>();
        if (string.IsNullOrWhiteSpace(configuration[$"{EmailSettings.Section}:SmtpHost"]))
        {
            services.AddSingleton<IEmailSender, UnconfiguredEmailSender>();
        }
        else
        {
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        }

        return services;
    }
}
