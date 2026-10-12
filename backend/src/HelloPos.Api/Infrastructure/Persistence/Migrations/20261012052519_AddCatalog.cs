using System;
using HelloPos.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HelloPos.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "deposits_taxable",
                schema: "pos",
                table: "businesses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "catalog_imports",
                schema: "pos",
                columns: table => new
                {
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    actor_staff_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    rows = table.Column<int>(type: "integer", nullable: false),
                    created = table.Column<int>(type: "integer", nullable: false),
                    updated = table.Column<int>(type: "integer", nullable: false),
                    unchanged = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("ak_catalog_imports_business_id_id", x => new { x.business_id, x.id });
                    table.ForeignKey(
                        name: "fk_catalog_imports_businesses_business_id",
                        column: x => x.business_id,
                        principalSchema: "pos",
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_catalog_imports_staff_members_business_id_actor_staff_id",
                        columns: x => new { x.business_id, x.actor_staff_id },
                        principalSchema: "pos",
                        principalTable: "staff_members",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tax_profiles",
                schema: "pos",
                columns: table => new
                {
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    kind = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("ak_tax_profiles_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_tax_profiles_kind", "kind IN ('taxable', 'zero_rated', 'exempt')");
                    table.CheckConstraint("ck_tax_profiles_name", "char_length(name) BETWEEN 1 AND 60");
                    table.ForeignKey(
                        name: "fk_tax_profiles_businesses_business_id",
                        column: x => x.business_id,
                        principalSchema: "pos",
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "catalog_changes",
                schema: "pos",
                columns: table => new
                {
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    actor_staff_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    changes = table.Column<string>(type: "jsonb", nullable: false),
                    import_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_catalog_changes", x => new { x.business_id, x.id });
                    table.ForeignKey(
                        name: "fk_catalog_changes_businesses_business_id",
                        column: x => x.business_id,
                        principalSchema: "pos",
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_catalog_changes_catalog_imports_business_id_import_id",
                        columns: x => new { x.business_id, x.import_id },
                        principalSchema: "pos",
                        principalTable: "catalog_imports",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_catalog_changes_staff_members_business_id_actor_staff_id",
                        columns: x => new { x.business_id, x.actor_staff_id },
                        principalSchema: "pos",
                        principalTable: "staff_members",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "categories",
                schema: "pos",
                columns: table => new
                {
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    default_tax_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_categories", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_categories_name", "char_length(name) BETWEEN 1 AND 60");
                    table.ForeignKey(
                        name: "fk_categories_businesses_business_id",
                        column: x => x.business_id,
                        principalSchema: "pos",
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_categories_tax_profiles_business_id_default_tax_profile_id",
                        columns: x => new { x.business_id, x.default_tax_profile_id },
                        principalSchema: "pos",
                        principalTable: "tax_profiles",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tax_profile_versions",
                schema: "pos",
                columns: table => new
                {
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tax_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("ak_tax_profile_versions_business_id_id", x => new { x.business_id, x.id });
                    table.ForeignKey(
                        name: "fk_tax_profile_versions_businesses_business_id",
                        column: x => x.business_id,
                        principalSchema: "pos",
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_profile_versions_tax_profiles_business_id_tax_profile_id",
                        columns: x => new { x.business_id, x.tax_profile_id },
                        principalSchema: "pos",
                        principalTable: "tax_profiles",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "products",
                schema: "pos",
                columns: table => new
                {
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    sku = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    normalized_sku = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tax_profile_id = table.Column<Guid>(type: "uuid", nullable: true),
                    unit = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    open_price = table.Column<bool>(type: "boolean", nullable: false),
                    min_quantity = table.Column<decimal>(type: "numeric(9,3)", precision: 9, scale: 3, nullable: false),
                    step = table.Column<decimal>(type: "numeric(9,3)", precision: 9, scale: 3, nullable: false),
                    min_age = table.Column<short>(type: "smallint", nullable: true),
                    deposit_per_unit = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("ak_products_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_products_deposit", "deposit_per_unit >= 0");
                    table.CheckConstraint("ck_products_min_age", "min_age IS NULL OR min_age BETWEEN 1 AND 99");
                    table.CheckConstraint("ck_products_name", "char_length(name) BETWEEN 1 AND 120");
                    table.CheckConstraint("ck_products_price", "(open_price AND unit_price IS NULL) OR (NOT open_price AND unit_price >= 0)");
                    table.CheckConstraint("ck_products_quantities", "min_quantity > 0 AND step > 0 AND mod(min_quantity, step) = 0");
                    table.CheckConstraint("ck_products_unit", "unit IN ('piece', 'g', 'kg', 'ml', 'l')");
                    table.ForeignKey(
                        name: "fk_products_businesses_business_id",
                        column: x => x.business_id,
                        principalSchema: "pos",
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_products_categories_business_id_category_id",
                        columns: x => new { x.business_id, x.category_id },
                        principalSchema: "pos",
                        principalTable: "categories",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_products_tax_profiles_business_id_tax_profile_id",
                        columns: x => new { x.business_id, x.tax_profile_id },
                        principalSchema: "pos",
                        principalTable: "tax_profiles",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tax_components",
                schema: "pos",
                columns: table => new
                {
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tax_profile_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    label = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    rate_percent = table.Column<decimal>(type: "numeric(6,3)", precision: 6, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tax_components", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_tax_components_label", "char_length(label) BETWEEN 1 AND 20");
                    table.CheckConstraint("ck_tax_components_rate", "rate_percent >= 0 AND rate_percent <= 100");
                    table.ForeignKey(
                        name: "fk_tax_components_businesses_business_id",
                        column: x => x.business_id,
                        principalSchema: "pos",
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_components_tax_profile_versions_business_id_tax_profile",
                        columns: x => new { x.business_id, x.tax_profile_version_id },
                        principalSchema: "pos",
                        principalTable: "tax_profile_versions",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "item_discounts",
                schema: "pos",
                columns: table => new
                {
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    value = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    from_date = table.Column<DateOnly>(type: "date", nullable: false),
                    to_date = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_discounts", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_item_discounts_kind", "kind IN ('percent', 'fixed')");
                    table.CheckConstraint("ck_item_discounts_period", "to_date IS NULL OR to_date >= from_date");
                    table.CheckConstraint("ck_item_discounts_value", "value >= 0 AND (kind <> 'percent' OR value <= 100)");
                    table.ForeignKey(
                        name: "fk_item_discounts_businesses_business_id",
                        column: x => x.business_id,
                        principalSchema: "pos",
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_item_discounts_products_business_id_product_id",
                        columns: x => new { x.business_id, x.product_id },
                        principalSchema: "pos",
                        principalTable: "products",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_barcodes",
                schema: "pos",
                columns: table => new
                {
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    kind = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_barcodes", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_product_barcodes_code", "code ~ '^[0-9]{4,13}$'");
                    table.CheckConstraint("ck_product_barcodes_kind", "kind IN ('ean13', 'ean8', 'plu')");
                    table.ForeignKey(
                        name: "fk_product_barcodes_businesses_business_id",
                        column: x => x.business_id,
                        principalSchema: "pos",
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_product_barcodes_products_business_id_product_id",
                        columns: x => new { x.business_id, x.product_id },
                        principalSchema: "pos",
                        principalTable: "products",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_catalog_changes_business_id_actor_staff_id",
                schema: "pos",
                table: "catalog_changes",
                columns: new[] { "business_id", "actor_staff_id" });

            migrationBuilder.CreateIndex(
                name: "ix_catalog_changes_business_id_entity_id_at",
                schema: "pos",
                table: "catalog_changes",
                columns: new[] { "business_id", "entity_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_catalog_changes_business_id_import_id",
                schema: "pos",
                table: "catalog_changes",
                columns: new[] { "business_id", "import_id" });

            migrationBuilder.CreateIndex(
                name: "ix_catalog_imports_business_id_actor_staff_id",
                schema: "pos",
                table: "catalog_imports",
                columns: new[] { "business_id", "actor_staff_id" });

            migrationBuilder.CreateIndex(
                name: "ix_categories_business_id_default_tax_profile_id",
                schema: "pos",
                table: "categories",
                columns: new[] { "business_id", "default_tax_profile_id" });

            migrationBuilder.CreateIndex(
                name: "ix_categories_business_id_normalized_name",
                schema: "pos",
                table: "categories",
                columns: new[] { "business_id", "normalized_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_item_discounts_business_id_product_id",
                schema: "pos",
                table: "item_discounts",
                columns: new[] { "business_id", "product_id" });

            migrationBuilder.CreateIndex(
                name: "ix_product_barcodes_business_id_code",
                schema: "pos",
                table: "product_barcodes",
                columns: new[] { "business_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_barcodes_business_id_product_id",
                schema: "pos",
                table: "product_barcodes",
                columns: new[] { "business_id", "product_id" });

            migrationBuilder.CreateIndex(
                name: "ix_products_business_id_category_id",
                schema: "pos",
                table: "products",
                columns: new[] { "business_id", "category_id" });

            migrationBuilder.CreateIndex(
                name: "ix_products_business_id_normalized_sku",
                schema: "pos",
                table: "products",
                columns: new[] { "business_id", "normalized_sku" },
                unique: true,
                filter: "normalized_sku IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_products_business_id_tax_profile_id",
                schema: "pos",
                table: "products",
                columns: new[] { "business_id", "tax_profile_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tax_components_business_id_tax_profile_version_id_label",
                schema: "pos",
                table: "tax_components",
                columns: new[] { "business_id", "tax_profile_version_id", "label" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tax_profile_versions_business_id_tax_profile_id_effective_f",
                schema: "pos",
                table: "tax_profile_versions",
                columns: new[] { "business_id", "tax_profile_id", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tax_profiles_business_id_normalized_name",
                schema: "pos",
                table: "tax_profiles",
                columns: new[] { "business_id", "normalized_name" },
                unique: true);

            migrationBuilder.Sql(CatalogMigrationSql.Extensions);
            migrationBuilder.Sql(CatalogMigrationSql.NameSearchIndex);
            migrationBuilder.Sql(CatalogMigrationSql.NoOverlappingDiscounts);
            foreach (var table in CatalogMigrationSql.Tables)
            {
                migrationBuilder.EnableBusinessRowSecurity(table);
            }

            migrationBuilder.Sql(CatalogMigrationSql.AppendOnlyChanges);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(CatalogMigrationSql.DropNoOverlappingDiscounts);
            migrationBuilder.Sql(CatalogMigrationSql.DropNameSearchIndex);
            foreach (var table in CatalogMigrationSql.Tables)
            {
                migrationBuilder.DisableBusinessRowSecurity(table);
            }

            migrationBuilder.DropTable(
                name: "catalog_changes",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "item_discounts",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "product_barcodes",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "tax_components",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "catalog_imports",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "products",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "tax_profile_versions",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "categories",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "tax_profiles",
                schema: "pos");

            migrationBuilder.DropColumn(
                name: "deposits_taxable",
                schema: "pos",
                table: "businesses");
        }
    }
}
