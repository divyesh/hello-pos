namespace HelloPos.Api.Infrastructure.Persistence;

/// <summary>SQL for migration <c>AddBusinessSettings</c> (feature #12, data-model.md, research R2, R6).</summary>
public static class SettingsMigrationSql
{
    private const string Pos = HelloPosDbContext.Schema;

    /// <summary>The settings tables, all under row-level security.</summary>
    public static readonly IReadOnlyList<string> Tables =
        ["payment_types", "table_areas", "dining_tables", "selling_settings", "settings_changes"];

    /// <summary>The lists tills download, stamped like catalog roots (feature #13 research R1).</summary>
    public static readonly IReadOnlyList<string> TillLists = ["payment_types", "table_areas", "dining_tables"];

    public static readonly string Triggers = string.Join('\n', TillLists.Select(t =>
        $"CREATE TRIGGER tr_{t}_revision BEFORE INSERT OR UPDATE ON {Pos}.{t} FOR EACH ROW EXECUTE FUNCTION {Pos}.stamp_catalog_revision();"));

    public static readonly string DropTriggers = string.Join('\n', TillLists.Select(t => $"DROP TRIGGER IF EXISTS tr_{t}_revision ON {Pos}.{t};"));

    public const string AppendOnlyChanges = $"REVOKE UPDATE, DELETE ON {Pos}.settings_changes FROM {Migrator.AppGroupRole};";

    /// <summary>Every existing business gets its Cash and its selling settings (0% limit, no tables, no tips).</summary>
    public const string Backfill = $"""
        INSERT INTO {Pos}.payment_types (business_id, id, name, normalized_name, kind, built_in, opens_drawer, asks_reference, position, active)
          SELECT id, uuidv7(), 'Cash', 'cash', 'cash', true, true, false, 0, true FROM {Pos}.businesses;
        INSERT INTO {Pos}.selling_settings (business_id, id, uses_tables, tips_enabled) SELECT id, id, false, false FROM {Pos}.businesses;
        """;
}
