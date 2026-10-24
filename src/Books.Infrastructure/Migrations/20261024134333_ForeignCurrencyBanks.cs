using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ForeignCurrencyBanks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_account_usages_usage",
                table: "account_usages");

            migrationBuilder.AddColumn<string>(
                name: "currency",
                table: "payments",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "exchange_rate",
                table: "payments",
                type: "numeric(20,10)",
                precision: 20,
                scale: 10,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "control_amount",
                table: "payment_lines",
                type: "numeric(20,4)",
                precision: 20,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "settled_amount",
                table: "payment_lines",
                type: "numeric(20,4)",
                precision: 20,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "currency_amount",
                table: "journal_lines",
                type: "numeric(20,4)",
                precision: 20,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "from_rate",
                table: "bank_transfers",
                type: "numeric(20,10)",
                precision: 20,
                scale: 10,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "to_amount",
                table: "bank_transfers",
                type: "numeric(20,4)",
                precision: 20,
                scale: 4,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_usages_usage",
                table: "account_usages",
                sql: "usage IN ('payable', 'receivable', 'bank', 'output_vat', 'input_vat', 'non_deductible_vat', 'withholding_payable', 'salary_expense', 'nssf_expense', 'salaries_payable', 'salary_tax_payable', 'nssf_payable', 'other_tax_payable', 'inventory', 'cost_of_sales', 'fixed_asset', 'accumulated_depreciation', 'depreciation_expense', 'fx_realized', 'fx_unrealized')");

            // Before M12.2 every payment was in the ledger currency: control lines moved their own amount.
            migrationBuilder.Sql("UPDATE payment_lines SET control_amount = net_amount;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_account_usages_usage",
                table: "account_usages");

            migrationBuilder.DropColumn(
                name: "currency",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "exchange_rate",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "control_amount",
                table: "payment_lines");

            migrationBuilder.DropColumn(
                name: "settled_amount",
                table: "payment_lines");

            migrationBuilder.DropColumn(
                name: "currency_amount",
                table: "journal_lines");

            migrationBuilder.DropColumn(
                name: "from_rate",
                table: "bank_transfers");

            migrationBuilder.DropColumn(
                name: "to_amount",
                table: "bank_transfers");

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_usages_usage",
                table: "account_usages",
                sql: "usage IN ('payable', 'receivable', 'bank', 'output_vat', 'input_vat', 'non_deductible_vat', 'withholding_payable', 'salary_expense', 'nssf_expense', 'salaries_payable', 'salary_tax_payable', 'nssf_payable', 'other_tax_payable', 'inventory', 'cost_of_sales', 'fixed_asset', 'accumulated_depreciation', 'depreciation_expense')");
        }
    }
}
