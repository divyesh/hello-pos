using System;
using HelloPos.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HelloPos.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "revision",
                schema: "pos",
                table: "tax_profiles",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "revision",
                schema: "pos",
                table: "products",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "revision",
                schema: "pos",
                table: "item_discounts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "revision",
                schema: "pos",
                table: "categories",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "catalog_removals",
                schema: "pos",
                columns: table => new
                {
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    entity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_catalog_removals", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_catalog_removals_entity", "entity IN ('item_discount')");
                    table.ForeignKey(
                        name: "fk_catalog_removals_businesses_business_id",
                        column: x => x.business_id,
                        principalSchema: "pos",
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tax_profiles_business_id_revision",
                schema: "pos",
                table: "tax_profiles",
                columns: new[] { "business_id", "revision" });

            migrationBuilder.CreateIndex(
                name: "ix_products_business_id_revision",
                schema: "pos",
                table: "products",
                columns: new[] { "business_id", "revision" });

            migrationBuilder.CreateIndex(
                name: "ix_item_discounts_business_id_revision",
                schema: "pos",
                table: "item_discounts",
                columns: new[] { "business_id", "revision" });

            migrationBuilder.CreateIndex(
                name: "ix_categories_business_id_revision",
                schema: "pos",
                table: "categories",
                columns: new[] { "business_id", "revision" });

            migrationBuilder.CreateIndex(
                name: "ix_catalog_removals_business_id_revision",
                schema: "pos",
                table: "catalog_removals",
                columns: new[] { "business_id", "revision" });

            migrationBuilder.EnableBusinessRowSecurity("catalog_removals");
            migrationBuilder.Sql(CatalogRevisionSql.AppendOnlyRemovals);
            migrationBuilder.Sql(CatalogRevisionSql.Functions);
            migrationBuilder.Sql(CatalogRevisionSql.Triggers);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(CatalogRevisionSql.Drop);
            migrationBuilder.DropTable(
                name: "catalog_removals",
                schema: "pos");

            migrationBuilder.DropIndex(
                name: "ix_tax_profiles_business_id_revision",
                schema: "pos",
                table: "tax_profiles");

            migrationBuilder.DropIndex(
                name: "ix_products_business_id_revision",
                schema: "pos",
                table: "products");

            migrationBuilder.DropIndex(
                name: "ix_item_discounts_business_id_revision",
                schema: "pos",
                table: "item_discounts");

            migrationBuilder.DropIndex(
                name: "ix_categories_business_id_revision",
                schema: "pos",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "revision",
                schema: "pos",
                table: "tax_profiles");

            migrationBuilder.DropColumn(
                name: "revision",
                schema: "pos",
                table: "products");

            migrationBuilder.DropColumn(
                name: "revision",
                schema: "pos",
                table: "item_discounts");

            migrationBuilder.DropColumn(
                name: "revision",
                schema: "pos",
                table: "categories");
        }
    }
}
