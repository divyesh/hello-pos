using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using HelloPos.Api.Infrastructure.Identity.Email;

namespace HelloPos.Api.Tests.Infrastructure;

/// <summary>Collects sent emails instead of delivering them (feature 005 tests).</summary>
public sealed partial class InMemoryEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> sent = new();

    public IReadOnlyList<EmailMessage> Sent => [.. sent];

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        sent.Enqueue(message);
        return Task.CompletedTask;
    }

    public IReadOnlyList<EmailMessage> To(string email) =>
        Sent.Where(m => string.Equals(m.To, email, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>The newest email of <paramref name="kind"/> to <paramref name="email"/>.</summary>
    public EmailMessage Last(string email, string kind) =>
        To(email).LastOrDefault(m => m.Kind == kind)
        ?? throw new InvalidOperationException($"No {kind} email was sent to {email}.");

    /// <summary>The owner id and token from the link in an email (verification or reset).</summary>
    public static (Guid OwnerId, string Token) LinkOf(EmailMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var match = LinkPattern().Match(message.TextBody);
        if (!match.Success)
        {
            throw new InvalidOperationException($"No link in the {message.Kind} email.");
        }

        return (Guid.Parse(match.Groups["owner"].Value), Uri.UnescapeDataString(match.Groups["token"].Value));
    }

    [GeneratedRegex(@"\?owner=(?<owner>[0-9a-f-]{36})&token=(?<token>\S+)")]
    private static partial Regex LinkPattern();
}
