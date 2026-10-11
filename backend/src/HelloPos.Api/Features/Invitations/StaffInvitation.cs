using HelloPos.Api.Features.Staff;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Infrastructure.Tenancy;
using HelloPos.Kernel.Staff;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HelloPos.Api.Features.Invitations;

/// <summary>
/// An offer of a back-office role (owner or manager) in one business to one email address (feature
/// 006, FR-011, research R7). The emailed token is stored only as its SHA-256.
/// </summary>
public sealed class StaffInvitation : IBusinessOwned
{
    public const int EmailMaxLength = 256;

    public Guid BusinessId { get; set; }

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Email { get; set; }

    public required string NormalizedEmail { get; set; }

    public StaffRole Role { get; set; }

    public required string DisplayName { get; set; }

    public required byte[] TokenHash { get; set; }

    public string Status { get; set; } = InvitationStatus.Pending;

    public DateTimeOffset ExpiresAt { get; set; }

    public Guid InvitedByStaffId { get; init; }

    public Guid? AcceptedStaffId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public static class InvitationStatus
{
    public const string Pending = "pending";
    public const string Accepted = "accepted";
    public const string Revoked = "revoked";
}

public sealed class StaffInvitationConfiguration : IEntityTypeConfiguration<StaffInvitation>
{
    public void Configure(EntityTypeBuilder<StaffInvitation> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("staff_invitations", t =>
        {
            t.HasCheckConstraint("ck_staff_invitations_role", "role IN ('owner', 'manager')");
            t.HasCheckConstraint("ck_staff_invitations_status", "status IN ('pending', 'accepted', 'revoked')");
            t.HasCheckConstraint("ck_staff_invitations_display_name", $"char_length(display_name) BETWEEN 1 AND {StaffRules.DisplayNameMaxLength}");
        });
        builder.ConfigureBusinessOwned();
        builder.Property(i => i.Email).HasMaxLength(StaffInvitation.EmailMaxLength);
        builder.Property(i => i.NormalizedEmail).HasMaxLength(StaffInvitation.EmailMaxLength);
        builder.Property(i => i.Role)
            .HasConversion(r => StaffRoleNames.ToWire(r), v => StaffRoleNames.Parse(v) ?? StaffRole.Manager)
            .HasMaxLength(10);
        builder.Property(i => i.DisplayName).HasMaxLength(StaffRules.DisplayNameMaxLength);
        builder.Property(i => i.Status).HasMaxLength(10);
        builder.Property(i => i.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(i => i.UpdatedAt).HasDefaultValueSql("now()");
        builder.HasIndex(i => i.TokenHash).IsUnique();
        builder.HasIndex(i => new { i.BusinessId, i.NormalizedEmail }).IsUnique().HasFilter("status = 'pending'");
        builder.HasOne<StaffMember>().WithMany()
            .HasForeignKey(i => new { i.BusinessId, i.InvitedByStaffId })
            .HasPrincipalKey(s => new { s.BusinessId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StaffMember>().WithMany()
            .HasForeignKey(i => new { i.BusinessId, i.AcceptedStaffId })
            .HasPrincipalKey(s => new { s.BusinessId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
