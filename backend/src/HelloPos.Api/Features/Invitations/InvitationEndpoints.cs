using HelloPos.Api.Features.Owners;
using HelloPos.Api.Features.Staff;
using HelloPos.Api.Infrastructure;
using HelloPos.Api.Infrastructure.Identity;
using HelloPos.Api.Infrastructure.Identity.Email;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Infrastructure.Tenancy;
using HelloPos.Kernel.Staff;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HelloPos.Api.Features.Invitations;

/// <summary>Contract <c>Invitation</c>; <c>expired</c> is a pending invitation past its expiry.</summary>
public sealed record InvitationResponse(
    Guid Id, string Email, string Role, string DisplayName, string Status, DateTimeOffset ExpiresAt, DateTimeOffset CreatedAt);

public sealed record NewInvitationRequest(string? Email, string? Role, string? DisplayName);

public sealed record InvitationTokenRequest(string? Token);

public sealed record InvitationPreviewResponse(string BusinessName, string Role, string DisplayName);

/// <summary>
/// Invitations to back-office roles (feature 006, FR-011, FR-011a, research R7). Owners invite and list;
/// anyone with the link previews; the invited account accepts. Unknown, used, revoked, expired and
/// other-email cases all answer the same 404.
/// </summary>
public sealed class InvitationEndpoints : IEndpointMapper
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    public void Map(IEndpointRouteBuilder app)
    {
        var owners = app.MapGroup("/staff/invitations")
            .WithTags(StaffEndpoints.Tag)
            .RequireRole(StaffRole.Owner)
            .RequireSameBusiness();
        owners.MapGet("", ListAsync).WithName("listInvitations");
        owners.MapPost("", InviteAsync).WithName("inviteStaff").RequireClientHeader();

        app.MapPost("/invitations/preview", PreviewAsync).WithName("previewInvitation")
            .WithTags(StaffEndpoints.Tag).AllowAnonymous().RequireClientHeader().RequireRateLimiting(IdentitySetup.OwnerAuthRateLimit);
        app.MapPost("/invitations/accept", AcceptAsync).WithName("acceptInvitation")
            .WithTags(StaffEndpoints.Tag).RequireAuthorization().RequireClientHeader().RequireRateLimiting(IdentitySetup.OwnerAuthRateLimit);
    }

    public static async Task<Ok<List<InvitationResponse>>> ListAsync(
        HelloPosDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(clock);
        var now = clock.GetUtcNow();
        var invitations = await db.StaffInvitations.AsNoTracking()
            .OrderBy(i => i.Status == InvitationStatus.Pending ? 0 : 1)
            .ThenByDescending(i => i.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(invitations.Select(i => ToResponse(i, now)).ToList());
    }

    public static async Task<Results<Created<InvitationResponse>, ValidationProblem, ProblemHttpResult>> InviteAsync(
        NewInvitationRequest request, ICurrentStaff current, HelloPosDbContext db, UserManager<OwnerAccount> users,
        IEmailSender sender, OwnerEmails emails, TimeProvider clock, ILoggerFactory loggers, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(emails);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(loggers);
        var actor = (await current.GetAsync(cancellationToken).ConfigureAwait(false))!;

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        OwnerEndpoints.ValidateEmail(request.Email, "email", errors);
        var role = StaffRoleNames.Parse(request.Role);
        if (role is not (StaffRole.Owner or StaffRole.Manager))
        {
            errors["role"] = ["Choose owner or manager."];
        }

        if (StaffRules.ValidateDisplayName(request.DisplayName) is { } nameError)
        {
            errors["displayName"] = [nameError];
        }

        if (errors.Count > 0)
        {
            return OwnerEndpoints.Invalid(errors);
        }

        if (StaffProblems.Check(actor.Role, StaffAction.Invite, newRole: role) is { } refused)
        {
            return refused;
        }

        var email = request.Email!.Trim();
        var normalized = users.NormalizeEmail(email);
        if (await users.FindByEmailAsync(email).ConfigureAwait(false) is { } existing
            && await db.StaffMembers.AnyAsync(s => s.AccountId == existing.Id, cancellationToken).ConfigureAwait(false))
        {
            return StaffProblems.AlreadyMember();
        }

        if (await db.StaffInvitations.AnyAsync(i => i.NormalizedEmail == normalized && i.Status == InvitationStatus.Pending, cancellationToken)
                .ConfigureAwait(false))
        {
            return AlreadyInvited();
        }

        var now = clock.GetUtcNow();
        var (token, hash) = InvitationTokens.Create();
        var invitation = new StaffInvitation
        {
            Email = email,
            NormalizedEmail = normalized,
            Role = role!.Value,
            DisplayName = request.DisplayName!.Trim(),
            TokenHash = hash,
            ExpiresAt = now + Lifetime,
            InvitedByStaffId = actor.Id,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.StaffInvitations.Add(invitation);
        db.Record(clock, actor.Id, StaffChangeActions.Invited, invitationId: invitation.Id, newValue: StaffRoleNames.ToWire(invitation.Role));
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return AlreadyInvited(); // a concurrent invite to the same address won
        }

        var businessName = await db.Businesses.Select(b => b.Name).SingleAsync(cancellationToken).ConfigureAwait(false);
        await OwnerEndpoints.SendSafelyAsync(sender,
            emails.Invitation(email, invitation.DisplayName, businessName, StaffRoleNames.ToWire(invitation.Role), actor.DisplayName, token),
            loggers.CreateLogger<InvitationEndpoints>(), cancellationToken).ConfigureAwait(false);
        return TypedResults.Created($"/staff/invitations/{invitation.Id}", ToResponse(invitation, now));
    }

    public static async Task<Results<Ok<InvitationPreviewResponse>, ProblemHttpResult>> PreviewAsync(
        InvitationTokenRequest request, HelloPosDbContext db, DbContextOptions<HelloPosDbContext> options, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await InvitationTokens.FindPendingAsync(request.Token, db, options, clock, cancellationToken).ConfigureAwait(false)
            is not var (scoped, invitation))
        {
            return StaffProblems.NotFound();
        }

        await using (scoped.ConfigureAwait(false))
        {
            var businessName = await scoped.Businesses.Select(b => b.Name).SingleAsync(cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(new InvitationPreviewResponse(businessName, StaffRoleNames.ToWire(invitation.Role), invitation.DisplayName));
        }
    }

    public static async Task<Results<NoContent, ProblemHttpResult, UnauthorizedHttpResult>> AcceptAsync(
        InvitationTokenRequest request, ICurrentOwner owner, UserManager<OwnerAccount> users, HelloPosDbContext db,
        DbContextOptions<HelloPosDbContext> options, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(clock);
        var account = owner.OwnerId is { } id ? await users.FindByIdAsync(id.ToString()).ConfigureAwait(false) : null;
        if (account is null)
        {
            return TypedResults.Unauthorized();
        }

        if (await InvitationTokens.FindPendingAsync(request.Token, db, options, clock, cancellationToken).ConfigureAwait(false)
            is not var (scoped, invitation))
        {
            return StaffProblems.NotFound();
        }

        await using (scoped.ConfigureAwait(false))
        {
            // Only the invited address may accept; anyone else sees the same answer as a wrong link.
            if (!account.EmailConfirmed || !string.Equals(account.NormalizedEmail, invitation.NormalizedEmail, StringComparison.Ordinal))
            {
                return StaffProblems.NotFound();
            }

            if (await scoped.StaffMembers.AnyAsync(s => s.AccountId == account.Id, cancellationToken).ConfigureAwait(false))
            {
                return StaffProblems.AlreadyMember();
            }

            var now = clock.GetUtcNow();
            var member = new StaffMember
            {
                DisplayName = invitation.DisplayName,
                Role = invitation.Role,
                AccountId = account.Id,
                CreatedAt = now,
                UpdatedAt = now,
            };
            scoped.StaffMembers.Add(member);
            invitation.Status = InvitationStatus.Accepted;
            invitation.AcceptedStaffId = member.Id;
            invitation.UpdatedAt = now;
            scoped.Record(clock, member.Id, StaffChangeActions.InvitationAccepted,
                staffMemberId: member.Id, invitationId: invitation.Id, newValue: StaffRoleNames.ToWire(member.Role));
            await scoped.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return TypedResults.NoContent();
        }
    }

    private static InvitationResponse ToResponse(StaffInvitation invitation, DateTimeOffset now) => new(
        invitation.Id,
        invitation.Email,
        StaffRoleNames.ToWire(invitation.Role),
        invitation.DisplayName,
        invitation.Status == InvitationStatus.Pending && invitation.ExpiresAt <= now ? "expired" : invitation.Status,
        invitation.ExpiresAt,
        invitation.CreatedAt);

    private static ProblemHttpResult AlreadyInvited() => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "This address already has a pending invitation. Resend or revoke it instead.");
}
