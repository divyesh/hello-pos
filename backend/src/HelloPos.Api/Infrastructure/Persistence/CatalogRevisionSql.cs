namespace HelloPos.Api.Infrastructure.Persistence;

/// <summary>
/// SQL for migration <c>AddCatalogRevisions</c> (feature #13, research R1). Every change to a catalog root
/// (tax profile, category, product, item discount) or to one of its children (tax version, tax component,
/// barcode) stamps the root's <c>revision</c> with the id of the writing transaction; removed discounts
/// are recorded in <c>catalog_removals</c>. A till asks for rows with <c>revision</c> â‰¥ the oldest
/// transaction still running when it last read (<see cref="ReadPosition"/>), so a change that commits
/// after a read is never skipped. Triggers, not application code, so no slice can forget.
/// </summary>
public static class CatalogRevisionSql
{
    private const string Pos = HelloPosDbContext.Schema;

    /// <summary>The catalog roots that carry a revision.</summary>
    public static readonly IReadOnlyList<string> Roots = ["tax_profiles", "categories", "products", "item_discounts"];

    /// <summary>The till's next <c>since</c>: no transaction below it can still commit (read in the sync's snapshot).</summary>
    public const string ReadPosition =
        """SELECT pg_snapshot_xmin(pg_current_snapshot())::text::bigint AS "Value" """;

    /// <summary>Above this, a <c>since</c> cannot have come from this database (restored or another server).</summary>
    public const string ReadLimit =
        """SELECT pg_snapshot_xmax(pg_current_snapshot())::text::bigint AS "Value" """;

    public const string Functions = $$"""
        CREATE FUNCTION {{Pos}}.catalog_xid() RETURNS bigint
          LANGUAGE sql VOLATILE
          AS $$ SELECT pg_current_xact_id()::text::bigint $$;

        CREATE FUNCTION {{Pos}}.stamp_catalog_revision() RETURNS trigger
          LANGUAGE plpgsql
          AS $$ BEGIN NEW.revision := {{Pos}}.catalog_xid(); RETURN NEW; END $$;

        CREATE FUNCTION {{Pos}}.touch_tax_profile_of_version() RETURNS trigger
          LANGUAGE plpgsql
          AS $$
          DECLARE r {{Pos}}.tax_profile_versions;
          BEGIN
            IF TG_OP = 'DELETE' THEN r := OLD; ELSE r := NEW; END IF;
            UPDATE {{Pos}}.tax_profiles SET revision = {{Pos}}.catalog_xid()
              WHERE business_id = r.business_id AND id = r.tax_profile_id;
            RETURN NULL;
          END $$;

        CREATE FUNCTION {{Pos}}.touch_tax_profile_of_component() RETURNS trigger
          LANGUAGE plpgsql
          AS $$
          DECLARE r {{Pos}}.tax_components;
          BEGIN
            IF TG_OP = 'DELETE' THEN r := OLD; ELSE r := NEW; END IF;
            UPDATE {{Pos}}.tax_profiles p SET revision = {{Pos}}.catalog_xid()
              FROM {{Pos}}.tax_profile_versions v
              WHERE v.business_id = r.business_id AND v.id = r.tax_profile_version_id
                AND p.business_id = v.business_id AND p.id = v.tax_profile_id;
            RETURN NULL;
          END $$;

        CREATE FUNCTION {{Pos}}.touch_product_of_barcode() RETURNS trigger
          LANGUAGE plpgsql
          AS $$
          DECLARE r {{Pos}}.product_barcodes;
          BEGIN
            IF TG_OP = 'DELETE' THEN r := OLD; ELSE r := NEW; END IF;
            UPDATE {{Pos}}.products SET revision = {{Pos}}.catalog_xid()
              WHERE business_id = r.business_id AND id = r.product_id;
            RETURN NULL;
          END $$;

        CREATE FUNCTION {{Pos}}.record_discount_removal() RETURNS trigger
          LANGUAGE plpgsql
          AS $$
          BEGIN
            INSERT INTO {{Pos}}.catalog_removals (business_id, entity, entity_id, revision)
              VALUES (OLD.business_id, 'item_discount', OLD.id, {{Pos}}.catalog_xid());
            RETURN NULL;
          END $$;

        GRANT EXECUTE ON FUNCTION {{Pos}}.catalog_xid() TO {{Migrator.AppGroupRole}};
        """;

    public static readonly string Triggers = string.Join('\n', Roots.Select(t =>
        $"CREATE TRIGGER tr_{t}_revision BEFORE INSERT OR UPDATE ON {Pos}.{t} FOR EACH ROW EXECUTE FUNCTION {Pos}.stamp_catalog_revision();"))
        + $"""

        CREATE TRIGGER tr_tax_profile_versions_touch AFTER INSERT OR UPDATE OR DELETE ON {Pos}.tax_profile_versions
          FOR EACH ROW EXECUTE FUNCTION {Pos}.touch_tax_profile_of_version();
        CREATE TRIGGER tr_tax_components_touch AFTER INSERT OR UPDATE OR DELETE ON {Pos}.tax_components
          FOR EACH ROW EXECUTE FUNCTION {Pos}.touch_tax_profile_of_component();
        CREATE TRIGGER tr_product_barcodes_touch AFTER INSERT OR UPDATE OR DELETE ON {Pos}.product_barcodes
          FOR EACH ROW EXECUTE FUNCTION {Pos}.touch_product_of_barcode();
        CREATE TRIGGER tr_item_discounts_removed AFTER DELETE ON {Pos}.item_discounts
          FOR EACH ROW EXECUTE FUNCTION {Pos}.record_discount_removal();
        """;

    public const string AppendOnlyRemovals = $"REVOKE UPDATE, DELETE ON {Pos}.catalog_removals FROM {Migrator.AppGroupRole};";

    public static readonly string Drop = string.Join('\n', Roots.Select(t => $"DROP TRIGGER IF EXISTS tr_{t}_revision ON {Pos}.{t};"))
        + $"""

        DROP TRIGGER IF EXISTS tr_tax_profile_versions_touch ON {Pos}.tax_profile_versions;
        DROP TRIGGER IF EXISTS tr_tax_components_touch ON {Pos}.tax_components;
        DROP TRIGGER IF EXISTS tr_product_barcodes_touch ON {Pos}.product_barcodes;
        DROP TRIGGER IF EXISTS tr_item_discounts_removed ON {Pos}.item_discounts;
        DROP FUNCTION IF EXISTS {Pos}.record_discount_removal();
        DROP FUNCTION IF EXISTS {Pos}.touch_product_of_barcode();
        DROP FUNCTION IF EXISTS {Pos}.touch_tax_profile_of_component();
        DROP FUNCTION IF EXISTS {Pos}.touch_tax_profile_of_version();
        DROP FUNCTION IF EXISTS {Pos}.stamp_catalog_revision();
        DROP FUNCTION IF EXISTS {Pos}.catalog_xid();
        """;
}
