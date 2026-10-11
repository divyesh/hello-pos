using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace HelloPos.Api.Infrastructure.Identity.Email;

/// <summary>SMTP delivery through MailKit (research R5). Logs the kind of email only, never addresses or links.</summary>
public sealed partial class SmtpEmailSender(IOptions<EmailSettings> settings, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var options = settings.Value;
        using var mime = new MimeMessage();
        mime.From.Add(MailboxAddress.Parse(options.From));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { TextBody = message.TextBody, HtmlBody = message.HtmlBody }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(options.SmtpHost ?? throw new InvalidOperationException("Email:SmtpHost is not configured."), options.SmtpPort,
            Enum.Parse<SecureSocketOptions>(options.Security, ignoreCase: true), cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(options.UserName))
        {
            await client.AuthenticateAsync(options.UserName, options.Password ?? string.Empty, cancellationToken).ConfigureAwait(false);
        }

        await client.SendAsync(mime, cancellationToken).ConfigureAwait(false);
        await client.DisconnectAsync(quit: true, cancellationToken).ConfigureAwait(false);
        LogSent(logger, message.Kind);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Sent {EmailKind} email")]
    private static partial void LogSent(ILogger logger, string emailKind);
}

/// <summary>Used when no SMTP server is configured: drops the email and says so (without its content).</summary>
public sealed partial class UnconfiguredEmailSender(ILogger<UnconfiguredEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        LogDropped(logger, message.Kind);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{EmailKind} email not sent: Email:SmtpHost is not configured")]
    private static partial void LogDropped(ILogger logger, string emailKind);
}
