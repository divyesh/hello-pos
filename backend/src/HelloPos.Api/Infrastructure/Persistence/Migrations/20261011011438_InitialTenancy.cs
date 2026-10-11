using System;
using HelloPos.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HelloPos.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialTenancy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "pos");

            // Tenancy foundation: pos.current_business_id() and pos_app's default data rights.
            migrationBuilder.CreateTenancyFoundation();

            migrationBuilder.CreateTable(
                name: "businesses",
                schema: "pos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    country_code = table.Column<string>(type: "char(2)", nullable: false),
                    currency_code = table.Column<string>(type: "char(3)", nullable: false),
                    pricing_mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    financial_year_start_month = table.Column<short>(type: "smallint", nullable: false),
                    time_zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_businesses", x => x.id);
                    table.CheckConstraint("ck_businesses_country_code", "country_code IN ('IN', 'US', 'CA')");
                    table.CheckConstraint("ck_businesses_currency_code", "currency_code ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_businesses_financial_year_start_month", "financial_year_start_month BETWEEN 1 AND 12");
                    table.CheckConstraint("ck_businesses_name", "char_length(name) BETWEEN 1 AND 200");
                    table.CheckConstraint("ck_businesses_pricing_mode", "pricing_mode IN ('tax_inclusive', 'tax_exclusive')");
                });

            // A business row is visible and writable only to a session acting as that business.
            migrationBuilder.EnableBusinessRowSecurity("businesses", "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DisableBusinessRowSecurity("businesses");
            migrationBuilder.DropTable(
                name: "businesses",
                schema: "pos");
        }
    }
}
