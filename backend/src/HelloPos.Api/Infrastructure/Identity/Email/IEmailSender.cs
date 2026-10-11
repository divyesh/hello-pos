namespace HelloPos.Api.Infrastructure.Identity.Email;

/// <summary>An email to one recipient, with plain-text and HTML bodies. <see cref="Kind"/> is safe to log.</summary>
public sealed record EmailMessage(string To, string Subject, string TextBody, string HtmlBody, string Kind);

/// <summary>
/// Sends transactional email (feature 005, FR-004b). SMTP locally (Mailpit) and until a production
/// provider is chosen (#25/#26). Implementations never log message bodies: they carry sign-in links.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
