using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.RateLimiting;
using HelloPos.Api.Features.Devices;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HelloPos.Api.Infrastructure.Pos;

/// <summary>
/// How the Android app authenticates (feature 007, research R1, R2). Two schemes:
/// <list type="bullet">
/// <item><c>Device</c>: <c>Authorization: Device dev_…</c> for device-level calls (sign in, refresh,
/// offline data). The device must belong to the business named in the credential and not be revoked.</item>
/// <item><c>PosSession</c>: <c>Authorization: Bearer pat_…</c> for a signed-in person. Every request checks
/// the session against the database (FR-008, FR-009).</item>
/// </list>
/// Each acts as the business named in the secret only to find its row; the request is accepted only when
/// the secret matches that row, so the business comes from an authenticated secret.
/// </summary>
public static class PosAuthentication
{
    public const string DeviceScheme = "Device";
    public const string SessionScheme = "PosSession";
    public const string DevicePolicy = "PosDevice";
    public const string SessionPolicy = "PosSession";

    /// <summary>Per-address limit for POS sign-in and refresh; a shop's tills share one address.</summary>
    public const string RateLimit = "pos-auth";

    public const string DeviceIdClaim = "device_id";
    public const string StaffIdClaim = "staff_id";
    public const string SessionIdClaim = "session_id";

    /// <summary>Usage timestamps are written at most this often (no write per request).</summary>
    public static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(1);

    public static IServiceCollection AddPosAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var permitPerMinute = configuration.GetValue("RateLimits:PosAuthPerMinute", 60);
        services.Configure<RateLimiterOptions>(options => options.AddPolicy(RateLimit, context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = permitPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
        // The default scheme picks by the Authorization header, so every endpoint (and RequireRole) sees
        // POS sessions too: Bearer pat_… → PosSession, Device … → Device, otherwise the web cookie.
        services.AddAuthentication(options =>
            {
                options.DefaultScheme = SelectingScheme;
                options.DefaultChallengeScheme = SelectingScheme;
            })
            .AddPolicyScheme(SelectingScheme, SelectingScheme, o => o.ForwardDefaultSelector = context =>
                SchemeFor(context.Request) ?? Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme)
            .AddScheme<AuthenticationSchemeOptions, DeviceCredentialHandler>(DeviceScheme, _ => { })
            .AddScheme<AuthenticationSchemeOptions, PosSessionHandler>(SessionScheme, _ => { });
        services.AddAuthorizationBuilder()
            .AddPolicy(DevicePolicy, p => p.AddAuthenticationSchemes(DeviceScheme).RequireClaim(DeviceIdClaim))
            .AddPolicy(SessionPolicy, p => p.AddAuthenticationSchemes(SessionScheme).RequireClaim(SessionIdClaim));
        return services;
    }

    public const string SelectingScheme = "HelloPos";

    /// <summary>The POS scheme the request's Authorization header asks for, or null (then the cookie applies).</summary>
    public static string? SchemeFor(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var header = request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer " + PosSecrets.Prefix(PosSecretKind.AccessToken), StringComparison.OrdinalIgnoreCase))
        {
            return SessionScheme;
        }

        return header.StartsWith(DeviceScheme + " ", StringComparison.OrdinalIgnoreCase) ? DeviceScheme : null;
    }

    /// <summary>The value after <paramref name="scheme"/> in the Authorization header, or null.</summary>
    internal static string? HeaderValue(HttpRequest request, string scheme)
    {
        var header = request.Headers.Authorization.ToString();
        var prefix = scheme + " ";
        return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? header[prefix.Length..].Trim() : null;
    }

    /// <summary>A context acting as <paramref name="businessId"/>, for checking a secret of that business.</summary>
    internal static HelloPosDbContext ActingAs(IServiceProvider services, Guid businessId) =>
        new(services.GetRequiredService<DbContextOptions<HelloPosDbContext>>(), new FixedCurrentBusiness(businessId));

    internal static bool ShouldTouch(DateTimeOffset? last, DateTimeOffset now) => last is null || now - last.Value >= TouchInterval;
}

/// <summary>Authenticates <c>Authorization: Device dev_…</c> (feature 007, research R1).</summary>
public sealed class DeviceCredentialHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, TimeProvider clock)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var value = PosAuthentication.HeaderValue(Request, PosAuthentication.DeviceScheme);
        if (value is null)
        {
            return AuthenticateResult.NoResult();
        }

        if (PosSecrets.Parse(PosSecretKind.DeviceCredential, value) is not { } parsed)
        {
            return AuthenticateResult.Fail("Malformed device credential.");
        }

        await using var db = PosAuthentication.ActingAs(Context.RequestServices, parsed.BusinessId);
        var device = await db.Devices.SingleOrDefaultAsync(d => d.Id == parsed.Id, Context.RequestAborted).ConfigureAwait(false);
        if (device is null || device.RevokedAt is not null || !PosSecrets.Matches(parsed, device.CredentialHash))
        {
            return AuthenticateResult.Fail("Unknown or revoked device.");
        }

        var now = clock.GetUtcNow();
        if (PosAuthentication.ShouldTouch(device.LastUsedAt, now))
        {
            device.LastUsedAt = now;
            await db.SaveChangesAsync(Context.RequestAborted).ConfigureAwait(false);
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimsCurrentBusiness.ClaimType, parsed.BusinessId.ToString()),
                new Claim(PosAuthentication.DeviceIdClaim, device.Id.ToString()),
            ],
            PosAuthentication.DeviceScheme);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), PosAuthentication.DeviceScheme));
    }
}

