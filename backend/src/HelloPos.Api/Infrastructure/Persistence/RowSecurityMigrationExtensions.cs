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

    /// <summary>The signed-in owner (feature 005, research R6).</summary>
    public const string OwnerSessionSetting = "pos.owner_id";

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

    /// <summary>Feature 005: <c>pos.current_owner_id()</c>, the signed-in owner of the session.</summary>
    public static readonly string OwnerScopeFunctionSql = $"""
        CREATE OR REPLACE FUNCTION {HelloPosDbContext.Schema}.current_owner_id() RETURNS uuid
          LANGUAGE sql STABLE
          AS $$ SELECT NULLIF(current_setting('{OwnerSessionSetting}', true), '')::uuid $$;
        GRANT EXECUTE ON FUNCTION {HelloPosDbContext.Schema}.current_owner_id() TO {Migrator.AppGroupRole};
        """;

    public static void CreateOwnerScope(this MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.Sql(OwnerScopeFunctionSql);
    }

    /// <summary>Read-only policy name for the signed-in owner (feature 005).</summary>
    public const string OwnerReadPolicyName = "owner_read";

    /// <summary>
    /// The feature 003 business policy, plus a read-only policy for the signed-in owner named in
    /// <paramref name="ownerColumn"/>. Writes still need the session's business, so an owner session
    /// cannot plant a row (for example an ownership link) in a business it is not acting as.
    /// </summary>
    public static void EnableBusinessRowSecurityWithOwnerRead(this MigrationBuilder migrationBuilder, string table, string ownerColumn = "owner_id")
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        var schema = HelloPosDbContext.Schema;
        migrationBuilder.Sql(EnableSql(table));
        migrationBuilder.Sql($"""
            CREATE POLICY {OwnerReadPolicyName} ON {schema}.{table} FOR SELECT
              USING ({ownerColumn} = {schema}.current_owner_id());
            """);
    }

    /// <summary>
    /// Lets the signed-in owner read the businesses they own (through <c>business_owners</c>) before
    /// opening one (FR-012). Changing a business still needs a session acting as it (research R6).
    /// </summary>
    public static void AddBusinessesOwnerRead(this MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        var schema = HelloPosDbContext.Schema;
        migrationBuilder.Sql($"""
            CREATE POLICY {OwnerReadPolicyName} ON {schema}.businesses FOR SELECT
              USING (EXISTS (SELECT 1 FROM {schema}.business_owners bo
                             WHERE bo.business_id = businesses.id AND bo.owner_id = {schema}.current_owner_id()));
            """);
    }

    /// <summary>
    /// Feature 006: the businesses' <c>owner_read</c> policy based on staff. The signed-in account reads
    /// a business where it is an active owner or manager; a cashier row grants nothing. Replaces
    /// <see cref="AddBusinessesOwnerRead"/> (kept unchanged because migration history replays it).
    /// </summary>
    public static void ReplaceBusinessesOwnerReadWithStaff(this MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        var schema = HelloPosDbContext.Schema;
        migrationBuilder.Sql($"""
            DROP POLICY {OwnerReadPolicyName} ON {schema}.businesses;
            CREATE POLICY {OwnerReadPolicyName} ON {schema}.businesses FOR SELECT
              USING (EXISTS (SELECT 1 FROM {schema}.staff_members s
                             WHERE s.business_id = businesses.id
                               AND s.account_id = {schema}.current_owner_id()
                               AND s.active
                               AND s.role IN ('owner', 'manager')));
            """);
    }
}
