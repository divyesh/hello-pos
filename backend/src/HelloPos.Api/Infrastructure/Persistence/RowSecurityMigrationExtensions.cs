using Microsoft.EntityFrameworkCore.Migrations;

namespace HelloPos.Api.Infrastructure.Persistence;

/// <summary>
/// Row-level security for business-owned tables (spec FR-014, research R6). Every business-owned
/// table calls <see cref="EnableBusinessRowSecurity"/> in the migration that creates it; the
/// convention check fails the build when one does not.
/// </summary>
public static class RowSecurityMigrationExtensions
{
    public const string PolicyName = "business_scope";
    public const string SessionSetting = "pos.business_id";

    /// <summary>
    /// Run once, first: the function policies use, and the data rights migrations grant to the
    /// <c>pos_app</c> group role for every table the migrator creates from now on.
    /// </summary>
    public static readonly string FoundationSql = $"""
        CREATE OR REPLACE FUNCTION {HelloPosDbContext.Schema}.current_business_id() RETURNS uuid
          LANGUAGE sql STABLE
          AS $$ SELECT NULLIF(current_setting('{SessionSetting}', true), '')::uuid $$;
        GRANT USAGE ON SCHEMA {HelloPosDbContext.Schema} TO {Migrator.AppGroupRole};
        GRANT EXECUTE ON FUNCTION {HelloPosDbContext.Schema}.current_business_id() TO {Migrator.AppGroupRole};
        ALTER DEFAULT PRIVILEGES IN SCHEMA {HelloPosDbContext.Schema}
          GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {Migrator.AppGroupRole};
        ALTER DEFAULT PRIVILEGES IN SCHEMA {HelloPosDbContext.Schema}
          GRANT USAGE, SELECT ON SEQUENCES TO {Migrator.AppGroupRole};
        """;

    public static void CreateTenancyFoundation(this MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.Sql(FoundationSql);
    }

    public static void EnableBusinessRowSecurity(this MigrationBuilder migrationBuilder, string table, string column = "business_id")
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.Sql(EnableSql(table, column));
    }

    public static void DisableBusinessRowSecurity(this MigrationBuilder migrationBuilder, string table)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.Sql($"""
            DROP POLICY IF EXISTS {PolicyName} ON {HelloPosDbContext.Schema}.{table};
            ALTER TABLE {HelloPosDbContext.Schema}.{table} DISABLE ROW LEVEL SECURITY;
            """);
    }

    /// <summary>Rows are visible and writable only when <paramref name="column"/> is the session's business.</summary>
    public static string EnableSql(string table, string column = "business_id") => $"""
        ALTER TABLE {HelloPosDbContext.Schema}.{table} ENABLE ROW LEVEL SECURITY;
        CREATE POLICY {PolicyName} ON {HelloPosDbContext.Schema}.{table}
          USING ({column} = {HelloPosDbContext.Schema}.current_business_id())
          WITH CHECK ({column} = {HelloPosDbContext.Schema}.current_business_id());
        """;
}
