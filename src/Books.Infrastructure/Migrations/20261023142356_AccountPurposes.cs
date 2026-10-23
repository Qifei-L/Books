using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AccountPurposes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "account_purposes",
                columns: table => new
                {
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_purposes", x => new { x.ledger_id, x.purpose });
                    table.CheckConstraint("ck_account_purposes_purpose", "purpose IN ('retained_earnings', 'opening_balance_clearing', 'rounding_differences')");
                    table.ForeignKey(
                        name: "fk_account_purposes_accounts_ledger_id_account_id",
                        columns: x => new { x.ledger_id, x.account_id },
                        principalTable: "accounts",
                        principalColumns: new[] { "ledger_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_account_purposes_ledgers_org_id_ledger_id",
                        columns: x => new { x.org_id, x.ledger_id },
                        principalTable: "ledgers",
                        principalColumns: new[] { "org_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_account_purposes_ledger_id_account_id",
                table: "account_purposes",
                columns: new[] { "ledger_id", "account_id" });

            migrationBuilder.CreateIndex(
                name: "ix_account_purposes_org_id_ledger_id",
                table: "account_purposes",
                columns: new[] { "org_id", "ledger_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_purposes");
        }
    }
}
