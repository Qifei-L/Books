using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLedgerGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ledger_grants",
                columns: table => new
                {
                    membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    membership_generation = table.Column<int>(type: "integer", nullable: false),
                    access = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    granted_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_grants", x => new { x.membership_id, x.ledger_id });
                    table.CheckConstraint("ck_ledger_grants_access", "access IN ('read', 'edit')");
                    table.ForeignKey(
                        name: "fk_ledger_grants_ledgers_org_id_ledger_id",
                        columns: x => new { x.org_id, x.ledger_id },
                        principalTable: "ledgers",
                        principalColumns: new[] { "org_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ledger_grants_memberships_org_id_membership_id",
                        columns: x => new { x.org_id, x.membership_id },
                        principalTable: "memberships",
                        principalColumns: new[] { "org_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ledger_grants_users_granted_by",
                        column: x => x.granted_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_grants_granted_by",
                table: "ledger_grants",
                column: "granted_by");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_grants_org_id_ledger_id",
                table: "ledger_grants",
                columns: new[] { "org_id", "ledger_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_grants_org_id_membership_id",
                table: "ledger_grants",
                columns: new[] { "org_id", "membership_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ledger_grants");
        }
    }
}
