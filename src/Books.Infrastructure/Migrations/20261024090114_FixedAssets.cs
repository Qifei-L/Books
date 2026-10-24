using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixedAssets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_account_usages_usage",
                table: "account_usages");

            migrationBuilder.CreateTable(
                name: "depreciation_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period = table.Column<DateOnly>(type: "date", nullable: false),
                    journal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_depreciation_runs", x => x.id);
                    table.CheckConstraint("ck_depreciation_runs_status", "status IN ('draft', 'posted', 'deleted') AND EXTRACT(DAY FROM period) = 1");
                    table.ForeignKey(
                        name: "fk_depreciation_runs_ledgers_org_id_ledger_id",
                        columns: x => new { x.org_id, x.ledger_id },
                        principalTable: "ledgers",
                        principalColumns: new[] { "org_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fixed_assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    acquisition_date = table.Column<DateOnly>(type: "date", nullable: false),
                    cost = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    residual_value = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    useful_life_months = table.Column<int>(type: "integer", nullable: false),
                    asset_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    accumulated_depreciation_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    depreciation_expense_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    disposed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    disposal_journal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fixed_assets", x => x.id);
                    table.ForeignKey(
                        name: "fk_fixed_assets_accounts_accumulated_depreciation_account_id",
                        column: x => x.accumulated_depreciation_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fixed_assets_accounts_asset_account_id",
                        column: x => x.asset_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fixed_assets_accounts_depreciation_expense_account_id",
                        column: x => x.depreciation_expense_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fixed_assets_ledgers_org_id_ledger_id",
                        columns: x => new { x.org_id, x.ledger_id },
                        principalTable: "ledgers",
                        principalColumns: new[] { "org_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "depreciation_lines",
                columns: table => new
                {
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_depreciation_lines", x => new { x.run_id, x.line_no });
                    table.ForeignKey(
                        name: "fk_depreciation_lines_depreciation_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "depreciation_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_depreciation_lines_fixed_assets_asset_id",
                        column: x => x.asset_id,
                        principalTable: "fixed_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_usages_usage",
                table: "account_usages",
                sql: "usage IN ('payable', 'receivable', 'bank', 'output_vat', 'input_vat', 'non_deductible_vat', 'withholding_payable', 'salary_expense', 'nssf_expense', 'salaries_payable', 'salary_tax_payable', 'nssf_payable', 'other_tax_payable', 'inventory', 'cost_of_sales', 'fixed_asset', 'accumulated_depreciation', 'depreciation_expense')");

            migrationBuilder.CreateIndex(
                name: "ix_depreciation_lines_asset_id",
                table: "depreciation_lines",
                column: "asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_depreciation_runs_ledger_id_period",
                table: "depreciation_runs",
                columns: new[] { "ledger_id", "period" },
                unique: true,
                filter: "status = 'posted'");

            migrationBuilder.CreateIndex(
                name: "ix_depreciation_runs_org_id_ledger_id",
                table: "depreciation_runs",
                columns: new[] { "org_id", "ledger_id" });

            migrationBuilder.CreateIndex(
                name: "ix_fixed_assets_accumulated_depreciation_account_id",
                table: "fixed_assets",
                column: "accumulated_depreciation_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_fixed_assets_asset_account_id",
                table: "fixed_assets",
                column: "asset_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_fixed_assets_depreciation_expense_account_id",
                table: "fixed_assets",
                column: "depreciation_expense_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_fixed_assets_ledger_id_code",
                table: "fixed_assets",
                columns: new[] { "ledger_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fixed_assets_org_id_ledger_id",
                table: "fixed_assets",
                columns: new[] { "org_id", "ledger_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "depreciation_lines");

            migrationBuilder.DropTable(
                name: "depreciation_runs");

            migrationBuilder.DropTable(
                name: "fixed_assets");

            migrationBuilder.DropCheckConstraint(
                name: "ck_account_usages_usage",
                table: "account_usages");

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_usages_usage",
                table: "account_usages",
                sql: "usage IN ('payable', 'receivable', 'bank', 'output_vat', 'input_vat', 'non_deductible_vat', 'withholding_payable', 'salary_expense', 'nssf_expense', 'salaries_payable', 'salary_tax_payable', 'nssf_payable', 'other_tax_payable', 'inventory', 'cost_of_sales')");
        }
    }
}
