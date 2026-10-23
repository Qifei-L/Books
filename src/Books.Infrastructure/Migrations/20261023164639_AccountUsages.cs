using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AccountUsages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "non_deductible_account_id",
                table: "tax_codes",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "account_usages",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usage = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_usages", x => new { x.account_id, x.usage });
                    table.CheckConstraint("ck_account_usages_usage", "usage IN ('payable', 'receivable', 'bank', 'output_vat', 'input_vat', 'non_deductible_vat', 'withholding_payable')");
                    table.ForeignKey(
                        name: "fk_account_usages_accounts_ledger_id_account_id",
                        columns: x => new { x.ledger_id, x.account_id },
                        principalTable: "accounts",
                        principalColumns: new[] { "ledger_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_account_usages_ledgers_org_id_ledger_id",
                        columns: x => new { x.org_id, x.ledger_id },
                        principalTable: "ledgers",
                        principalColumns: new[] { "org_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tax_codes_ledger_id_non_deductible_account_id",
                table: "tax_codes",
                columns: new[] { "ledger_id", "non_deductible_account_id" });

            migrationBuilder.CreateIndex(
                name: "ix_account_usages_ledger_id_account_id",
                table: "account_usages",
                columns: new[] { "ledger_id", "account_id" });

            migrationBuilder.CreateIndex(
                name: "ix_account_usages_ledger_id_usage",
                table: "account_usages",
                columns: new[] { "ledger_id", "usage" });

            migrationBuilder.CreateIndex(
                name: "ix_account_usages_org_id_ledger_id",
                table: "account_usages",
                columns: new[] { "org_id", "ledger_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_tax_codes_accounts_ledger_id_non_deductible_account_id",
                table: "tax_codes",
                columns: new[] { "ledger_id", "non_deductible_account_id" },
                principalTable: "accounts",
                principalColumns: new[] { "ledger_id", "id" },
                onDelete: ReferentialAction.Restrict);

            // Existing mappings get their usages, so every reference already follows the usage rule.
            migrationBuilder.Sql("""
                INSERT INTO account_usages (account_id, usage, org_id, ledger_id)
                SELECT b.account_id, 'bank', b.org_id, b.ledger_id FROM bank_accounts b
                ON CONFLICT DO NOTHING;
                INSERT INTO account_usages (account_id, usage, org_id, ledger_id)
                SELECT DISTINCT t.account_id,
                    CASE t.kind WHEN 'vat_output' THEN 'output_vat' WHEN 'vat_input' THEN 'input_vat' ELSE 'withholding_payable' END,
                    t.org_id, t.ledger_id
                FROM tax_codes t WHERE t.account_id IS NOT NULL
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_tax_codes_accounts_ledger_id_non_deductible_account_id",
                table: "tax_codes");

            migrationBuilder.DropTable(
                name: "account_usages");

            migrationBuilder.DropIndex(
                name: "ix_tax_codes_ledger_id_non_deductible_account_id",
                table: "tax_codes");

            migrationBuilder.DropColumn(
                name: "non_deductible_account_id",
                table: "tax_codes");
        }
    }
}
