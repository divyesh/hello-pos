using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Infrastructure.Tenancy;
using HelloPos.Kernel.Staff;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HelloPos.Api.Features.Staff;

/// <summary>
/// A person who works at one business (feature 006, data-model.md): owner, manager or cashier. Owners
/// and managers with <see cref="AccountId"/> use the back office; anyone with a POS username and PIN
/// uses the POS (#7). Staff are deactivated, never deleted, so history stays attributable.
/// </summary>
public sealed class StaffMember : IBusinessOwned
{
    public Guid BusinessId { get; set; }

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string DisplayName { get; set; }

    public StaffRole Role { get; set; }

    public bool Active { get; set; } = true;

    /// <summary>The back-office account (<c>identity.owner_accounts</c>); required for owners.</summary>
    public Guid? AccountId { get; set; }

    /// <summary>Lower-case, unique within the business; set together with <see cref="PinHash"/>.</summary>
    public string? PosUsername { get; set; }

    /// <summary>Slow salted hash of the PIN; never returned or logged.</summary>
    public string? PinHash { get; set; }

    /// <summary>When the PIN last changed, so #7 can end POS sessions issued before it.</summary>
    public DateTimeOffset? PinChangedAt { get; set; }

    /// <summary>Concurrency token: +1 on every change; a stale value is "changed by someone else".</summary>
    public int Version { get; set; } = 1;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public static class StaffRoleNames
{
    public const string Owner = "owner";
    public const string Manager = "manager";
    public const string Cashier = "cashier";

    public static string ToWire(StaffRole role) => role switch
    {
        StaffRole.Owner => Owner,
        StaffRole.Manager => Manager,
        _ => Cashier,
    };

    public static StaffRole? Parse(string? value) => value switch
    {
        Owner => StaffRole.Owner,
        Manager => StaffRole.Manager,
        Cashier => StaffRole.Cashier,
        _ => null,
    };
}

public sealed class StaffMemberConfiguration : IEntityTypeConfiguration<StaffMember>
{
    public void Configure(EntityTypeBuilder<StaffMember> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("staff_members", t =>
        {
            t.HasCheckConstraint("ck_staff_members_role", "role IN ('owner', 'manager', 'cashier')");
            t.HasCheckConstraint("ck_staff_members_display_name", $"char_length(display_name) BETWEEN 1 AND {StaffRules.DisplayNameMaxLength}");
            t.HasCheckConstraint("ck_staff_members_owner_account", "role <> 'owner' OR account_id IS NOT NULL");
            t.HasCheckConstraint("ck_staff_members_pos_credentials", "(pos_username IS NULL) = (pin_hash IS NULL)");
            t.HasCheckConstraint("ck_staff_members_can_sign_in", "account_id IS NOT NULL OR pos_username IS NOT NULL");
            t.HasCheckConstraint("ck_staff_members_pos_username", "pos_username ~ '^[a-z0-9._-]{3,32}$'");
        });
        builder.ConfigureBusinessOwned();
        builder.Property(s => s.DisplayName).HasMaxLength(StaffRules.DisplayNameMaxLength);
        builder.Property(s => s.Role)
            .HasConversion(r => StaffRoleNames.ToWire(r), v => StaffRoleNames.Parse(v) ?? StaffRole.Cashier)
            .HasMaxLength(10);
        builder.Property(s => s.PosUsername).HasMaxLength(32);
        builder.Property(s => s.Version).IsConcurrencyToken().HasDefaultValue(1);
        builder.Property(s => s.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(s => s.UpdatedAt).HasDefaultValueSql("now()");
        builder.HasIndex(s => new { s.BusinessId, s.PosUsername }).IsUnique().HasFilter("pos_username IS NOT NULL");
        builder.HasIndex(s => new { s.BusinessId, s.AccountId }).IsUnique().HasFilter("account_id IS NOT NULL");
        builder.HasIndex(s => s.AccountId);
    }
}
