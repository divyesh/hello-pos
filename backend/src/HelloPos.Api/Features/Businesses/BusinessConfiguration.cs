using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HelloPos.Api.Features.Businesses;

/// <summary><c>pos.businesses</c> per data-model.md; row-level security is added in the migration.</summary>
public sealed class BusinessConfiguration : IEntityTypeConfiguration<Business>
{
    public void Configure(EntityTypeBuilder<Business> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("businesses", t =>
        {
            t.HasCheckConstraint("ck_businesses_country_code", "country_code IN ('IN', 'US', 'CA')");
            t.HasCheckConstraint("ck_businesses_currency_code", "currency_code ~ '^[A-Z]{3}$'");
            t.HasCheckConstraint("ck_businesses_pricing_mode", "pricing_mode IN ('tax_inclusive', 'tax_exclusive')");
            t.HasCheckConstraint("ck_businesses_financial_year_start_month", "financial_year_start_month BETWEEN 1 AND 12");
            t.HasCheckConstraint("ck_businesses_name", "char_length(name) BETWEEN 1 AND 200");
        });

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever().HasDefaultValueSql("uuidv7()");
        builder.Property(b => b.Name).HasMaxLength(200);
        builder.Property(b => b.CountryCode).HasColumnType("char(2)");
        builder.Property(b => b.CurrencyCode).HasColumnType("char(3)");
        builder.Property(b => b.PricingMode)
            .HasConversion(
                mode => mode == PricingMode.TaxInclusive ? "tax_inclusive" : "tax_exclusive",
                value => value == "tax_inclusive" ? PricingMode.TaxInclusive : PricingMode.TaxExclusive)
            .HasMaxLength(20);
        builder.Property(b => b.TimeZone).HasMaxLength(64);
        builder.Property(b => b.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(b => b.UpdatedAt).HasDefaultValueSql("now()");
    }
}
