namespace HelloPos.Api.Infrastructure.Persistence;

/// <summary>SQL for migration <c>AddCatalog</c> (feature #11, data-model.md, research R6, R7, R10).</summary>
public static class CatalogMigrationSql
{
    private const string Pos = HelloPosDbContext.Schema;

    /// <summary>The catalog's business-owned tables, all under row-level security.</summary>
    public static readonly IReadOnlyList<string> Tables =
    [
        "tax_profiles", "tax_profile_versions", "tax_components", "categories", "products",
        "product_barcodes", "item_discounts", "catalog_changes", "catalog_imports",
    ];

    /// <summary>Trusted contrib extensions (PostgreSQL 13+): trigram search and gist on equality types.</summary>
    public const string Extensions = """
        CREATE EXTENSION IF NOT EXISTS pg_trgm;
        CREATE EXTENSION IF NOT EXISTS btree_gist;
        """;

    /// <summary>Finds products by any part of the name (research R7).</summary>
    public const string NameSearchIndex =
        $"CREATE INDEX ix_products_name_trgm ON {Pos}.products USING gin (lower(name) gin_trgm_ops);";

    /// <summary>Two discounts of one product may never cover the same day, even when saved at once (research R6).</summary>
    public const string NoOverlappingDiscounts = $"""
        ALTER TABLE {Pos}.item_discounts ADD CONSTRAINT ex_item_discounts_no_overlap
          EXCLUDE USING gist (business_id WITH =, product_id WITH =, daterange(from_date, to_date, '[]') WITH &&);
        """;

    public const string AppendOnlyChanges = $"REVOKE UPDATE, DELETE ON {Pos}.catalog_changes FROM {Migrator.AppGroupRole};";

    public const string DropNameSearchIndex = $"DROP INDEX IF EXISTS {Pos}.ix_products_name_trgm;";

    public const string DropNoOverlappingDiscounts = $"ALTER TABLE {Pos}.item_discounts DROP CONSTRAINT IF EXISTS ex_item_discounts_no_overlap;";
}
