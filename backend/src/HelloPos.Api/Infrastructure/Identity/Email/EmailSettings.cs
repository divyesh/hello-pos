namespace HelloPos.Api.Infrastructure.Identity.Email;

/// <summary>Configuration section <c>Email</c>.</summary>
public sealed class EmailSettings
{
    public const string Section = "Email";

    public string? SmtpHost { get; set; }

    public int SmtpPort { get; set; } = 587;

    /// <summary><c>Auto</c> (default), <c>None</c>, <c>StartTls</c> or <c>SslOnConnect</c>.</summary>
    public string Security { get; set; } = "Auto";

    public string? UserName { get; set; }

    public string? Password { get; set; }

    public string From { get; set; } = "Hello POS <no-reply@hellopos.local>";
}

/// <summary>Configuration section <c>Web</c>: where links in emails point (the web back office).</summary>
public sealed class WebSettings
{
    public const string Section = "Web";

    public string BaseUrl { get; set; } = "http://localhost:5090";
}
