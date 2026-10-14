using HelloPos.Api.Features.Catalog;
using HelloPos.Api.Features.Staff;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Infrastructure.Tenancy;
using HelloPos.Kernel.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HelloPos.Api.Features.Settings;

// Business settings (feature #12, data-model.md). Every table is business-owned, every link carries the
// business id, nothing a sale may reference is deleted, and the lists tills need carry a trigger-set
// revision like the catalog (feature #13).

/// <summary>How customers pay; Cash is built in, the rest are recorded without a provider.</summary>
public sealed class PaymentType : IBusinessOwned
{
    public Guid BusinessId { get; set; }

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    public required string NormalizedName { get; set; }

    /// <summary>Stored snake_case: <c>cash</c>, <c>external_card</c>, <c>digital_wallet</c>, <c>cheque</c>, <c>voucher</c>, <c>other</c>.</summary>
    public required string Kind { get; set; }

    public bool BuiltIn { get; init; }

    public bool OpensDrawer { get; set; }

    public bool AsksReference { get; set; }

    public int Position { get; set; }

    public bool Active { get; set; } = true;

    public int Version { get; set; } = 1;

    public long Revision { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Only cash gives change (FR-003).</summary>
    public bool GivesChange => Kind == PaymentKinds.Cash;
}

public sealed class TableArea : IBusinessOwned
{
    public Guid BusinessId { get; set; }

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    public required string NormalizedName { get; set; }

    public int Position { get; set; }

    public bool Active { get; set; } = true;

    public int Version { get; set; } = 1;

