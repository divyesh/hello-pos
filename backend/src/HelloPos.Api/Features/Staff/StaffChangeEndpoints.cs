using HelloPos.Api.Features.Owners;
using HelloPos.Api.Infrastructure;
using HelloPos.Api.Infrastructure.Identity;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Infrastructure.Tenancy;
using HelloPos.Kernel.Staff;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HelloPos.Api.Features.Staff;

public sealed record StaffUpdateRequest(string? DisplayName, string? Role, int Version);

public sealed record StaffVersionRequest(int Version);

public sealed record StaffPinRequest(string? PosUsername, string? Pin, int Version);

/// <summary>
/// Changing people (feature 006, US2): rename and change role, deactivate and reactivate (never
/// delete), set or reset the POS username and PIN. Each change sends the version it read (a stale one is
/// <c>409 staff-changed</c>), is checked against the kernel rule table, keeps an active owner
/// (<see cref="LastOwnerGuard"/>), and is audited in the same transaction.
/// </summary>
public sealed class StaffChangeEndpoints : IEndpointMapper
{
    public void Map(IEndpointRouteBuilder app)
    {
        var staff = app.MapGroup("/staff/{staffId:guid}")
            .WithTags(StaffEndpoints.Tag)
            .RequireRole(StaffRole.Owner, StaffRole.Manager)
            .RequireSameBusiness()
            .RequireClientHeader();

        staff.MapPut("", UpdateAsync).WithName("updateStaff");
        staff.MapPost("/deactivate", DeactivateAsync).WithName("deactivateStaff");
        staff.MapPost("/reactivate", ReactivateAsync).WithName("reactivateStaff");
        staff.MapPut("/pin", SetPinAsync).WithName("setStaffPin");
    }

    public static async Task<Results<Ok<StaffMemberResponse>, ValidationProblem, ProblemHttpResult>> UpdateAsync(
        Guid staffId, StaffUpdateRequest request, ICurrentStaff current, HelloPosDbContext db, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(db);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var role = StaffRoleNames.Parse(request.Role);
        if (role is null)
        {
            errors["role"] = ["Choose owner, manager or cashier."];
        }

        if (StaffRules.ValidateDisplayName(request.DisplayName) is { } nameError)
        {
            errors["displayName"] = [nameError];
        }

        if (errors.Count > 0)
        {
            return OwnerEndpoints.Invalid(errors);
        }

        return await ChangeAsync(staffId, request.Version, current, db, clock, async (actor, person) =>
        {
            var name = request.DisplayName!.Trim();
            var newRole = role!.Value;
            if (name != person.DisplayName && StaffProblems.Check(actor.Role, StaffAction.Rename, person.Role) is { } noRename)
            {
                return noRename;
            }

            if (newRole != person.Role)
            {
                if (StaffProblems.Check(actor.Role, StaffAction.ChangeRole, person.Role, newRole) is { } noRoleChange)
                {
                    return noRoleChange;
                }

                if (StaffRules.RequiresAccount(newRole) && person.AccountId is null)
                {
                    return OwnerEndpoints.Invalid(new Dictionary<string, string[]>(StringComparer.Ordinal)
                    {
                        ["role"] = ["Owners need a back-office account. Invite this person by email as an owner instead."],
                    });
                }

                if (person.Role == StaffRole.Owner && person.Active
                    && await LastOwnerRefusedAsync(db, person, cancellationToken).ConfigureAwait(false) is { } lastOwner)
                {
                    return lastOwner;
                }
            }

            if (name != person.DisplayName)
            {
                db.Record(clock, actor.Id, StaffChangeActions.Renamed, person.Id, oldValue: person.DisplayName, newValue: name);
                person.DisplayName = name;
            }

            if (newRole != person.Role)
            {
                db.Record(clock, actor.Id, StaffChangeActions.RoleChanged, person.Id,
                    oldValue: StaffRoleNames.ToWire(person.Role), newValue: StaffRoleNames.ToWire(newRole));
                person.Role = newRole;
            }

            return null;
        }, cancellationToken).ConfigureAwait(false);
    }

    public static Task<Results<Ok<StaffMemberResponse>, ValidationProblem, ProblemHttpResult>> DeactivateAsync(
        Guid staffId, StaffVersionRequest request, ICurrentStaff current, HelloPosDbContext db, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ChangeAsync(staffId, request.Version, current, db, clock, async (actor, person) =>
        {
            if (StaffProblems.Check(actor.Role, StaffAction.Deactivate, person.Role) is { } refused)
            {
                return refused;
            }

            if (!person.Active)
            {
                return null;
            }

            if (person.Role == StaffRole.Owner
                && await LastOwnerRefusedAsync(db, person, cancellationToken).ConfigureAwait(false) is { } lastOwner)
            {
                return lastOwner;
            }

            person.Active = false;
            db.Record(clock, actor.Id, StaffChangeActions.Deactivated, person.Id);
            return null;
        }, cancellationToken);
    }

