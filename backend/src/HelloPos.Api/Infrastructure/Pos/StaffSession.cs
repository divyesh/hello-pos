using HelloPos.Api.Features.Devices;
using HelloPos.Api.Features.Staff;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HelloPos.Api.Infrastructure.Pos;

/// <summary>
/// One person signed in on one device for one business (feature 007, FR-008, research R2). Opaque tokens:
/// only their SHA-256 hashes are stored. Ended, never deleted.
/// </summary>
public sealed class StaffSession : IBusinessOwned
{
    public static readonly TimeSpan AccessLifetime = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromHours(12);

    public Guid BusinessId { get; set; }

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid StaffMemberId { get; init; }

    public Guid DeviceId { get; init; }

    public required byte[] AccessTokenHash { get; set; }

    public DateTimeOffset AccessExpiresAt { get; set; }

    public required byte[] RefreshTokenHash { get; set; }

    /// <summary>The refresh token just rotated away: presenting it again means it was copied.</summary>
    public byte[]? PreviousRefreshTokenHash { get; set; }

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }

    public DateTimeOffset LastActiveAt { get; set; }

    /// <summary>Set when the session began as an offline sign-in on the device.</summary>
    public DateTimeOffset? OfflineSignedInAt { get; init; }

    public DateTimeOffset? EndedAt { get; set; }

    public string? EndReason { get; set; }
}

public static class SessionEndReasons
{
    public const string SignOut = "sign_out";
    public const string RefreshReuse = "refresh_reuse";
    public const string PinChanged = "pin_changed";
    public const string Deactivated = "deactivated";
    public const string DeviceRevoked = "device_revoked";
    public const string Expired = "expired";
}

public sealed class StaffSessionConfiguration : IEntityTypeConfiguration<StaffSession>
{
    public void Configure(EntityTypeBuilder<StaffSession> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("staff_sessions", t => t.HasCheckConstraint("ck_staff_sessions_end_reason",
            "end_reason IS NULL OR end_reason IN ('sign_out', 'refresh_reuse', 'pin_changed', 'deactivated', 'device_revoked', 'expired')"));
        builder.ConfigureBusinessOwned();
        builder.Property(s => s.EndReason).HasMaxLength(20);
        builder.HasIndex(s => s.AccessTokenHash).IsUnique();
        builder.HasIndex(s => s.RefreshTokenHash).IsUnique();
        builder.HasIndex(s => new { s.BusinessId, s.StaffMemberId });
        builder.HasIndex(s => new { s.BusinessId, s.DeviceId });
        builder.HasOne<StaffMember>().WithMany()
            .HasForeignKey(s => new { s.BusinessId, s.StaffMemberId })
            .HasPrincipalKey(m => new { m.BusinessId, m.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Device>().WithMany()
            .HasForeignKey(s => new { s.BusinessId, s.DeviceId })
            .HasPrincipalKey(d => new { d.BusinessId, d.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
