using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LedgerRolesAndPartyLocalizations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "role",
                table: "ledger_grants",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "role",
                table: "invitation_grants",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "party_localizations",
                columns: table => new
                {
                    party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    locale = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_party_localizations", x => new { x.party_id, x.locale });
                    table.CheckConstraint("ck_party_localizations_locale", "locale IN ('km')");
                    table.ForeignKey(
                        name: "fk_party_localizations_parties_party_id",
                        column: x => x.party_id,
                        principalTable: "parties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_ledger_grants_role",
                table: "ledger_grants",
                sql: "role IS NULL OR role IN ('viewer', 'bookkeeper', 'accountant', 'manager')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invitation_grants_role",
                table: "invitation_grants",
                sql: "role IS NULL OR role IN ('viewer', 'bookkeeper', 'accountant', 'manager')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "party_localizations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ledger_grants_role",
                table: "ledger_grants");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invitation_grants_role",
                table: "invitation_grants");

            migrationBuilder.DropColumn(
                name: "role",
                table: "ledger_grants");

            migrationBuilder.DropColumn(
                name: "role",
                table: "invitation_grants");
        }
    }
}