/// <summary>Authenticates <c>Authorization: Bearer pat_…</c> and enforces every FR-008 end condition.</summary>
public sealed class PosSessionHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, TimeProvider clock)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var value = PosAuthentication.HeaderValue(Request, "Bearer");
        if (value is null || !value.StartsWith(PosSecrets.Prefix(PosSecretKind.AccessToken), StringComparison.Ordinal))
        {
            return AuthenticateResult.NoResult();
        }

        if (PosSecrets.Parse(PosSecretKind.AccessToken, value) is not { } parsed)
        {
            return AuthenticateResult.Fail("Malformed access token.");
        }

        await using var db = PosAuthentication.ActingAs(Context.RequestServices, parsed.BusinessId);
        var session = await db.StaffSessions.SingleOrDefaultAsync(s => s.Id == parsed.Id, Context.RequestAborted).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        if (session is null || session.EndedAt is not null || !PosSecrets.Matches(parsed, session.AccessTokenHash)
            || session.AccessExpiresAt <= now)
        {
            return AuthenticateResult.Fail("Invalid or expired session.");
        }

        if (await SessionChecks.EndReasonAsync(db, session, now, Context.RequestAborted).ConfigureAwait(false) is { } reason)
        {
            session.EndedAt = now;
            session.EndReason = reason;
            await db.SaveChangesAsync(Context.RequestAborted).ConfigureAwait(false);
            return AuthenticateResult.Fail("Session ended.");
        }

        if (PosAuthentication.ShouldTouch(session.LastActiveAt, now))
        {
            session.LastActiveAt = now;
            await db.SaveChangesAsync(Context.RequestAborted).ConfigureAwait(false);
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimsCurrentBusiness.ClaimType, parsed.BusinessId.ToString()),
                new Claim(PosAuthentication.DeviceIdClaim, session.DeviceId.ToString()),
                new Claim(PosAuthentication.StaffIdClaim, session.StaffMemberId.ToString()),
                new Claim(PosAuthentication.SessionIdClaim, session.Id.ToString()),
            ],
            PosAuthentication.SessionScheme);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), PosAuthentication.SessionScheme));
    }
}

/// <summary>The FR-008 conditions under which a live session must end now.</summary>
public static class SessionChecks
{
    public static async Task<string?> EndReasonAsync(HelloPosDbContext db, StaffSession session, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(session);
        if (session.ExpiresAt <= now)
        {
            return SessionEndReasons.Expired;
        }

        var device = await db.Devices.AsNoTracking()
            .SingleAsync(d => d.Id == session.DeviceId, cancellationToken).ConfigureAwait(false);
        if (device.RevokedAt is not null)
        {
            return SessionEndReasons.DeviceRevoked;
        }

        var person = await db.StaffMembers.AsNoTracking()
            .SingleAsync(s => s.Id == session.StaffMemberId, cancellationToken).ConfigureAwait(false);
        if (!person.Active)
        {
            return SessionEndReasons.Deactivated;
        }

        return person.PinChangedAt is { } changed && session.StartedAt < changed ? SessionEndReasons.PinChanged : null;
    }
}
