using HelloPos.Api.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace HelloPos.Api.Infrastructure.Persistence;

/// <summary>One place for provider settings, shared by the API, the migrator, design-time tooling and tests.</summary>
public static class PersistenceOptions
{
    /// <summary>
    /// The business data context. Without a connection string (an API started without a database) it
    /// is configured but every use fails, as any other database call would.
    /// </summary>
    public static TBuilder UseHelloPosDatabase<TBuilder>(this TBuilder builder, string? connectionString)
        where TBuilder : DbContextOptionsBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        void Configure(Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlDbContextOptionsBuilder npgsql) => npgsql
            .MigrationsHistoryTable(HelloPosDbContext.MigrationsHistoryTable, HelloPosDbContext.Schema)
            .MigrationsAssembly(typeof(HelloPosDbContext).Assembly.GetName().Name);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            builder.UseNpgsql(Configure);
        }
        else
        {
            builder.UseNpgsql(ConnectionStrings.Normalize(connectionString), Configure);
        }

        builder
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(BusinessSessionInterceptor.Instance, BusinessStampingInterceptor.Instance);
        return builder;
    }

    /// <summary>
    /// Owner accounts (feature 005): same provider and naming, own history table in schema
    /// <c>identity</c>, and no business interceptors (nothing in this schema is business-owned).
    /// Without a connection string (an API started without a database) the context is configured but
    /// every use fails, as any other database call would.
    /// </summary>
    public static TBuilder UseOwnerIdentityDatabase<TBuilder>(this TBuilder builder, string? connectionString)
        where TBuilder : DbContextOptionsBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        void Configure(Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlDbContextOptionsBuilder npgsql) => npgsql
            .MigrationsHistoryTable(OwnerIdentityDbContext.MigrationsHistoryTable, OwnerIdentityDbContext.Schema)
            .MigrationsAssembly(typeof(OwnerIdentityDbContext).Assembly.GetName().Name);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            builder.UseNpgsql(Configure);
        }
        else
        {
            builder.UseNpgsql(ConnectionStrings.Normalize(connectionString), Configure);
        }

        builder.UseSnakeCaseNamingConvention();
        return builder;
    }
}
