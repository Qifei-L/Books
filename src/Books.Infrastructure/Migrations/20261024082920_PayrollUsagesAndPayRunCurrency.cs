using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PayrollUsagesAndPayRunCurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_account_usages_usage",
                table: "account_usages");

            migrationBuilder.AddColumn<string>(
                name: "currency",
                table: "pay_runs",
                type: "character(3)",
                fixedLength: true,
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "rate",
                table: "pay_runs",
                type: "numeric(20,10)",
                precision: 20,
                scale: 10,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_usages_usage",
                table: "account_usages",
                sql: "usage IN ('payable', 'receivable', 'bank', 'output_vat', 'input_vat', 'non_deductible_vat', 'withholding_payable', 'salary_expense', 'nssf_expense', 'salaries_payable', 'salary_tax_payable', 'nssf_payable')");
        
            // Pay runs saved before currencies: their lines are in the ledger's currency.
            migrationBuilder.Sql("UPDATE pay_runs p SET currency = l.base_currency FROM ledgers l WHERE l.id = p.ledger_id AND p.currency = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_account_usages_usage",
                table: "account_usages");

            migrationBuilder.DropColumn(
                name: "currency",
                table: "pay_runs");

            migrationBuilder.DropColumn(
                name: "rate",
                table: "pay_runs");

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_usages_usage",
                table: "account_usages",
                sql: "usage IN ('payable', 'receivable', 'bank', 'output_vat', 'input_vat', 'non_deductible_vat', 'withholding_payable')");
        }
    }
}
