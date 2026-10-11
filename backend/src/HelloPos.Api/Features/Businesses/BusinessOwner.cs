using HelloPos.Api.Infrastructure.Persistence;
using HelloPos.Api.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HelloPos.Api.Features.Businesses;

/// <summary>
/// Links an owner account to a business they own (feature 005, data-model.md). Business-owned, so it
/// follows every feature 003 rule; its row-level security also lets the signed-in owner see their own
/// links before a business is opened.
/// </summary>
public sealed class BusinessOwner : IBusinessOwned
{
    public Guid BusinessId { get; set; }

    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>The owner account in schema <c>identity</c> (no cross-schema foreign key; enforced by the API).</summary>
    public Guid OwnerId { get; init; }

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class BusinessOwnerConfiguration : IEntityTypeConfiguration<BusinessOwner>
{
    public void Configure(EntityTypeBuilder<BusinessOwner> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("business_owners");
        builder.ConfigureBusinessOwned();
        builder.HasIndex(o => new { o.BusinessId, o.OwnerId }).IsUnique();
        builder.HasIndex(o => o.OwnerId);
        builder.Property(o => o.CreatedAt).HasDefaultValueSql("now()");
    }
}
