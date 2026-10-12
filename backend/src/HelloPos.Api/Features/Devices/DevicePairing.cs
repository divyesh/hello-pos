using HelloPos.Api.Features.Staff;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HelloPos.Api.Features.Devices;

/// <summary>
/// A one-time code that connects a device to a business (feature 007, FR-002): valid 5 minutes, used once.
/// Only the code's SHA-256 is stored.
/// </summary>
public sealed class DevicePairing : IBusinessOwned
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    public Guid BusinessId { get; set; }

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required byte[] CodeHash { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }

    public Guid CreatedByStaffId { get; init; }

    public DateTimeOffset? UsedAt { get; set; }

    public Guid? DeviceId { get; set; }

    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class DevicePairingConfiguration : IEntityTypeConfiguration<DevicePairing>
{
    public void Configure(EntityTypeBuilder<DevicePairing> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("device_pairings");
        builder.ConfigureBusinessOwned();
        builder.Property(p => p.CreatedAt).HasDefaultValueSql("now()");
        builder.HasIndex(p => p.CodeHash).IsUnique();
        builder.HasOne<StaffMember>().WithMany()
            .HasForeignKey(p => new { p.BusinessId, p.CreatedByStaffId })
            .HasPrincipalKey(s => new { s.BusinessId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Device>().WithMany()
            .HasForeignKey(p => new { p.BusinessId, p.DeviceId })
            .HasPrincipalKey(d => new { d.BusinessId, d.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
