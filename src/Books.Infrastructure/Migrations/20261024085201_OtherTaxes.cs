using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OtherTaxes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_tax_codes_kind",
                table: "tax_codes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_account_usages_usage",
                table: "account_usages");

            migrationBuilder.AddColumn<decimal>(
                name: "other_tax_amount",
                table: "sales_invoice_lines",
                type: "numeric(20,4)",
                precision: 20,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "other_tax_code_id",
                table: "sales_invoice_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_tax_codes_kind",
                table: "tax_codes",
                sql: "kind IN ('vat_output', 'vat_input', 'vat_zero_rated', 'non_vat', 'withholding', 'other_tax')");

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoice_lines_other_tax_code_id",
                table: "sales_invoice_lines",
                column: "other_tax_code_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_usages_usage",
                table: "account_usages",
                sql: "usage IN ('payable', 'receivable', 'bank', 'output_vat', 'input_vat', 'non_deductible_vat', 'withholding_payable', 'salary_expense', 'nssf_expense', 'salaries_payable', 'salary_tax_payable', 'nssf_payable', 'other_tax_payable')");

            migrationBuilder.AddForeignKey(
                name: "fk_sales_invoice_lines_tax_codes_other_tax_code_id",
                table: "sales_invoice_lines",
                column: "other_tax_code_id",
                principalTable: "tax_codes",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_sales_invoice_lines_tax_codes_other_tax_code_id",
                table: "sales_invoice_lines");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tax_codes_kind",
                table: "tax_codes");

            migrationBuilder.DropIndex(
                name: "ix_sales_invoice_lines_other_tax_code_id",
                table: "sales_invoice_lines");

            migrationBuilder.DropCheckConstraint(
                name: "ck_account_usages_usage",
                table: "account_usages");

            migrationBuilder.DropColumn(
                name: "other_tax_amount",
                table: "sales_invoice_lines");

            migrationBuilder.DropColumn(
                name: "other_tax_code_id",
                table: "sales_invoice_lines");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tax_codes_kind",
                table: "tax_codes",
                sql: "kind IN ('vat_output', 'vat_input', 'vat_zero_rated', 'non_vat', 'withholding')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_usages_usage",
                table: "account_usages",
                sql: "usage IN ('payable', 'receivable', 'bank', 'output_vat', 'input_vat', 'non_deductible_vat', 'withholding_payable', 'salary_expense', 'nssf_expense', 'salaries_payable', 'salary_tax_payable', 'nssf_payable')");
        }
    }
}
