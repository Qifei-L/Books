using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FxRevaluations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_account_usages_usage",
                table: "account_usages");

            migrationBuilder.CreateTable(
                name: "fx_revaluations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revaluation_no = table.Column<long>(type: "bigint", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    rates = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    journal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reversal_journal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fx_revaluations", x => x.id);
                    table.CheckConstraint("ck_fx_revaluations_status", "status IN ('posted', 'deleted') AND journal_id IS NOT NULL AND reversal_journal_id IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_fx_revaluations_journals_ledger_id_journal_id",
                        columns: x => new { x.ledger_id, x.journal_id },
                        principalTable: "journals",
                        principalColumns: new[] { "ledger_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fx_revaluations_journals_ledger_id_reversal_journal_id",
                        columns: x => new { x.ledger_id, x.reversal_journal_id },
                        principalTable: "journals",
                        principalColumns: new[] { "ledger_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fx_revaluations_ledgers_org_id_ledger_id",
                        columns: x => new { x.org_id, x.ledger_id },
                        principalTable: "ledgers",
                        principalColumns: new[] { "org_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fx_revaluations_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fx_revaluation_lines",
                columns: table => new
                {
                    revaluation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    party_id = table.Column<Guid>(type: "uuid", nullable: true),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    foreign_amount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    before = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    after = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fx_revaluation_lines", x => new { x.revaluation_id, x.line_no });
                    table.ForeignKey(
                        name: "fk_fx_revaluation_lines_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fx_revaluation_lines_fx_revaluations_revaluation_id",
                        column: x => x.revaluation_id,
                        principalTable: "fx_revaluations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_usages_usage",
                table: "account_usages",
                sql: "usage IN ('payable', 'receivable', 'bank', 'output_vat', 'input_vat', 'non_deductible_vat', 'withholding_payable', 'salary_expense', 'nssf_expense', 'salaries_payable', 'salary_tax_payable', 'nssf_payable', 'other_tax_payable', 'inventory', 'cost_of_sales', 'fixed_asset', 'accumulated_depreciation', 'depreciation_expense', 'fx_realized', 'fx_unrealized', 'receivable_revaluation', 'payable_revaluation')");

            migrationBuilder.CreateIndex(
                name: "ix_fx_revaluation_lines_account_id",
                table: "fx_revaluation_lines",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_fx_revaluations_created_by",
                table: "fx_revaluations",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_fx_revaluations_ledger_id_date",
                table: "fx_revaluations",
                columns: new[] { "ledger_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_fx_revaluations_ledger_id_journal_id",
                table: "fx_revaluations",
                columns: new[] { "ledger_id", "journal_id" });

            migrationBuilder.CreateIndex(
                name: "ix_fx_revaluations_ledger_id_revaluation_no",
                table: "fx_revaluations",
                columns: new[] { "ledger_id", "revaluation_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fx_revaluations_ledger_id_reversal_journal_id",
                table: "fx_revaluations",
                columns: new[] { "ledger_id", "reversal_journal_id" });

            migrationBuilder.CreateIndex(
                name: "ix_fx_revaluations_org_id_ledger_id",
                table: "fx_revaluations",
                columns: new[] { "org_id", "ledger_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fx_revaluation_lines");

            migrationBuilder.DropTable(
                name: "fx_revaluations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_account_usages_usage",
                table: "account_usages");

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_usages_usage",
                table: "account_usages",
                sql: "usage IN ('payable', 'receivable', 'bank', 'output_vat', 'input_vat', 'non_deductible_vat', 'withholding_payable', 'salary_expense', 'nssf_expense', 'salaries_payable', 'salary_tax_payable', 'nssf_payable', 'other_tax_payable', 'inventory', 'cost_of_sales', 'fixed_asset', 'accumulated_depreciation', 'depreciation_expense', 'fx_realized', 'fx_unrealized')");
        }
    }
}
