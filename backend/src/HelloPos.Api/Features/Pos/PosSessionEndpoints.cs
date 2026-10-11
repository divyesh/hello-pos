using System.Globalization;
using HelloPos.Api.Features.Owners;
using HelloPos.Api.Features.Staff;
using HelloPos.Api.Infrastructure;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Infrastructure.Pos;
using HelloPos.Kernel.Staff;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HelloPos.Api.Features.Pos;

public sealed record PosSignInRequest(string? Username, string? Pin);

public sealed record PosRefreshRequest(string? RefreshToken);

public sealed record PosTokens(string AccessToken, DateTimeOffset AccessExpiresAt, string RefreshToken, DateTimeOffset SessionExpiresAt);

public sealed record PosStaff(Guid StaffId, string DisplayName, string Role, Guid BusinessId, string BusinessName);

public sealed record PosSignInResult(PosTokens Tokens, PosStaff Staff);

/// <summary>
/// Signing in and out on a registered device (feature 007, US2, research R2, R3). Wrong, unknown,
/// inactive, locked and other-business usernames all get the same 401. Five failures lock the person for
/// 15 minutes on every device; repeated failures on one device make it wait.
/// </summary>
public sealed partial class PosSessionEndpoints : IEndpointMapper
{
    public const int LockAfterFailures = 5;
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);
    public const int DeviceBackOffAfter = 3;
    public static readonly TimeSpan MaximumBackOff = TimeSpan.FromMinutes(5);

    public void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/pos/sign-in", SignInAsync).WithName("posSignIn").WithTags(PairDevice.Tag)
            .RequireAuthorization(PosAuthentication.DevicePolicy).RequireRateLimiting(PosAuthentication.RateLimit);
        app.MapPost("/pos/refresh", RefreshAsync).WithName("posRefresh").WithTags(PairDevice.Tag)
            .RequireAuthorization(PosAuthentication.DevicePolicy).RequireRateLimiting(PosAuthentication.RateLimit);
        app.MapPost("/pos/sign-out", SignOutAsync).WithName("posSignOut").WithTags(PairDevice.Tag)
            .RequireAuthorization(PosAuthentication.SessionPolicy);
        app.MapGet("/pos/me", MeAsync).WithName("posMe").WithTags(PairDevice.Tag)
            .RequireAuthorization(PosAuthentication.SessionPolicy);
    }

    public static async Task<Results<Ok<PosSignInResult>, ValidationProblem, ProblemHttpResult>> SignInAsync(
        PosSignInRequest request, HttpContext context, HelloPosDbContext db, IPasswordHasher<StaffMember> hasher,
        TimeProvider clock, ILogger<PosSessionEndpoints> logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(hasher);
        ArgumentNullException.ThrowIfNull(clock);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(request.Username))
        {
            errors["username"] = ["Enter your username."];
        }

        if (string.IsNullOrEmpty(request.Pin))
        {
            errors["pin"] = ["Enter your PIN."];
        }

        if (errors.Count > 0)
        {
            return OwnerEndpoints.Invalid(errors);
        }

        var now = clock.GetUtcNow();
        var deviceId = DeviceIdOf(context);
        var device = await db.Devices.SingleAsync(d => d.Id == deviceId, cancellationToken).ConfigureAwait(false);
        if (device.NextAttemptAt is { } next && next > now)
        {
            context.Response.Headers.RetryAfter = Math.Ceiling((next - now).TotalSeconds).ToString(CultureInfo.InvariantCulture);
            return TypedResults.Problem(statusCode: StatusCodes.Status429TooManyRequests,
                title: "Too many wrong attempts on this device. Wait a moment and try again.");
        }

        var username = StaffRules.NormalizePosUsername(request.Username!);
        var person = await db.StaffMembers.SingleOrDefaultAsync(s => s.PosUsername == username, cancellationToken).ConfigureAwait(false);
        var locked = person?.PinLockedUntil is { } until && until > now;
        var verified = person is { Active: true, PinHash: { } hash } && !locked
            && hasher.VerifyHashedPassword(person, hash, request.Pin!) != PasswordVerificationResult.Failed;
        if (person?.PinHash is null)
        {
            hasher.HashPassword(new StaffMember { DisplayName = string.Empty }, request.Pin!); // same work either way
        }

        if (!verified)
        {
            RecordFailure(db, device, person, locked, now, clock);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            LogFailed(logger, device.Id);
            return InvalidCredentials();
        }

        person!.PinFailedCount = 0;
        person.PinLockedUntil = null;
        device.FailedSignins = 0;
        device.NextAttemptAt = null;
        var (session, tokens) = NewSession(device.BusinessId, person.Id, device.Id, now, offlineSignedInAt: null);
        db.StaffSessions.Add(session);
        db.Record(clock, person.Id, StaffChangeActions.SignedIn, person.Id, deviceId: device.Id);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new PosSignInResult(tokens, await StaffOfAsync(db, person, cancellationToken).ConfigureAwait(false)));
    }

    public static async Task<Results<Ok<PosTokens>, ProblemHttpResult>> RefreshAsync(
        PosRefreshRequest request, HttpContext context, HelloPosDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(clock);
        var deviceId = DeviceIdOf(context);
        if (PosSecrets.Parse(PosSecretKind.RefreshToken, request.RefreshToken) is not { } parsed
            || parsed.BusinessId != db.CurrentBusinessId)
        {
            return SessionEnded();
        }

        var session = await db.StaffSessions.SingleOrDefaultAsync(s => s.Id == parsed.Id && s.DeviceId == deviceId, cancellationToken)
            .ConfigureAwait(false);
        if (session is null || session.EndedAt is not null)
        {
            return SessionEnded();
        }

        var now = clock.GetUtcNow();
        if (PosSecrets.Matches(parsed, session.PreviousRefreshTokenHash))
        {
            // A replaced refresh token came back: it was copied. End the whole session.
            await EndAsync(db, session, SessionEndReasons.RefreshReuse, now, cancellationToken).ConfigureAwait(false);
            return SessionEnded();
        }

        if (!PosSecrets.Matches(parsed, session.RefreshTokenHash))
        {
            return SessionEnded();
        }

        if (await SessionChecks.EndReasonAsync(db, session, now, cancellationToken).ConfigureAwait(false) is { } reason)
        {
            await EndAsync(db, session, reason, now, cancellationToken).ConfigureAwait(false);
            return SessionEnded();
        }

        var (access, accessHash) = PosSecrets.Issue(PosSecretKind.AccessToken, session.BusinessId, session.Id);
        var (refresh, refreshHash) = PosSecrets.Issue(PosSecretKind.RefreshToken, session.BusinessId, session.Id);
        var accessExpires = Earlier(now + StaffSession.AccessLifetime, session.ExpiresAt);

        // Only one refresh with this token can win, even when two arrive together.
        var current = session.RefreshTokenHash;
        var won = await db.StaffSessions
            .Where(s => s.Id == session.Id && s.RefreshTokenHash == current && s.EndedAt == null)
            .ExecuteUpdateAsync(u => u
                .SetProperty(s => s.AccessTokenHash, accessHash)
                .SetProperty(s => s.AccessExpiresAt, accessExpires)
                .SetProperty(s => s.PreviousRefreshTokenHash, current)
                .SetProperty(s => s.RefreshTokenHash, refreshHash)
                .SetProperty(s => s.LastActiveAt, now), cancellationToken)
            .ConfigureAwait(false);
        return won == 1
            ? TypedResults.Ok(new PosTokens(access, accessExpires, refresh, session.ExpiresAt))
            : SessionEnded();
    }

    public static async Task<NoContent> SignOutAsync(HttpContext context, HelloPosDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(clock);
        var sessionId = Guid.Parse(context.User.FindFirst(PosAuthentication.SessionIdClaim)!.Value);
        var session = await db.StaffSessions.SingleAsync(s => s.Id == sessionId, cancellationToken).ConfigureAwait(false);
        db.Record(clock, session.StaffMemberId, StaffChangeActions.SignedOut, session.StaffMemberId, deviceId: session.DeviceId);
        await EndAsync(db, session, SessionEndReasons.SignOut, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    public static async Task<Results<Ok<PosStaff>, UnauthorizedHttpResult>> MeAsync(
        HttpContext context, HelloPosDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(db);
        var staffId = Guid.Parse(context.User.FindFirst(PosAuthentication.StaffIdClaim)!.Value);
        var person = await db.StaffMembers.AsNoTracking().SingleOrDefaultAsync(s => s.Id == staffId && s.Active, cancellationToken)
            .ConfigureAwait(false);
        return person is null
            ? TypedResults.Unauthorized()
            : TypedResults.Ok(await StaffOfAsync(db, person, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>A new session and its first tokens; the access token never outlives the session.</summary>
    internal static (StaffSession Session, PosTokens Tokens) NewSession(
        Guid businessId, Guid staffId, Guid deviceId, DateTimeOffset now, DateTimeOffset? offlineSignedInAt, DateTimeOffset? expiresAt = null)
    {
        var id = Guid.CreateVersion7();
        var (access, accessHash) = PosSecrets.Issue(PosSecretKind.AccessToken, businessId, id);
        var (refresh, refreshHash) = PosSecrets.Issue(PosSecretKind.RefreshToken, businessId, id);
        var expires = expiresAt ?? now + StaffSession.MaximumLifetime;
        var session = new StaffSession
        {
            Id = id,
            StaffMemberId = staffId,
            DeviceId = deviceId,
            AccessTokenHash = accessHash,
            AccessExpiresAt = Earlier(now + StaffSession.AccessLifetime, expires),
            RefreshTokenHash = refreshHash,
            StartedAt = now,
            ExpiresAt = expires,
            LastActiveAt = now,
            OfflineSignedInAt = offlineSignedInAt,
        };
        return (session, new PosTokens(access, session.AccessExpiresAt, refresh, expires));
    }

    internal static async Task<PosStaff> StaffOfAsync(HelloPosDbContext db, StaffMember person, CancellationToken cancellationToken)
    {
        var business = await db.Businesses.AsNoTracking().SingleAsync(cancellationToken).ConfigureAwait(false);
        return new PosStaff(person.Id, person.DisplayName, StaffRoleNames.ToWire(person.Role), business.Id, business.Name);
    }

    internal static Guid DeviceIdOf(HttpContext context) =>
        Guid.Parse(context.User.FindFirst(PosAuthentication.DeviceIdClaim)!.Value);

    internal static ProblemHttpResult InvalidCredentials() => TypedResults.Problem(
        statusCode: StatusCodes.Status401Unauthorized, title: "Username or PIN is incorrect.");

    /// <summary>
    /// Counts a failure: on the device (back-off after <see cref="DeviceBackOffAfter"/>) and, for a real
    /// active person not already locked, on that person (lock after <see cref="LockAfterFailures"/>).
    /// </summary>
    internal static void RecordFailure(HelloPosDbContext db, Devices.Device device, StaffMember? person, bool locked, DateTimeOffset now, TimeProvider clock)
    {
        device.FailedSignins++;
        if (device.FailedSignins >= DeviceBackOffAfter)
        {
            var wait = TimeSpan.FromSeconds(Math.Pow(2, device.FailedSignins - DeviceBackOffAfter));
            device.NextAttemptAt = now + (wait < MaximumBackOff ? wait : MaximumBackOff);
        }

        if (!locked)
        {
            RecordPersonFailure(db, person, now, clock, device.Id);
        }
    }

    /// <summary>Counts a failed PIN of an active person who is not locked; locks after <see cref="LockAfterFailures"/>.</summary>
    internal static void RecordPersonFailure(HelloPosDbContext db, StaffMember? person, DateTimeOffset now, TimeProvider clock, Guid deviceId)
    {
        if (person is not { Active: true } || person.PinLockedUntil is { } until && until > now)
        {
            return;
        }

        person.PinFailedCount++;
        db.Record(clock, person.Id, StaffChangeActions.SignInFailed, person.Id, deviceId: deviceId);
        if (person.PinFailedCount >= LockAfterFailures)
        {
            person.PinFailedCount = 0;
            person.PinLockedUntil = now + LockDuration;
            db.Record(clock, person.Id, StaffChangeActions.LockedOut, person.Id, deviceId: deviceId);
        }
    }

    internal static async Task EndAsync(HelloPosDbContext db, StaffSession session, string reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        session.EndedAt = now;
        session.EndReason = reason;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static DateTimeOffset Earlier(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;

    internal static ProblemHttpResult SessionEnded() => TypedResults.Problem(
        statusCode: StatusCodes.Status401Unauthorized, title: "The session has ended. Sign in again.");

    [LoggerMessage(Level = LogLevel.Information, Message = "POS sign-in failed on device {DeviceId}")]
    private static partial void LogFailed(ILogger logger, Guid deviceId);
}
