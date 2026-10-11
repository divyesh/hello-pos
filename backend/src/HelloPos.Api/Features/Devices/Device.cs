using HelloPos.Api.Features.Staff;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HelloPos.Api.Features.Devices;

/// <summary>
/// A POS device registered to one business (feature 007, data-model.md). It authenticates with a device
/// credential whose secret is stored only as a SHA-256 hash; its public key receives the offline sign-in
/// bundle. Revoked, never deleted.
/// </summary>
public sealed class Device : IBusinessOwned
{
    public const int NameMaxLength = 60;

    public Guid BusinessId { get; set; }

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    public required byte[] CredentialHash { get; set; }

    /// <summary>RSA public key (X.509 SubjectPublicKeyInfo) for the offline bundle.</summary>
    public required byte[] PublicKey { get; set; }

    public Guid RegisteredByStaffId { get; init; }

    public DateTimeOffset RegisteredAt { get; init; }

    public DateTimeOffset? LastUsedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public Guid? RevokedByStaffId { get; set; }

    /// <summary>Consecutive failed PIN sign-ins on this device (any username).</summary>
    public int FailedSignins { get; set; }

    /// <summary>Back-off: no PIN sign-in on this device before this time.</summary>
    public DateTimeOffset? NextAttemptAt { get; set; }

    public int Version { get; set; } = 1;
}

public sealed class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("devices", t =>
            t.HasCheckConstraint("ck_devices_name", $"char_length(name) BETWEEN 1 AND {Device.NameMaxLength}"));
        builder.ConfigureBusinessOwned();
        builder.Property(d => d.Name).HasMaxLength(Device.NameMaxLength);
        builder.Property(d => d.Version).IsConcurrencyToken().HasDefaultValue(1);
        builder.Property(d => d.RegisteredAt).HasDefaultValueSql("now()");
        builder.HasIndex(d => d.CredentialHash).IsUnique();
        builder.HasOne<StaffMember>().WithMany()
            .HasForeignKey(d => new { d.BusinessId, d.RegisteredByStaffId })
            .HasPrincipalKey(s => new { s.BusinessId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StaffMember>().WithMany()
            .HasForeignKey(d => new { d.BusinessId, d.RevokedByStaffId })
            .HasPrincipalKey(s => new { s.BusinessId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
