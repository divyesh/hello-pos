namespace HelloPos.Api.Infrastructure.Persistence;

/// <summary>SQL for migration <c>AddStaff</c> (feature 006, data-model.md, research R1, R7, R8).</summary>
public static class StaffMigrationSql
{
    private const string Pos = HelloPosDbContext.Schema;

    /// <summary>Every feature 005 owner becomes an active owner staff member, named after their account.</summary>
    public const string CopyOwners = $"""
        INSERT INTO {Pos}.staff_members (business_id, id, display_name, role, active, account_id, version, created_at, updated_at)
        SELECT bo.business_id, uuidv7(), a.display_name, 'owner', true, bo.owner_id, 1, bo.created_at, bo.created_at
        FROM {Pos}.business_owners bo
        JOIN identity.owner_accounts a ON a.id = bo.owner_id;
        """;

    /// <summary>The way back: active owners with an account become ownership links again.</summary>
    public const string CopyOwnersBack = $"""
        INSERT INTO {Pos}.business_owners (business_id, id, owner_id, created_at)
        SELECT business_id, uuidv7(), account_id, created_at
        FROM {Pos}.staff_members
        WHERE role = 'owner' AND active AND account_id IS NOT NULL;
        """;

    /// <summary>The audit trail is append-only for the app (Constitution IV stance).</summary>
    public const string AppendOnlyChanges = $"REVOKE UPDATE, DELETE ON {Pos}.staff_changes FROM {Migrator.AppGroupRole};";

    /// <summary>
    /// Finds the business of a pending, unexpired invitation from its token hash, for a person who has
    /// no business open yet (research R7). Runs as its owner (the migrator, which owns the table, so
    /// row-level security does not hide the row); returns nothing but the business id.
    /// </summary>
    public const string InvitationLookupFunction = $"""
        CREATE FUNCTION {Pos}.invitation_business(p_token_hash bytea) RETURNS uuid
          LANGUAGE sql STABLE SECURITY DEFINER
          SET search_path = pg_catalog, pg_temp
          AS $$
            SELECT i.business_id FROM {Pos}.staff_invitations i
            WHERE i.token_hash = p_token_hash AND i.status = 'pending' AND i.expires_at > now()
          $$;
        REVOKE ALL ON FUNCTION {Pos}.invitation_business(bytea) FROM PUBLIC;
        GRANT EXECUTE ON FUNCTION {Pos}.invitation_business(bytea) TO {Migrator.AppGroupRole};
        """;

    public const string DropInvitationLookupFunction = $"DROP FUNCTION {Pos}.invitation_business(bytea);";
}
