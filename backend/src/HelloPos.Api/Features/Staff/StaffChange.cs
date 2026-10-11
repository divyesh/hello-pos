using HelloPos.Api.Features.Devices;
using HelloPos.Api.Features.Invitations;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HelloPos.Api.Features.Staff;

/// <summary>
/// One entry of the staff audit trail (feature 006, FR-013, research R8). Append-only: the app login
/// may insert and read, never update or delete. Never holds a PIN or an invitation token.
/// </summary>
public sealed class StaffChange : IBusinessOwned
{
    public Guid BusinessId { get; set; }

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid? StaffMemberId { get; init; }

    public Guid? InvitationId { get; init; }

    /// <summary>The POS device involved (feature 007), if any.</summary>
    public Guid? DeviceId { get; init; }

    /// <summary>Who did it; for an accepted invitation, the new staff member themselves.</summary>
    public Guid ActorStaffId { get; init; }

    public required string Action { get; init; }

    public string? OldValue { get; init; }

    public string? NewValue { get; init; }

    public DateTimeOffset At { get; init; }
}

public static class StaffChangeActions
{
    public const string Added = "added";
    public const string Invited = "invited";
    public const string InvitationAccepted = "invitation_accepted";
    public const string InvitationRevoked = "invitation_revoked";
    public const string InvitationResent = "invitation_resent";
    public const string RoleChanged = "role_changed";
    public const string Renamed = "renamed";
    public const string UsernameChanged = "username_changed";
    public const string Deactivated = "deactivated";
    public const string Reactivated = "reactivated";
    public const string PinSet = "pin_set";
    public const string PinChanged = "pin_changed";
    public const string DeviceRegistered = "device_registered";
    public const string DeviceRenamed = "device_renamed";
    public const string DeviceRevoked = "device_revoked";
    public const string SignedIn = "signed_in";
    public const string SignedInOffline = "signed_in_offline";
    public const string SignInFailed = "sign_in_failed";
    public const string LockedOut = "locked_out";
    public const string SignedOut = "signed_out";
}

public sealed class StaffChangeConfiguration : IEntityTypeConfiguration<StaffChange>
{
    public void Configure(EntityTypeBuilder<StaffChange> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("staff_changes");
        builder.ConfigureBusinessOwned();
        builder.Property(c => c.Action).HasMaxLength(30);
        builder.Property(c => c.OldValue).HasMaxLength(100);
        builder.Property(c => c.NewValue).HasMaxLength(100);
        builder.Property(c => c.At).HasDefaultValueSql("now()");
        builder.HasOne<StaffMember>().WithMany()
            .HasForeignKey(c => new { c.BusinessId, c.StaffMemberId })
            .HasPrincipalKey(s => new { s.BusinessId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StaffMember>().WithMany()
            .HasForeignKey(c => new { c.BusinessId, c.ActorStaffId })
            .HasPrincipalKey(s => new { s.BusinessId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Device>().WithMany()
            .HasForeignKey(c => new { c.BusinessId, c.DeviceId })
            .HasPrincipalKey(d => new { d.BusinessId, d.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StaffInvitation>().WithMany()
            .HasForeignKey(c => new { c.BusinessId, c.InvitationId })
            .HasPrincipalKey(i => new { i.BusinessId, i.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
