using HelloPos.Api.Features.Staff;
using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Infrastructure.Tenancy;
using HelloPos.Kernel.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HelloPos.Api.Features.Catalog;

// The catalog (feature #11, data-model.md). Every table is business-owned, every link carries the
// business id (composite keys), and nothing a sale may reference is deleted: it is deactivated.

public static class TaxKinds
{
    public const string Taxable = "taxable";
    public const string ZeroRated = "zero_rated";
    public const string Exempt = "exempt";
}

/// <summary>A tax treatment ("HST 13%", "GST 5%", "Exempt"); its rates live in dated versions.</summary>
public sealed class TaxProfile : IBusinessOwned
{
    public Guid BusinessId { get; set; }

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    /// <summary>Lower-case trimmed name; unique per business.</summary>
    public required string NormalizedName { get; set; }

    /// <summary><see cref="TaxKinds"/>; fixed after creation.</summary>
    public required string Kind { get; init; }

    public bool Active { get; set; } = true;

    public int Version { get; set; } = 1;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>The rates of a tax profile from a date (business time zone); immutable once in force.</summary>
public sealed class TaxProfileVersion : IBusinessOwned
{
    public Guid BusinessId { get; set; }

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid TaxProfileId { get; init; }

    public DateOnly EffectiveFrom { get; set; }
}

public sealed class TaxComponent : IBusinessOwned
{
    public Guid BusinessId { get; set; }

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid TaxProfileVersionId { get; init; }

    public short Position { get; init; }

    public required string Label { get; init; }

    public decimal RatePercent { get; init; }
}

public sealed class Category : IBusinessOwned
{
    public Guid BusinessId { get; set; }

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    public required string NormalizedName { get; set; }

    public int SortOrder { get; set; }

    public Guid DefaultTaxProfileId { get; set; }

    public bool Active { get; set; } = true;

