using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HelloPos.Api.Infrastructure.Identity;

/// <summary>
/// Owner accounts and the Data Protection key ring, in schema <c>identity</c> (research R1, R4). Separate
/// from <see cref="Persistence.HelloPosDbContext"/>: no business scope, no row-level security, its own
/// migrations and history table. Owned by the migrator; the app login gets row rights only.
/// </summary>
public sealed class OwnerIdentityDbContext(DbContextOptions<OwnerIdentityDbContext> options)
    : IdentityUserContext<OwnerAccount, Guid>(options), IDataProtectionKeyContext
{
    public const string Schema = "identity";
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.OnModelCreating(builder);
        builder.HasDefaultSchema(Schema);

        builder.Entity<OwnerAccount>(account =>
        {
            account.ToTable("owner_accounts", t =>
                t.HasCheckConstraint("ck_owner_accounts_display_name",
                    $"char_length(display_name) BETWEEN 1 AND {OwnerAccount.DisplayNameMaxLength}"));
            account.Property(a => a.Id).ValueGeneratedNever();
            account.Property(a => a.DisplayName).HasMaxLength(OwnerAccount.DisplayNameMaxLength);
            account.Property(a => a.CreatedAt).HasDefaultValueSql("now()");
        });
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("owner_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("owner_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("owner_tokens");
        builder.Entity<DataProtectionKey>().ToTable("data_protection_keys");
    }
}
