using System;
using HelloPos.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HelloPos.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "cash_rounding_step",
                schema: "pos",
                table: "businesses",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "business_owners",
                schema: "pos",
                columns: table => new
                {
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_business_owners", x => new { x.business_id, x.id });
                    table.ForeignKey(
                        name: "fk_business_owners_businesses_business_id",
                        column: x => x.business_id,
                        principalSchema: "pos",
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_businesses_cash_rounding_step",
                schema: "pos",
                table: "businesses",
                sql: "cash_rounding_step IN (0, 0.05, 0.10, 1.00)");

            migrationBuilder.CreateIndex(
                name: "ix_business_owners_business_id_owner_id",
                schema: "pos",
                table: "business_owners",
                columns: new[] { "business_id", "owner_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_business_owners_owner_id",
                schema: "pos",
                table: "business_owners",
                column: "owner_id");

            // Owner scope (research R6): a signed-in owner can read their ownership links and the
            // businesses they own; every write still needs a session acting as the business.
            migrationBuilder.CreateOwnerScope();
            migrationBuilder.EnableBusinessRowSecurityWithOwnerRead("business_owners");
            migrationBuilder.AddBusinessesOwnerRead();
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY owner_read ON pos.businesses;");
            migrationBuilder.Sql("DROP POLICY owner_read ON pos.business_owners;");
            migrationBuilder.DisableBusinessRowSecurity("business_owners");
            migrationBuilder.Sql("DROP FUNCTION pos.current_owner_id();");

            migrationBuilder.DropTable(
                name: "business_owners",
                schema: "pos");

            migrationBuilder.DropCheckConstraint(
                name: "ck_businesses_cash_rounding_step",
                schema: "pos",
                table: "businesses");

            migrationBuilder.DropColumn(
                name: "cash_rounding_step",
                schema: "pos",
                table: "businesses");
        }
    }
}