    public static Task<Results<Ok<StaffMemberResponse>, ValidationProblem, ProblemHttpResult>> ReactivateAsync(
        Guid staffId, StaffVersionRequest request, ICurrentStaff current, HelloPosDbContext db, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ChangeAsync(staffId, request.Version, current, db, clock, (actor, person) =>
        {
            if (StaffProblems.Check(actor.Role, StaffAction.Reactivate, person.Role) is { } refused)
            {
                return Task.FromResult<IResult?>(refused);
            }

            if (!person.Active)
            {
                person.Active = true;
                db.Record(clock, actor.Id, StaffChangeActions.Reactivated, person.Id);
            }

            return Task.FromResult<IResult?>(null);
        }, cancellationToken);
    }

    public static async Task<Results<Ok<StaffMemberResponse>, ValidationProblem, ProblemHttpResult>> SetPinAsync(
        Guid staffId, StaffPinRequest request, ICurrentStaff current, HelloPosDbContext db, StaffCredentials credentials,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(credentials);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (StaffRules.ValidatePosUsername(request.PosUsername) is { } usernameError)
        {
            errors["posUsername"] = [usernameError];
        }

        if (PinRules.Validate(request.Pin) is { } pinError)
        {
            errors["pin"] = [pinError];
        }

        if (errors.Count > 0)
        {
            return OwnerEndpoints.Invalid(errors);
        }

        var username = StaffRules.NormalizePosUsername(request.PosUsername!);
        try
        {
            return await ChangeAsync(staffId, request.Version, current, db, clock, async (actor, person) =>
            {
                if (StaffProblems.Check(actor.Role, StaffAction.SetPin, person.Role) is { } refused)
                {
                    return refused;
                }

                if (username != person.PosUsername
                    && await db.StaffMembers.AnyAsync(s => s.PosUsername == username && s.Id != person.Id, cancellationToken).ConfigureAwait(false))
                {
                    return UsernameTaken();
                }

                if (username != person.PosUsername)
                {
                    db.Record(clock, actor.Id, StaffChangeActions.UsernameChanged, person.Id, oldValue: person.PosUsername, newValue: username);
                }

                credentials.SetPin(person, username, request.Pin!);
                db.Record(clock, actor.Id, StaffChangeActions.PinSet, person.Id);
                return null;
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return UsernameTaken();
        }
    }

    /// <summary>
    /// Loads the person (404 for anyone outside the current business), checks the version, applies
    /// <paramref name="change"/> (which returns a refusal or <c>null</c>), and saves with the version
    /// as a concurrency token, all in one transaction (the last-owner lock lives until it ends).
    /// </summary>
    private static async Task<Results<Ok<StaffMemberResponse>, ValidationProblem, ProblemHttpResult>> ChangeAsync(
        Guid staffId, int version, ICurrentStaff current, HelloPosDbContext db, TimeProvider clock,
        Func<StaffMember, StaffMember, Task<IResult?>> change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(clock);
        var actor = (await current.GetAsync(cancellationToken).ConfigureAwait(false))!;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var person = await db.StaffMembers.SingleOrDefaultAsync(s => s.Id == staffId, cancellationToken).ConfigureAwait(false);
        if (person is null)
        {
            return StaffProblems.NotFound();
        }

        if (person.Version != version)
        {
            return StaffProblems.StaffChanged();
        }

        switch (await change(actor, person).ConfigureAwait(false))
        {
            case ProblemHttpResult problem:
                return problem;
            case ValidationProblem invalid:
                return invalid;
        }

        if (db.ChangeTracker.HasChanges())
        {
            db.Entry(person).Property(p => p.Version).OriginalValue = version;
            person.Version = version + 1;
            person.UpdatedAt = clock.GetUtcNow();
            try
            {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                return StaffProblems.StaffChanged();
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(StaffMemberResponse.From(person));
    }

    private static async Task<ProblemHttpResult?> LastOwnerRefusedAsync(HelloPosDbContext db, StaffMember owner, CancellationToken cancellationToken)
    {
        await db.LockBusinessAsync(cancellationToken).ConfigureAwait(false);
        return await db.HasAnotherActiveOwnerAsync(owner.Id, cancellationToken).ConfigureAwait(false)
            ? null
            : StaffProblems.LastOwner();
    }

    private static ValidationProblem UsernameTaken() =>
        OwnerEndpoints.Invalid(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["posUsername"] = [StaffValidation.UsernameTaken],
        });
}