    public long Revision { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class DiningTable : IBusinessOwned
{
    public Guid BusinessId { get; set; }

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid AreaId { get; set; }

    public required string Name { get; set; }

    public required string NormalizedName { get; set; }

    public short Seats { get; set; } = SettingsRules.DefaultSeats;

    public int Position { get; set; }

    public bool Active { get; set; } = true;

    public int Version { get; set; } = 1;

    public long Revision { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>One row per business (its id equals the business id): tables switch, discount limit, tips.</summary>
public sealed class SellingSettings : IBusinessOwned
{
    public Guid BusinessId { get; set; }

    public Guid Id { get; init; }

    public bool UsesTables { get; set; }

    /// <summary>0–100; new businesses start at 0 (clarification Q2).</summary>
    public decimal DiscountLimitPercent { get; set; }

    /// <summary>Currency units of the business currency; minor units on the wire.</summary>
    public decimal? DiscountLimitAmount { get; set; }

    public bool TipsEnabled { get; set; }

    public List<decimal> TipPresets { get; set; } = [];

    public int Version { get; set; } = 1;

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Append-only settings history (FR-016); the app login may insert and read only.</summary>
public sealed class SettingsChange : IBusinessOwned
{
    public Guid BusinessId { get; set; }

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public DateTimeOffset At { get; init; }

    public Guid ActorStaffId { get; init; }

    public required string Entity { get; init; }

    public Guid EntityId { get; init; }

    public required string Action { get; init; }

    /// <summary>JSON object: field name → [old, new].</summary>
    public required string Changes { get; init; }
}

/// <summary>Payment kinds as stored (snake_case) and on the wire (camelCase).</summary>
public static class PaymentKinds
{
    public const string Cash = "cash";

    private static readonly Dictionary<string, string> ToStored = new(StringComparer.Ordinal)
    {
        [SettingsRules.Cash] = Cash,
        ["externalCard"] = "external_card",
        ["digitalWallet"] = "digital_wallet",
        ["cheque"] = "cheque",
        ["voucher"] = "voucher",
        ["other"] = "other",
    };

    private static readonly Dictionary<string, string> ToWireMap = ToStored.ToDictionary(p => p.Value, p => p.Key, StringComparer.Ordinal);

    public static string Stored(string wire) => ToStored[wire];

    public static string ToWire(string stored) => ToWireMap[stored];

    public static IEnumerable<string> All => ToStored.Values;
}

public sealed class PaymentTypeConfiguration : IEntityTypeConfiguration<PaymentType>
{
    public void Configure(EntityTypeBuilder<PaymentType> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("payment_types", t =>
        {
            t.HasCheckConstraint("ck_payment_types_name", $"char_length(name) BETWEEN 1 AND {SettingsRules.PaymentTypeNameMaxLength}");
            t.HasCheckConstraint("ck_payment_types_kind", $"kind IN ({string.Join(", ", PaymentKinds.All.Select(k => $"'{k}'"))})");
            t.HasCheckConstraint("ck_payment_types_cash_is_built_in", "(kind = 'cash') = built_in");
            t.HasCheckConstraint("ck_payment_types_built_in_active", "NOT built_in OR active");
        });
        builder.ConfigureBusinessOwned();
        builder.ConfigureCatalogRevision();
        builder.Ignore(p => p.GivesChange);
        builder.Property(p => p.Name).HasMaxLength(SettingsRules.PaymentTypeNameMaxLength);
        builder.Property(p => p.NormalizedName).HasMaxLength(SettingsRules.PaymentTypeNameMaxLength);
        builder.Property(p => p.Kind).HasMaxLength(14);
        builder.Property(p => p.Version).IsConcurrencyToken().HasDefaultValue(1);
        builder.Property(p => p.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(p => p.UpdatedAt).HasDefaultValueSql("now()");
        builder.HasIndex(p => new { p.BusinessId, p.NormalizedName }).IsUnique();
        builder.HasIndex(p => p.BusinessId).IsUnique().HasFilter("built_in").HasDatabaseName("ux_payment_types_one_built_in");
    }
}

public sealed class TableAreaConfiguration : IEntityTypeConfiguration<TableArea>
{
    public void Configure(EntityTypeBuilder<TableArea> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("table_areas", t =>
            t.HasCheckConstraint("ck_table_areas_name", $"char_length(name) BETWEEN 1 AND {SettingsRules.AreaNameMaxLength}"));
        builder.ConfigureBusinessOwned();
        builder.ConfigureCatalogRevision();
        builder.Property(a => a.Name).HasMaxLength(SettingsRules.AreaNameMaxLength);
        builder.Property(a => a.NormalizedName).HasMaxLength(SettingsRules.AreaNameMaxLength);
        builder.Property(a => a.Version).IsConcurrencyToken().HasDefaultValue(1);
        builder.Property(a => a.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(a => a.UpdatedAt).HasDefaultValueSql("now()");
        builder.HasIndex(a => new { a.BusinessId, a.NormalizedName }).IsUnique();
    }
}

public sealed class DiningTableConfiguration : IEntityTypeConfiguration<DiningTable>
{
    public void Configure(EntityTypeBuilder<DiningTable> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("dining_tables", t =>
        {
            t.HasCheckConstraint("ck_dining_tables_name", $"char_length(name) BETWEEN 1 AND {SettingsRules.TableNameMaxLength}");
            t.HasCheckConstraint("ck_dining_tables_seats", $"seats BETWEEN {SettingsRules.MinSeats} AND {SettingsRules.MaxSeats}");
        });
        builder.ConfigureBusinessOwned();
        builder.ConfigureCatalogRevision();
        builder.Property(t => t.Name).HasMaxLength(SettingsRules.TableNameMaxLength);
        builder.Property(t => t.NormalizedName).HasMaxLength(SettingsRules.TableNameMaxLength);
        builder.Property(t => t.Version).IsConcurrencyToken().HasDefaultValue(1);
        builder.Property(t => t.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(t => t.UpdatedAt).HasDefaultValueSql("now()");
        builder.HasIndex(t => new { t.BusinessId, t.NormalizedName }).IsUnique();
        builder.HasIndex(t => new { t.BusinessId, t.AreaId });
        builder.HasOne<TableArea>().WithMany()
            .HasForeignKey(t => new { t.BusinessId, t.AreaId })
            .HasPrincipalKey(a => new { a.BusinessId, a.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SellingSettingsConfiguration : IEntityTypeConfiguration<SellingSettings>
{
    public void Configure(EntityTypeBuilder<SellingSettings> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("selling_settings", t =>
        {
            t.HasCheckConstraint("ck_selling_settings_one_per_business", "id = business_id");
            t.HasCheckConstraint("ck_selling_settings_limit_percent", "discount_limit_percent BETWEEN 0 AND 100");
            t.HasCheckConstraint("ck_selling_settings_limit_amount", "discount_limit_amount IS NULL OR discount_limit_amount >= 0");
            t.HasCheckConstraint("ck_selling_settings_tip_presets", $"cardinality(tip_presets) <= {SettingsRules.MaxTipPresets}");
        });
        builder.ConfigureBusinessOwned();
        builder.Property(s => s.DiscountLimitPercent).HasPrecision(5, 2).HasDefaultValue(0m);
        builder.Property(s => s.DiscountLimitAmount).HasPrecision(19, 4);
        builder.Property(s => s.TipPresets).HasColumnType("numeric(5,2)[]").HasDefaultValueSql("'{}'");
        builder.Property(s => s.Version).IsConcurrencyToken().HasDefaultValue(1);
        builder.Property(s => s.UpdatedAt).HasDefaultValueSql("now()");
    }
}

public sealed class SettingsChangeConfiguration : IEntityTypeConfiguration<SettingsChange>
{
    public void Configure(EntityTypeBuilder<SettingsChange> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("settings_changes");
        builder.ConfigureBusinessOwned();
        builder.Property(c => c.Entity).HasMaxLength(20);
        builder.Property(c => c.Action).HasMaxLength(20);
        builder.Property(c => c.Changes).HasColumnType("jsonb");
        builder.Property(c => c.At).HasDefaultValueSql("now()");
        builder.HasIndex(c => new { c.BusinessId, c.EntityId, c.At });
        builder.HasOne<StaffMember>().WithMany()
            .HasForeignKey(c => new { c.BusinessId, c.ActorStaffId })
            .HasPrincipalKey(s => new { s.BusinessId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
