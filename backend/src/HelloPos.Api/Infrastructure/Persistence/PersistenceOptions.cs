using Microsoft.EntityFrameworkCore;

namespace HelloPos.Api.Infrastructure.Persistence;

/// <summary>One place for provider settings, shared by the API, the migrator, design-time tooling and tests.</summary>
public static class PersistenceOptions
{
    public static TBuilder UseHelloPosDatabase<TBuilder>(this TBuilder builder, string connectionString)
        where TBuilder : DbContextOptionsBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder
            .UseNpgsql(ConnectionStrings.Normalize(connectionString), npgsql => npgsql
                .MigrationsHistoryTable(HelloPosDbContext.MigrationsHistoryTable, HelloPosDbContext.Schema)
                .MigrationsAssembly(typeof(HelloPosDbContext).Assembly.GetName().Name))
            .UseSnakeCaseNamingConvention();
        return builder;
    }
}
