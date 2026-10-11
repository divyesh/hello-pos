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

/// <summary>
/// <c>GET /staff</c>, <c>GET /staff/{id}</c>, <c>POST /staff</c> (feature 006, US1). Every staff
/// endpoint acts on the open business, needs an owner or manager (<see cref="ICurrentStaff"/>, read per
/// request) and honours the stale-tab header; the kernel rule table decides each action.
/// </summary>
public sealed class StaffEndpoints : IEndpointMapper
{
    public const string Tag = "Staff";

    public void Map(IEndpointRouteBuilder app)
    {
        var staff = app.MapGroup("/staff")
            .WithTags(Tag)
            .RequireRole(StaffRole.Owner, StaffRole.Manager)
            .RequireSameBusiness();

        staff.MapGet("", ListAsync).WithName("listStaff");
        staff.MapGet("/{staffId:guid}", GetAsync).WithName("getStaff");
        staff.MapPost("", AddAsync).WithName("addStaff").RequireClientHeader();
    }

    public static async Task<Results<Ok<List<StaffMemberResponse>>, ValidationProblem>> ListAsync(
        HelloPosDbContext db, string? status, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        IQueryable<StaffMember> query = db.StaffMembers.AsNoTracking();
        switch (status ?? "active")
        {
            case "active":
                query = query.Where(s => s.Active);
                break;
            case "inactive":
                query = query.Where(s => !s.Active);
                break;
            case "all":
                break;
            default:
                return OwnerEndpoints.Invalid(new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["status"] = ["Use active, inactive or all."],
                });
        }

        var people = await query.OrderBy(s => s.DisplayName).ThenBy(s => s.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(people.Select(StaffMemberResponse.From).ToList());
    }

    public static async Task<Results<Ok<StaffMemberResponse>, ProblemHttpResult>> GetAsync(
        Guid staffId, HelloPosDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var person = await db.StaffMembers.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == staffId, cancellationToken).ConfigureAwait(false);
        return person is null ? StaffProblems.NotFound() : TypedResults.Ok(StaffMemberResponse.From(person));
    }

    public static async Task<Results<Created<StaffMemberResponse>, ValidationProblem, ProblemHttpResult>> AddAsync(
        NewStaffMemberRequest request, ICurrentStaff current, HelloPosDbContext db, StaffCredentials credentials,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(clock);
        var actor = (await current.GetAsync(cancellationToken).ConfigureAwait(false))!;

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var role = StaffRoleNames.Parse(request.Role);
        if (role is null)
        {
            errors["role"] = ["Choose manager or cashier."];
        }

        StaffValidation.ValidatePosFields(request.DisplayName, request.PosUsername, request.Pin, errors);
        if (errors.Count > 0)
        {
            return OwnerEndpoints.Invalid(errors);
        }

        if (StaffProblems.Check(actor.Role, StaffAction.AddPosStaff, newRole: role) is { } refused)
        {
            return refused;
        }

        var username = StaffRules.NormalizePosUsername(request.PosUsername!);
        if (await db.StaffMembers.AnyAsync(s => s.PosUsername == username, cancellationToken).ConfigureAwait(false))
        {
            return UsernameTaken();
        }

        var now = clock.GetUtcNow();
        var person = new StaffMember
        {
            DisplayName = request.DisplayName!.Trim(),
            Role = role!.Value,
            CreatedAt = now,
            UpdatedAt = now,
        };
        credentials.SetPin(person, username, request.Pin!);
        db.StaffMembers.Add(person);
        db.Record(clock, actor.Id, StaffChangeActions.Added, staffMemberId: person.Id, newValue: StaffRoleNames.ToWire(person.Role));
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return UsernameTaken(); // lost a race with a concurrent add of the same username
        }

        return TypedResults.Created($"/staff/{person.Id}", StaffMemberResponse.From(person));
    }

    private static ValidationProblem UsernameTaken() =>
        OwnerEndpoints.Invalid(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["posUsername"] = [StaffValidation.UsernameTaken],
        });
}
