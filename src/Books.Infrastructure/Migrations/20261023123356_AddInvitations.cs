using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInvitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "invitations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inviter_membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_email_key = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    proposed_role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    accepted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invitations", x => x.id);
                    table.UniqueConstraint("ak_invitations_org_id_id", x => new { x.org_id, x.id });
                    table.CheckConstraint("ck_invitations_role", "proposed_role IN ('admin', 'member')");
                    table.CheckConstraint("ck_invitations_status", "status IN ('pending', 'accepted', 'revoked', 'expired', 'invalidated')");
                    table.ForeignKey(
                        name: "fk_invitations_memberships_org_id_inviter_membership_id",
                        columns: x => new { x.org_id, x.inviter_membership_id },
                        principalTable: "memberships",
                        principalColumns: new[] { "org_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invitations_users_accepted_by",
                        column: x => x.accepted_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "invitation_grants",
                columns: table => new
                {
                    invitation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    access = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invitation_grants", x => new { x.invitation_id, x.ledger_id });
                    table.CheckConstraint("ck_invitation_grants_access", "access IN ('read', 'edit')");
                    table.ForeignKey(
                        name: "fk_invitation_grants_invitations_org_id_invitation_id",
                        columns: x => new { x.org_id, x.invitation_id },
                        principalTable: "invitations",
                        principalColumns: new[] { "org_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invitation_grants_ledgers_org_id_ledger_id",
                        columns: x => new { x.org_id, x.ledger_id },
                        principalTable: "ledgers",
                        principalColumns: new[] { "org_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_invitation_grants_org_id_invitation_id",
                table: "invitation_grants",
                columns: new[] { "org_id", "invitation_id" });

            migrationBuilder.CreateIndex(
                name: "ix_invitation_grants_org_id_ledger_id",
                table: "invitation_grants",
                columns: new[] { "org_id", "ledger_id" });

            migrationBuilder.CreateIndex(
                name: "ix_invitations_accepted_by",
                table: "invitations",
                column: "accepted_by");

            migrationBuilder.CreateIndex(
                name: "ix_invitations_org_id_inviter_membership_id_status",
                table: "invitations",
                columns: new[] { "org_id", "inviter_membership_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_invitations_org_id_target_email_key",
                table: "invitations",
                columns: new[] { "org_id", "target_email_key" },
                unique: true,
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ix_invitations_target_email_key_status_expires_at",
                table: "invitations",
                columns: new[] { "target_email_key", "status", "expires_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "invitation_grants");

            migrationBuilder.DropTable(
                name: "invitations");
        }
    }
}
