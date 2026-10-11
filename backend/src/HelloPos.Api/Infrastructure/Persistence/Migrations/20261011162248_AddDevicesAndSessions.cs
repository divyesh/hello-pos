using System;
using HelloPos.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HelloPos.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDevicesAndSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "pin_failed_count",
                schema: "pos",
                table: "staff_members",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "pin_locked_until",
                schema: "pos",
                table: "staff_members",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "device_id",
                schema: "pos",
                table: "staff_changes",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "devices",
                schema: "pos",
                columns: table => new
                {
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    credential_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    public_key = table.Column<byte[]>(type: "bytea", nullable: false),
                    registered_by_staff_id = table.Column<Guid>(type: "uuid", nullable: false),
                    registered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by_staff_id = table.Column<Guid>(type: "uuid", nullable: true),
                    failed_signins = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_devices", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_devices_name", "char_length(name) BETWEEN 1 AND 60");
                    table.ForeignKey(
                        name: "fk_devices_businesses_business_id",
                        column: x => x.business_id,
                        principalSchema: "pos",
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_devices_staff_members_business_id_registered_by_staff_id",
                        columns: x => new { x.business_id, x.registered_by_staff_id },
                        principalSchema: "pos",
                        principalTable: "staff_members",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_devices_staff_members_business_id_revoked_by_staff_id",
                        columns: x => new { x.business_id, x.revoked_by_staff_id },
                        principalSchema: "pos",
                        principalTable: "staff_members",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "device_pairings",
                schema: "pos",
                columns: table => new
                {
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_staff_id = table.Column<Guid>(type: "uuid", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_pairings", x => new { x.business_id, x.id });
                    table.ForeignKey(
                        name: "fk_device_pairings_businesses_business_id",
                        column: x => x.business_id,
                        principalSchema: "pos",
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_device_pairings_devices_business_id_device_id",
                        columns: x => new { x.business_id, x.device_id },
                        principalSchema: "pos",
                        principalTable: "devices",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_device_pairings_staff_members_business_id_created_by_staff_",
                        columns: x => new { x.business_id, x.created_by_staff_id },
                        principalSchema: "pos",
                        principalTable: "staff_members",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "staff_sessions",
                schema: "pos",
                columns: table => new
                {
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    staff_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    access_token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    access_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    refresh_token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    previous_refresh_token_hash = table.Column<byte[]>(type: "bytea", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_active_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    offline_signed_in_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    end_reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staff_sessions", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_staff_sessions_end_reason", "end_reason IS NULL OR end_reason IN ('sign_out', 'refresh_reuse', 'pin_changed', 'deactivated', 'device_revoked', 'expired')");
                    table.ForeignKey(
                        name: "fk_staff_sessions_businesses_business_id",
                        column: x => x.business_id,
                        principalSchema: "pos",
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_staff_sessions_devices_business_id_device_id",
                        columns: x => new { x.business_id, x.device_id },
                        principalSchema: "pos",
                        principalTable: "devices",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_staff_sessions_staff_members_business_id_staff_member_id",
                        columns: x => new { x.business_id, x.staff_member_id },
                        principalSchema: "pos",
                        principalTable: "staff_members",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_staff_changes_business_id_device_id",
                schema: "pos",
                table: "staff_changes",
                columns: new[] { "business_id", "device_id" });

            migrationBuilder.CreateIndex(
                name: "ix_device_pairings_business_id_created_by_staff_id",
                schema: "pos",
                table: "device_pairings",
                columns: new[] { "business_id", "created_by_staff_id" });

            migrationBuilder.CreateIndex(
                name: "ix_device_pairings_business_id_device_id",
                schema: "pos",
                table: "device_pairings",
                columns: new[] { "business_id", "device_id" });

            migrationBuilder.CreateIndex(
                name: "ix_device_pairings_code_hash",
                schema: "pos",
                table: "device_pairings",
                column: "code_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_devices_business_id_registered_by_staff_id",
                schema: "pos",
                table: "devices",
                columns: new[] { "business_id", "registered_by_staff_id" });

            migrationBuilder.CreateIndex(
                name: "ix_devices_business_id_revoked_by_staff_id",
                schema: "pos",
                table: "devices",
                columns: new[] { "business_id", "revoked_by_staff_id" });

            migrationBuilder.CreateIndex(
                name: "ix_devices_credential_hash",
                schema: "pos",
                table: "devices",
                column: "credential_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_staff_sessions_access_token_hash",
                schema: "pos",
                table: "staff_sessions",
                column: "access_token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_staff_sessions_business_id_device_id",
                schema: "pos",
                table: "staff_sessions",
                columns: new[] { "business_id", "device_id" });

            migrationBuilder.CreateIndex(
                name: "ix_staff_sessions_business_id_staff_member_id",
                schema: "pos",
                table: "staff_sessions",
                columns: new[] { "business_id", "staff_member_id" });

            migrationBuilder.CreateIndex(
                name: "ix_staff_sessions_refresh_token_hash",
                schema: "pos",
                table: "staff_sessions",
                column: "refresh_token_hash",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_staff_changes_devices_business_id_device_id",
                schema: "pos",
                table: "staff_changes",
                columns: new[] { "business_id", "device_id" },
                principalSchema: "pos",
                principalTable: "devices",
                principalColumns: new[] { "business_id", "id" },
                onDelete: ReferentialAction.Restrict);

            // Each table is visible only to the session's business; the pairing lookup is the one way in
            // for a device that has no credential yet (research R1).
            migrationBuilder.EnableBusinessRowSecurity("devices");
            migrationBuilder.EnableBusinessRowSecurity("device_pairings");
            migrationBuilder.EnableBusinessRowSecurity("staff_sessions");
            migrationBuilder.Sql(DeviceMigrationSql.PairingLookupFunction);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DeviceMigrationSql.DropPairingLookupFunction);

            migrationBuilder.DropForeignKey(
                name: "fk_staff_changes_devices_business_id_device_id",
                schema: "pos",
                table: "staff_changes");

            migrationBuilder.DropTable(
                name: "device_pairings",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "staff_sessions",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "devices",
                schema: "pos");

            migrationBuilder.DropIndex(
                name: "ix_staff_changes_business_id_device_id",
                schema: "pos",
                table: "staff_changes");

            migrationBuilder.DropColumn(
                name: "pin_failed_count",
                schema: "pos",
                table: "staff_members");

            migrationBuilder.DropColumn(
                name: "pin_locked_until",
                schema: "pos",
                table: "staff_members");

            migrationBuilder.DropColumn(
                name: "device_id",
                schema: "pos",
                table: "staff_changes");
        }
    }
}
