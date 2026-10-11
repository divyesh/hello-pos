namespace HelloPos.Api.Infrastructure.Persistence;

/// <summary>SQL for migration <c>AddDevicesAndSessions</c> (feature 007, data-model.md, research R1).</summary>
public static class DeviceMigrationSql
{
    private const string Pos = HelloPosDbContext.Schema;

    /// <summary>
    /// Finds the business of an unused, unexpired pairing code from its hash, for a device that has no
    /// credential yet. Runs as its owner (the migrator, which owns the table, so row-level security does
    /// not hide the row); returns nothing but the business id.
    /// </summary>
    public const string PairingLookupFunction = $"""
        CREATE FUNCTION {Pos}.pairing_business(p_code_hash bytea) RETURNS uuid
          LANGUAGE sql STABLE SECURITY DEFINER
          SET search_path = pg_catalog, pg_temp
          AS $$
            SELECT p.business_id FROM {Pos}.device_pairings p
            WHERE p.code_hash = p_code_hash AND p.used_at IS NULL AND p.expires_at > now()
          $$;
        REVOKE ALL ON FUNCTION {Pos}.pairing_business(bytea) FROM PUBLIC;
        GRANT EXECUTE ON FUNCTION {Pos}.pairing_business(bytea) TO {Migrator.AppGroupRole};
        """;

    public const string DropPairingLookupFunction = $"DROP FUNCTION {Pos}.pairing_business(bytea);";
}
