using HelloPos.Api.Features.Businesses;
using HelloPos.Api.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HelloPos.Api.Infrastructure.Persistence;

/// <summary>
/// The single unit of work. Slices keep their entities and <see cref="IEntityTypeConfiguration{T}"/>
/// classes next to the feature; this context discovers them. Every <see cref="IBusinessOwned"/>
/// entity gets the named query filter <see cref="BusinessScopeFilter"/> (research R5).
/// </summary>
public class HelloPosDbContext : DbContext
{
    public const string Schema = "pos";
    public const string BusinessScopeFilter = "BusinessScope";
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    private readonly Guid? currentBusinessId;

    public HelloPosDbContext(DbContextOptions<HelloPosDbContext> options, ICurrentBusiness currentBusiness)
        : this((DbContextOptions)options, currentBusiness)
    {
    }

    /// <summary>For contexts that extend the model (test-only sample tables).</summary>
    protected HelloPosDbContext(DbContextOptions options, ICurrentBusiness currentBusiness)
        : base(options)
    {
        ArgumentNullException.ThrowIfNull(currentBusiness);
        currentBusinessId = currentBusiness.BusinessId;
    }

    /// <summary>The business this context instance is scoped to; <c>null</c> sees no business-owned rows.</summary>
    public Guid? CurrentBusinessId => currentBusinessId;

    public DbSet<Business> Businesses => Set<Business>();

    protected sealed override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HelloPosDbContext).Assembly);
        ConfigureAdditionalModel(modelBuilder);

        // After every entity is known: scope each business-owned entity to the current business.
        var scope = typeof(HelloPosDbContext).GetMethod(nameof(ApplyBusinessScope),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(t => typeof(IBusinessOwned).IsAssignableFrom(t.ClrType)))
        {
            scope.MakeGenericMethod(entityType.ClrType).Invoke(this, [modelBuilder]);
        }
    }

    /// <summary>Hook for contexts that add entities from other assemblies (tests only).</summary>
    protected virtual void ConfigureAdditionalModel(ModelBuilder modelBuilder)
    {
    }

    private void ApplyBusinessScope<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, IBusinessOwned =>
        modelBuilder.Entity<TEntity>().HasQueryFilter(BusinessScopeFilter, e => e.BusinessId == currentBusinessId);
}