    public int Version { get; set; } = 1;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class Product : IBusinessOwned
{
    public Guid BusinessId { get; set; }

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    public string? Sku { get; set; }

    /// <summary>Lower-case SKU; unique per business when present.</summary>
    public string? NormalizedSku { get; set; }

    public Guid CategoryId { get; set; }

    /// <summary>Overrides the category's default tax profile; null follows it.</summary>
    public Guid? TaxProfileId { get; set; }

    public required string Unit { get; set; }

    /// <summary>Currency units (not minor units); null exactly when <see cref="OpenPrice"/>.</summary>
    public decimal? UnitPrice { get; set; }

    public bool OpenPrice { get; set; }

    public decimal MinQuantity { get; set; } = 1m;

    public decimal Step { get; set; } = 1m;

    /// <summary>Minimum customer age; null when not age-restricted.</summary>
    public short? MinAge { get; set; }

    /// <summary>Container deposit per unit in currency units.</summary>
    public decimal DepositPerUnit { get; set; }

    public bool Active { get; set; } = true;

    public int Version { get; set; } = 1;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ProductBarcode : IBusinessOwned
{
    public Guid BusinessId { get; set; }

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid ProductId { get; init; }

    /// <summary>Normalized by <see cref="BarcodeRules"/> (UPC-A in its 13-digit form).</summary>
    public required string Code { get; init; }

    public required string Kind { get; init; }
}

public sealed class ItemDiscount : IBusinessOwned
{
    public Guid BusinessId { get; set; }

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid ProductId { get; init; }

    /// <summary><c>percent</c> or <c>fixed</c>.</summary>
    public required string Kind { get; set; }

    /// <summary>Percent (0–100), or a fixed amount per unit in currency units.</summary>
    public decimal Value { get; set; }

    public DateOnly FromDate { get; set; }

    /// <summary>Inclusive; null is open-ended.</summary>
    public DateOnly? ToDate { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Append-only catalog history (FR-015); the app login may insert and read only.</summary>
public sealed class CatalogChange : IBusinessOwned
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

    public Guid? ImportId { get; init; }
}

public sealed class CatalogImport : IBusinessOwned
{
    public Guid BusinessId { get; set; }

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public DateTimeOffset At { get; init; }

    public Guid ActorStaffId { get; init; }

    public required string FileName { get; init; }

    public int Rows { get; init; }

    public int Created { get; init; }

    public int Updated { get; init; }

    public int Unchanged { get; init; }
}

public sealed class TaxProfileConfiguration : IEntityTypeConfiguration<TaxProfile>
{
    public void Configure(EntityTypeBuilder<TaxProfile> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("tax_profiles", t =>
        {
            t.HasCheckConstraint("ck_tax_profiles_kind", "kind IN ('taxable', 'zero_rated', 'exempt')");
            t.HasCheckConstraint("ck_tax_profiles_name", $"char_length(name) BETWEEN 1 AND {CatalogRules.TaxProfileNameMaxLength}");
        });
        builder.ConfigureBusinessOwned();
        builder.Property(p => p.Name).HasMaxLength(CatalogRules.TaxProfileNameMaxLength);
        builder.Property(p => p.NormalizedName).HasMaxLength(CatalogRules.TaxProfileNameMaxLength);
        builder.Property(p => p.Kind).HasMaxLength(12);
        builder.Property(p => p.Version).IsConcurrencyToken().HasDefaultValue(1);
        builder.Property(p => p.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(p => p.UpdatedAt).HasDefaultValueSql("now()");
        builder.HasIndex(p => new { p.BusinessId, p.NormalizedName }).IsUnique();
    }
}

public sealed class TaxProfileVersionConfiguration : IEntityTypeConfiguration<TaxProfileVersion>
{
    public void Configure(EntityTypeBuilder<TaxProfileVersion> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("tax_profile_versions");
        builder.ConfigureBusinessOwned();
        builder.HasIndex(v => new { v.BusinessId, v.TaxProfileId, v.EffectiveFrom }).IsUnique();
        builder.HasOne<TaxProfile>().WithMany()
            .HasForeignKey(v => new { v.BusinessId, v.TaxProfileId })
            .HasPrincipalKey(p => new { p.BusinessId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TaxComponentConfiguration : IEntityTypeConfiguration<TaxComponent>
{
    public void Configure(EntityTypeBuilder<TaxComponent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("tax_components", t =>
        {
            t.HasCheckConstraint("ck_tax_components_rate", "rate_percent >= 0 AND rate_percent <= 100");
            t.HasCheckConstraint("ck_tax_components_label", $"char_length(label) BETWEEN 1 AND {CatalogRules.ComponentLabelMaxLength}");
        });
        builder.ConfigureBusinessOwned();
        builder.Property(c => c.Label).HasMaxLength(CatalogRules.ComponentLabelMaxLength);
        builder.Property(c => c.RatePercent).HasPrecision(6, 3);
        builder.HasIndex(c => new { c.BusinessId, c.TaxProfileVersionId, c.Label }).IsUnique();

        // Removing a future version removes its components with it.
        builder.HasOne<TaxProfileVersion>().WithMany()
            .HasForeignKey(c => new { c.BusinessId, c.TaxProfileVersionId })
            .HasPrincipalKey(v => new { v.BusinessId, v.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("categories", t =>
            t.HasCheckConstraint("ck_categories_name", $"char_length(name) BETWEEN 1 AND {CatalogRules.CategoryNameMaxLength}"));
        builder.ConfigureBusinessOwned();
        builder.Property(c => c.Name).HasMaxLength(CatalogRules.CategoryNameMaxLength);
        builder.Property(c => c.NormalizedName).HasMaxLength(CatalogRules.CategoryNameMaxLength);
        builder.Property(c => c.Version).IsConcurrencyToken().HasDefaultValue(1);
        builder.Property(c => c.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(c => c.UpdatedAt).HasDefaultValueSql("now()");
        builder.HasIndex(c => new { c.BusinessId, c.NormalizedName }).IsUnique();
        builder.HasOne<TaxProfile>().WithMany()
            .HasForeignKey(c => new { c.BusinessId, c.DefaultTaxProfileId })
            .HasPrincipalKey(p => new { p.BusinessId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("products", t =>
        {
            t.HasCheckConstraint("ck_products_name", $"char_length(name) BETWEEN 1 AND {CatalogRules.ProductNameMaxLength}");
            t.HasCheckConstraint("ck_products_unit", "unit IN ('piece', 'g', 'kg', 'ml', 'l')");
            t.HasCheckConstraint("ck_products_price", "(open_price AND unit_price IS NULL) OR (NOT open_price AND unit_price >= 0)");
            t.HasCheckConstraint("ck_products_quantities", "min_quantity > 0 AND step > 0 AND mod(min_quantity, step) = 0");
            t.HasCheckConstraint("ck_products_min_age", "min_age IS NULL OR min_age BETWEEN 1 AND 99");
            t.HasCheckConstraint("ck_products_deposit", "deposit_per_unit >= 0");
        });
        builder.ConfigureBusinessOwned();
        builder.Property(p => p.Name).HasMaxLength(CatalogRules.ProductNameMaxLength);
        builder.Property(p => p.Sku).HasMaxLength(CatalogRules.SkuMaxLength);
        builder.Property(p => p.NormalizedSku).HasMaxLength(CatalogRules.SkuMaxLength);
        builder.Property(p => p.Unit).HasMaxLength(5);
        builder.Property(p => p.UnitPrice).HasPrecision(19, 4);
        builder.Property(p => p.MinQuantity).HasPrecision(9, 3);
        builder.Property(p => p.Step).HasPrecision(9, 3);
        builder.Property(p => p.DepositPerUnit).HasPrecision(19, 4);
        builder.Property(p => p.Version).IsConcurrencyToken().HasDefaultValue(1);
        builder.Property(p => p.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(p => p.UpdatedAt).HasDefaultValueSql("now()");
        builder.HasIndex(p => new { p.BusinessId, p.NormalizedSku }).IsUnique().HasFilter("normalized_sku IS NOT NULL");
        builder.HasIndex(p => new { p.BusinessId, p.CategoryId });
        builder.HasOne<Category>().WithMany()
            .HasForeignKey(p => new { p.BusinessId, p.CategoryId })
            .HasPrincipalKey(c => new { c.BusinessId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TaxProfile>().WithMany()
            .HasForeignKey(p => new { p.BusinessId, p.TaxProfileId })
            .HasPrincipalKey(t => new { t.BusinessId, t.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ProductBarcodeConfiguration : IEntityTypeConfiguration<ProductBarcode>
{
    public void Configure(EntityTypeBuilder<ProductBarcode> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("product_barcodes", t =>
        {
            t.HasCheckConstraint("ck_product_barcodes_kind", "kind IN ('ean13', 'ean8', 'plu')");
            t.HasCheckConstraint("ck_product_barcodes_code", "code ~ '^[0-9]{4,13}$'");
        });
        builder.ConfigureBusinessOwned();
        builder.Property(b => b.Code).HasMaxLength(13);
        builder.Property(b => b.Kind).HasMaxLength(5);
        builder.HasIndex(b => new { b.BusinessId, b.Code }).IsUnique();

        // A product's barcodes are replaced as a set; a removed one may be reused later.
        builder.HasOne<Product>().WithMany()
            .HasForeignKey(b => new { b.BusinessId, b.ProductId })
            .HasPrincipalKey(p => new { p.BusinessId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ItemDiscountConfiguration : IEntityTypeConfiguration<ItemDiscount>
{
    public void Configure(EntityTypeBuilder<ItemDiscount> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("item_discounts", t =>
        {
            t.HasCheckConstraint("ck_item_discounts_kind", "kind IN ('percent', 'fixed')");
            t.HasCheckConstraint("ck_item_discounts_value", "value >= 0 AND (kind <> 'percent' OR value <= 100)");
            t.HasCheckConstraint("ck_item_discounts_period", "to_date IS NULL OR to_date >= from_date");
        });
        builder.ConfigureBusinessOwned();
        builder.Property(d => d.Kind).HasMaxLength(7);
        builder.Property(d => d.Value).HasPrecision(19, 4);
        builder.Property(d => d.CreatedAt).HasDefaultValueSql("now()");
        builder.HasOne<Product>().WithMany()
            .HasForeignKey(d => new { d.BusinessId, d.ProductId })
            .HasPrincipalKey(p => new { p.BusinessId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CatalogChangeConfiguration : IEntityTypeConfiguration<CatalogChange>
{
    public void Configure(EntityTypeBuilder<CatalogChange> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("catalog_changes");
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
        builder.HasOne<CatalogImport>().WithMany()
            .HasForeignKey(c => new { c.BusinessId, c.ImportId })
            .HasPrincipalKey(i => new { i.BusinessId, i.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CatalogImportConfiguration : IEntityTypeConfiguration<CatalogImport>
{
    public void Configure(EntityTypeBuilder<CatalogImport> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("catalog_imports");
        builder.ConfigureBusinessOwned();
        builder.Property(i => i.FileName).HasMaxLength(200);
        builder.Property(i => i.At).HasDefaultValueSql("now()");
        builder.HasOne<StaffMember>().WithMany()
            .HasForeignKey(i => new { i.BusinessId, i.ActorStaffId })
            .HasPrincipalKey(s => new { s.BusinessId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
