using HelloPos.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HelloPos.Api.Infrastructure.Identity;

/// <summary>Rights in schema <c>identity</c> (research R1): row access for the app group role, no DDL.</summary>
public static class IdentityMigrationExtensions
{
    public static readonly string AppRowRightsSql = $"""
        GRANT USAGE ON SCHEMA {OwnerIdentityDbContext.Schema} TO {Migrator.AppGroupRole};
        ALTER DEFAULT PRIVILEGES IN SCHEMA {OwnerIdentityDbContext.Schema}
          GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {Migrator.AppGroupRole};
        ALTER DEFAULT PRIVILEGES IN SCHEMA {OwnerIdentityDbContext.Schema}
          GRANT USAGE, SELECT ON SEQUENCES TO {Migrator.AppGroupRole};
        """;

    public static void GrantAppRowRights(this MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.Sql(AppRowRightsSql);
    }
}
