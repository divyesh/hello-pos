using HelloPos.Api.Features.Businesses;
using HelloPos.Api.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HelloPos.Api.Infrastructure.Persistence;

/// <summary>
/// Shared configuration for business-owned tables (research R7), so no slice can forget the rules:
/// key <c>(business_id, id)</c> and a required link to <c>businesses</c>. Links to other
/// business-owned tables must also include the business id:
/// <code>
/// builder.HasOne&lt;Category&gt;().WithMany()
///     .HasForeignKey(p =&gt; new { p.BusinessId, p.CategoryId })
///     .HasPrincipalKey(c =&gt; new { c.BusinessId, c.Id });
/// </code>
/// </summary>
public static class BusinessOwnedConfiguration
{
    public static EntityTypeBuilder<TEntity> ConfigureBusinessOwned<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : class, IBusinessOwned
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(e => new { e.BusinessId, e.Id });
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.HasOne<Business>()
            .WithMany()
            .HasForeignKey(e => e.BusinessId)
            .OnDelete(DeleteBehavior.Restrict);
        return builder;
    }
}
