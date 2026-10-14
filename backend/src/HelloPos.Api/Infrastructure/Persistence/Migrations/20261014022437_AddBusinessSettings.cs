using System;
using HelloPos.Api.Infrastructure.Persistence;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HelloPos.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payment_types",
                schema: "pos",
                columns: table => new
                {
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    kind = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    built_in = table.Column<bool>(type: "boolean", nullable: false),
                    opens_drawer = table.Column<bool>(type: "boolean", nullable: false),
                    asks_reference = table.Column<bool>(type: "boolean", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    revision = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_types", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_payment_types_built_in_active", "NOT built_in OR active");
                    table.CheckConstraint("ck_payment_types_cash_is_built_in", "(kind = 'cash') = built_in");
                    table.CheckConstraint("ck_payment_types_kind", "kind IN ('cash', 'external_card', 'digital_wallet', 'cheque', 'voucher', 'other')");
                    table.CheckConstraint("ck_payment_types_name", "char_length(name) BETWEEN 1 AND 40");
                    table.ForeignKey(
                        name: "fk_payment_types_businesses_business_id",
                        column: x => x.business_id,
                        principalSchema: "pos",
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "selling_settings",
                schema: "pos",
                columns: table => new
                {
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    uses_tables = table.Column<bool>(type: "boolean", nullable: false),
                    discount_limit_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false, defaultValue: 0m),
                    discount_limit_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    tips_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    tip_presets = table.Column<List<decimal>>(type: "numeric(5,2)[]", nullable: false, defaultValueSql: "'{}'"),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_selling_settings", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_selling_settings_limit_amount", "discount_limit_amount IS NULL OR discount_limit_amount >= 0");
                    table.CheckConstraint("ck_selling_settings_limit_percent", "discount_limit_percent BETWEEN 0 AND 100");
                    table.CheckConstraint("ck_selling_settings_one_per_business", "id = business_id");
                    table.CheckConstraint("ck_selling_settings_tip_presets", "cardinality(tip_presets) <= 3");
                    table.ForeignKey(
                        name: "fk_selling_settings_businesses_business_id",
                        column: x => x.business_id,
                        principalSchema: "pos",
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "settings_changes",
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
                    changes = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_settings_changes", x => new { x.business_id, x.id });
                    table.ForeignKey(
                        name: "fk_settings_changes_businesses_business_id",
                        column: x => x.business_id,
                        principalSchema: "pos",
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_settings_changes_staff_members_business_id_actor_staff_id",
                        columns: x => new { x.business_id, x.actor_staff_id },
                        principalSchema: "pos",
                        principalTable: "staff_members",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "table_areas",
                schema: "pos",
                columns: table => new
                {
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    revision = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("ak_table_areas_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_table_areas_name", "char_length(name) BETWEEN 1 AND 40");
                    table.ForeignKey(
                        name: "fk_table_areas_businesses_business_id",
                        column: x => x.business_id,
                        principalSchema: "pos",
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dining_tables",
                schema: "pos",
                columns: table => new
                {
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    area_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    seats = table.Column<short>(type: "smallint", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    revision = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dining_tables", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_dining_tables_name", "char_length(name) BETWEEN 1 AND 20");
                    table.CheckConstraint("ck_dining_tables_seats", "seats BETWEEN 1 AND 50");
                    table.ForeignKey(
                        name: "fk_dining_tables_businesses_business_id",
                        column: x => x.business_id,
                        principalSchema: "pos",
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dining_tables_table_areas_business_id_area_id",
                        columns: x => new { x.business_id, x.area_id },
                        principalSchema: "pos",
                        principalTable: "table_areas",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_dining_tables_business_id_area_id",
                schema: "pos",
                table: "dining_tables",
                columns: new[] { "business_id", "area_id" });

            migrationBuilder.CreateIndex(
                name: "ix_dining_tables_business_id_normalized_name",
                schema: "pos",
                table: "dining_tables",
                columns: new[] { "business_id", "normalized_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dining_tables_business_id_revision",
                schema: "pos",
                table: "dining_tables",
                columns: new[] { "business_id", "revision" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_types_business_id_normalized_name",
                schema: "pos",
                table: "payment_types",
                columns: new[] { "business_id", "normalized_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_types_business_id_revision",
                schema: "pos",
                table: "payment_types",
                columns: new[] { "business_id", "revision" });

            migrationBuilder.CreateIndex(
                name: "ux_payment_types_one_built_in",
                schema: "pos",
                table: "payment_types",
                column: "business_id",
                unique: true,
                filter: "built_in");

            migrationBuilder.CreateIndex(
                name: "ix_settings_changes_business_id_actor_staff_id",
                schema: "pos",
                table: "settings_changes",
                columns: new[] { "business_id", "actor_staff_id" });

            migrationBuilder.CreateIndex(
                name: "ix_settings_changes_business_id_entity_id_at",
                schema: "pos",
                table: "settings_changes",
                columns: new[] { "business_id", "entity_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_table_areas_business_id_normalized_name",
                schema: "pos",
                table: "table_areas",
                columns: new[] { "business_id", "normalized_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_table_areas_business_id_revision",
                schema: "pos",
                table: "table_areas",
                columns: new[] { "business_id", "revision" });

            migrationBuilder.Sql(SettingsMigrationSql.Backfill);
            foreach (var table in SettingsMigrationSql.Tables)
            {
                migrationBuilder.EnableBusinessRowSecurity(table);
            }

            migrationBuilder.Sql(SettingsMigrationSql.AppendOnlyChanges);
            migrationBuilder.Sql(SettingsMigrationSql.Triggers);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(SettingsMigrationSql.DropTriggers);
            migrationBuilder.DropTable(
                name: "dining_tables",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "payment_types",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "selling_settings",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "settings_changes",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "table_areas",
                schema: "pos");
        }
    }
}
