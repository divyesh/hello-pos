using HelloPos.Api.Features.Owners;
using HelloPos.Api.Features.Staff;
using HelloPos.Api.Infrastructure;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Infrastructure.Pos;
using HelloPos.Kernel.Staff;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace HelloPos.Api.Features.Pos;

public sealed record PosOfflineSession(Guid? StaffId, DateTimeOffset? SignedInAt, DateTimeOffset? PinChangedAt);

public sealed record PosAttempt(string? Username, bool Success, DateTimeOffset At);

public sealed record PosAttemptReport(IReadOnlyList<PosAttempt>? Attempts);

/// <summary>
/// What the device sends when it is back online after signing people in offline (research R4):
/// <c>POST /pos/sessions/offline</c> turns an offline sign-in into a session, and <c>POST /pos/attempts</c>
/// merges offline PIN attempts into the people's lockout counters.
/// </summary>
public sealed class OfflineSignIns : IEndpointMapper
{
    public const int MaxAttemptsPerReport = 100;

    public void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/pos/sessions/offline", SessionFromOfflineAsync).WithName("posSessionFromOffline").WithTags(PairDevice.Tag)
            .RequireAuthorization(PosAuthentication.DevicePolicy).RequireRateLimiting(PosAuthentication.RateLimit);
        app.MapPost("/pos/attempts", ReportAttemptsAsync).WithName("posReportAttempts").WithTags(PairDevice.Tag)
            .RequireAuthorization(PosAuthentication.DevicePolicy).RequireRateLimiting(PosAuthentication.RateLimit);
    }

    public static async Task<Results<Ok<PosTokens>, ValidationProblem, ProblemHttpResult>> SessionFromOfflineAsync(
        PosOfflineSession request, HttpContext context, HelloPosDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(clock);
        var now = clock.GetUtcNow();
        if (request.StaffId is not { } staffId || request.SignedInAt is not { } signedInAt)
        {
            return OwnerEndpoints.Invalid(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["staffId"] = ["Missing staff or time."] });
        }

        var expires = signedInAt + StaffSession.MaximumLifetime;
        if (signedInAt > now + ClockSkew || expires <= now)
        {
            return PosSessionEndpoints.SessionEnded();
        }

        var person = await db.StaffMembers.SingleOrDefaultAsync(s => s.Id == staffId, cancellationToken).ConfigureAwait(false);
        if (person is not { Active: true, PinHash: not null } || !SameInstant(person.PinChangedAt, request.PinChangedAt))
        {
            return PosSessionEndpoints.SessionEnded();
        }

        var deviceId = PosSessionEndpoints.DeviceIdOf(context);
        var (session, tokens) = PosSessionEndpoints.NewSession(
            db.CurrentBusinessId!.Value, person.Id, deviceId, now, offlineSignedInAt: signedInAt, expiresAt: expires);
        db.StaffSessions.Add(session);
        db.Record(clock, person.Id, StaffChangeActions.SignedInOffline, person.Id, deviceId: deviceId);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(tokens);
    }

    public static async Task<Results<NoContent, ValidationProblem>> ReportAttemptsAsync(
        PosAttemptReport request, HttpContext context, HelloPosDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(clock);
        if (request.Attempts is not { } attempts || attempts.Count > MaxAttemptsPerReport)
        {
            return OwnerEndpoints.Invalid(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["attempts"] = [$"Send 0 to {MaxAttemptsPerReport} attempts."],
            });
        }

        var now = clock.GetUtcNow();
        var deviceId = PosSessionEndpoints.DeviceIdOf(context);
        var usernames = attempts.Where(a => !string.IsNullOrWhiteSpace(a.Username))
            .Select(a => StaffRules.NormalizePosUsername(a.Username!)).Distinct(StringComparer.Ordinal).ToList();
        var people = await db.StaffMembers.Where(s => s.PosUsername != null && usernames.Contains(s.PosUsername))
            .ToDictionaryAsync(s => s.PosUsername!, StringComparer.Ordinal, cancellationToken).ConfigureAwait(false);

        foreach (var attempt in attempts.Where(a => !string.IsNullOrWhiteSpace(a.Username)).OrderBy(a => a.At))
        {
            if (!people.TryGetValue(StaffRules.NormalizePosUsername(attempt.Username!), out var person))
            {
                continue;
            }

            if (attempt.Success)
            {
                // A correct PIN offline resets the count unless a later lock is in force.
                if (person.PinLockedUntil is not { } until || until <= now)
                {
                    person.PinFailedCount = 0;
                }
            }
            else
            {
                PosSessionEndpoints.RecordPersonFailure(db, person, now, clock, deviceId);
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    /// <summary>Equal to the millisecond (JSON and the database keep different precisions).</summary>
    private static bool SameInstant(DateTimeOffset? a, DateTimeOffset? b) =>
        a is null ? b is null : b is not null && Math.Abs((a.Value - b.Value).TotalMilliseconds) < 1;
}
