using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HelloPos.Api.Tests.Tenancy.Samples;

// Test-only business-owned tables shaped like the old system's categories -> products (spec FR-017).
// They exist only in test databases; production migrations never contain them.

public sealed class SampleCategory : IBusinessOwned
{
    public Guid BusinessId { get; set; }

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class SampleProduct : IBusinessOwned
{
    public Guid BusinessId { get; set; }

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid CategoryId { get; set; }

    public required string Name { get; set; }

    public decimal UnitPrice { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class SampleCategoryConfiguration : IEntityTypeConfiguration<SampleCategory>
{
    public void Configure(EntityTypeBuilder<SampleCategory> builder)
    {
        builder.ToTable("sample_categories");
        builder.ConfigureBusinessOwned();
        builder.Property(c => c.Name).HasMaxLength(200);
        builder.Property(c => c.CreatedAt).HasDefaultValueSql("now()");
    }
}

public sealed class SampleProductConfiguration : IEntityTypeConfiguration<SampleProduct>
{
    public void Configure(EntityTypeBuilder<SampleProduct> builder)
    {
        builder.ToTable("sample_products");
        builder.ConfigureBusinessOwned();
        builder.HasOne<SampleCategory>().WithMany()
            .HasForeignKey(p => new { p.BusinessId, p.CategoryId })
            .HasPrincipalKey(c => new { c.BusinessId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(p => p.Name).HasMaxLength(200);
        builder.Property(p => p.UnitPrice).HasPrecision(19, 4);
        builder.Property(p => p.CreatedAt).HasDefaultValueSql("now()");
    }
}

/// <summary>The production model plus the sample tables.</summary>
public sealed class SampleDbContext : HelloPosDbContext
{
    public SampleDbContext(DbContextOptions<SampleDbContext> options, ICurrentBusiness currentBusiness)
        : base(options, currentBusiness)
    {
    }

    public DbSet<SampleCategory> SampleCategories => Set<SampleCategory>();

    public DbSet<SampleProduct> SampleProducts => Set<SampleProduct>();

    public static SampleDbContext Create(string connectionString, Guid? businessId) =>
        new(new DbContextOptionsBuilder<SampleDbContext>().UseHelloPosDatabase(connectionString).Options,
            new FixedCurrentBusiness(businessId));

    protected override void ConfigureAdditionalModel(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new SampleCategoryConfiguration());
        modelBuilder.ApplyConfiguration(new SampleProductConfiguration());
    }
}

/// <summary>Test-only: a current business set explicitly (spec FR-007). Never exists in production.</summary>
public sealed class FixedCurrentBusiness(Guid? businessId) : ICurrentBusiness
{
    public Guid? BusinessId => businessId;
}
