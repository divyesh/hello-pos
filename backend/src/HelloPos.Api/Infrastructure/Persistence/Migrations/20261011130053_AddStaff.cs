using System;
using HelloPos.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HelloPos.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStaff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "staff_members",
                schema: "pos",
                columns: table => new
                {
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    role = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pos_username = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    pin_hash = table.Column<string>(type: "text", nullable: true),
                    pin_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("ak_staff_members_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_staff_members_can_sign_in", "account_id IS NOT NULL OR pos_username IS NOT NULL");
                    table.CheckConstraint("ck_staff_members_display_name", "char_length(display_name) BETWEEN 1 AND 100");
                    table.CheckConstraint("ck_staff_members_owner_account", "role <> 'owner' OR account_id IS NOT NULL");
                    table.CheckConstraint("ck_staff_members_pos_credentials", "(pos_username IS NULL) = (pin_hash IS NULL)");
                    table.CheckConstraint("ck_staff_members_pos_username", "pos_username ~ '^[a-z0-9._-]{3,32}$'");
                    table.CheckConstraint("ck_staff_members_role", "role IN ('owner', 'manager', 'cashier')");
                    table.ForeignKey(
                        name: "fk_staff_members_businesses_business_id",
                        column: x => x.business_id,
                        principalSchema: "pos",
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "staff_invitations",
                schema: "pos",
                columns: table => new
                {
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    role = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    display_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    invited_by_staff_id = table.Column<Guid>(type: "uuid", nullable: false),
                    accepted_staff_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staff_invitations", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_staff_invitations_display_name", "char_length(display_name) BETWEEN 1 AND 100");
                    table.CheckConstraint("ck_staff_invitations_role", "role IN ('owner', 'manager')");
                    table.CheckConstraint("ck_staff_invitations_status", "status IN ('pending', 'accepted', 'revoked')");
                    table.ForeignKey(
                        name: "fk_staff_invitations_businesses_business_id",
                        column: x => x.business_id,
                        principalSchema: "pos",
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_staff_invitations_staff_members_business_id_accepted_staff_",
                        columns: x => new { x.business_id, x.accepted_staff_id },
                        principalSchema: "pos",
                        principalTable: "staff_members",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_staff_invitations_staff_members_business_id_invited_by_staf",
                        columns: x => new { x.business_id, x.invited_by_staff_id },
                        principalSchema: "pos",
                        principalTable: "staff_members",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "staff_changes",
                schema: "pos",
                columns: table => new
                {
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    staff_member_id = table.Column<Guid>(type: "uuid", nullable: true),
                    invitation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_staff_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    old_value = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    new_value = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staff_changes", x => new { x.business_id, x.id });
                    table.ForeignKey(
                        name: "fk_staff_changes_businesses_business_id",
                        column: x => x.business_id,
                        principalSchema: "pos",
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_staff_changes_staff_invitations_business_id_invitation_id",
                        columns: x => new { x.business_id, x.invitation_id },
                        principalSchema: "pos",
                        principalTable: "staff_invitations",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_staff_changes_staff_members_business_id_actor_staff_id",
                        columns: x => new { x.business_id, x.actor_staff_id },
                        principalSchema: "pos",
                        principalTable: "staff_members",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_staff_changes_staff_members_business_id_staff_member_id",
                        columns: x => new { x.business_id, x.staff_member_id },
                        principalSchema: "pos",
                        principalTable: "staff_members",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_staff_changes_business_id_actor_staff_id",
                schema: "pos",
                table: "staff_changes",
                columns: new[] { "business_id", "actor_staff_id" });

            migrationBuilder.CreateIndex(
                name: "ix_staff_changes_business_id_invitation_id",
                schema: "pos",
                table: "staff_changes",
                columns: new[] { "business_id", "invitation_id" });

            migrationBuilder.CreateIndex(
                name: "ix_staff_changes_business_id_staff_member_id",
                schema: "pos",
                table: "staff_changes",
                columns: new[] { "business_id", "staff_member_id" });

            migrationBuilder.CreateIndex(
                name: "ix_staff_invitations_business_id_accepted_staff_id",
                schema: "pos",
                table: "staff_invitations",
                columns: new[] { "business_id", "accepted_staff_id" });

            migrationBuilder.CreateIndex(
                name: "ix_staff_invitations_business_id_invited_by_staff_id",
                schema: "pos",
                table: "staff_invitations",
                columns: new[] { "business_id", "invited_by_staff_id" });

            migrationBuilder.CreateIndex(
                name: "ix_staff_invitations_business_id_normalized_email",
                schema: "pos",
                table: "staff_invitations",
                columns: new[] { "business_id", "normalized_email" },
                unique: true,
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ix_staff_invitations_token_hash",
                schema: "pos",
                table: "staff_invitations",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_staff_members_account_id",
                schema: "pos",
                table: "staff_members",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_staff_members_business_id_account_id",
                schema: "pos",
                table: "staff_members",
                columns: new[] { "business_id", "account_id" },
                unique: true,
                filter: "account_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_staff_members_business_id_pos_username",
                schema: "pos",
                table: "staff_members",
                columns: new[] { "business_id", "pos_username" },
                unique: true,
                filter: "pos_username IS NOT NULL");

            // Row-level security: the business's own rows; an account also reads its own staff rows.
            migrationBuilder.EnableBusinessRowSecurityWithOwnerRead("staff_members", "account_id");
            migrationBuilder.EnableBusinessRowSecurity("staff_invitations");
            migrationBuilder.EnableBusinessRowSecurity("staff_changes");
            migrationBuilder.Sql(StaffMigrationSql.AppendOnlyChanges);
            migrationBuilder.Sql(StaffMigrationSql.InvitationLookupFunction);

            // Feature 005 owners become owner staff; then business visibility follows staff rows and the
            // ownership table goes (its policies go with it).
            migrationBuilder.Sql(StaffMigrationSql.CopyOwners);
            migrationBuilder.ReplaceBusinessesOwnerReadWithStaff();
            migrationBuilder.DropTable(
                name: "business_owners",
                schema: "pos");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "business_owners",
                schema: "pos",
                columns: table => new
                {
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false)
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

            migrationBuilder.EnableBusinessRowSecurityWithOwnerRead("business_owners");
            migrationBuilder.Sql(StaffMigrationSql.CopyOwnersBack);
            migrationBuilder.Sql("DROP POLICY owner_read ON pos.businesses;");
            migrationBuilder.AddBusinessesOwnerRead();
            migrationBuilder.Sql(StaffMigrationSql.DropInvitationLookupFunction);

            migrationBuilder.DropTable(
                name: "staff_changes",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "staff_invitations",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "staff_members",
                schema: "pos");
        }
    }
}
