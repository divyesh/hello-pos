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

public sealed record PosChangePin(string? CurrentPin, string? NewPin);

/// <summary>
/// <c>PUT /pos/me/pin</c> (feature 007, FR-010, research R7): a person changes their own PIN by giving
/// the current one. A wrong current PIN counts toward lockout like a failed sign-in. The change ends the
/// person's other sessions; this one is replaced by a new session whose tokens are returned.
/// </summary>
public sealed class ChangeOwnPin : IEndpointMapper
{
    public void Map(IEndpointRouteBuilder app) =>
        app.MapPut("/pos/me/pin", HandleAsync)
            .WithName("posChangeOwnPin")
            .WithTags(PairDevice.Tag)
            .RequireAuthorization(PosAuthentication.SessionPolicy)
            .RequireRateLimiting(PosAuthentication.RateLimit);

    public static async Task<Results<Ok<PosTokens>, ValidationProblem, ProblemHttpResult>> HandleAsync(
        PosChangePin request, HttpContext context, HelloPosDbContext db, IPasswordHasher<StaffMember> hasher,
        StaffCredentials credentials, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(hasher);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(clock);
        var now = clock.GetUtcNow();
        var sessionId = Guid.Parse(context.User.FindFirst(PosAuthentication.SessionIdClaim)!.Value);
        var session = await db.StaffSessions.SingleAsync(s => s.Id == sessionId, cancellationToken).ConfigureAwait(false);
        var person = await db.StaffMembers.SingleAsync(s => s.Id == session.StaffMemberId, cancellationToken).ConfigureAwait(false);

        var locked = person.PinLockedUntil is { } until && until > now;
        if (locked || person.PinHash is null || string.IsNullOrEmpty(request.CurrentPin)
            || hasher.VerifyHashedPassword(person, person.PinHash, request.CurrentPin) == PasswordVerificationResult.Failed)
        {
            if (!string.IsNullOrEmpty(request.CurrentPin))
            {
                PosSessionEndpoints.RecordPersonFailure(db, person, now, clock, session.DeviceId);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            return OwnerEndpoints.Invalid(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["currentPin"] = [string.IsNullOrEmpty(request.CurrentPin) ? "Enter your current PIN." : "The current PIN is not correct."],
            });
        }

        if (PinRules.Validate(request.NewPin) is { } invalid)
        {
            return OwnerEndpoints.Invalid(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["newPin"] = [invalid] });
        }

        credentials.SetPin(person, person.PosUsername!, request.NewPin!);
        person.PinChangedAt = now; // the same instant the replacement session starts, so it is not "before the change"
        person.PinFailedCount = 0;
        person.Version++;
        person.UpdatedAt = now;

        // Every session of this person ends, this one included; this device continues on a fresh session.
        session.EndedAt = now;
        session.EndReason = SessionEndReasons.PinChanged;
        await db.StaffSessions
            .Where(s => s.StaffMemberId == person.Id && s.EndedAt == null && s.Id != session.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.EndedAt, now).SetProperty(s => s.EndReason, SessionEndReasons.PinChanged), cancellationToken)
            .ConfigureAwait(false);
        var (replacement, tokens) = PosSessionEndpoints.NewSession(session.BusinessId, person.Id, session.DeviceId, now, offlineSignedInAt: null);
        db.StaffSessions.Add(replacement);
        db.Record(clock, person.Id, StaffChangeActions.PinChanged, person.Id, deviceId: session.DeviceId);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(tokens);
    }
}
