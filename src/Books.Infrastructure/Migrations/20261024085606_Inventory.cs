using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Inventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_account_usages_usage",
                table: "account_usages");

            migrationBuilder.AddColumn<decimal>(
                name: "cost_amount",
                table: "sales_invoice_lines",
                type: "numeric(20,4)",
                precision: 20,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "cost_of_sales_account_id",
                table: "sales_invoice_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "inventory_account_id",
                table: "sales_invoice_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "track_inventory",
                table: "items",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_usages_usage",
                table: "account_usages",
                sql: "usage IN ('payable', 'receivable', 'bank', 'output_vat', 'input_vat', 'non_deductible_vat', 'withholding_payable', 'salary_expense', 'nssf_expense', 'salaries_payable', 'salary_tax_payable', 'nssf_payable', 'other_tax_payable', 'inventory', 'cost_of_sales')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_account_usages_usage",
                table: "account_usages");

            migrationBuilder.DropColumn(
                name: "cost_amount",
                table: "sales_invoice_lines");

            migrationBuilder.DropColumn(
                name: "cost_of_sales_account_id",
                table: "sales_invoice_lines");

            migrationBuilder.DropColumn(
                name: "inventory_account_id",
                table: "sales_invoice_lines");

            migrationBuilder.DropColumn(
                name: "track_inventory",
                table: "items");

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_usages_usage",
                table: "account_usages",
                sql: "usage IN ('payable', 'receivable', 'bank', 'output_vat', 'input_vat', 'non_deductible_vat', 'withholding_payable', 'salary_expense', 'nssf_expense', 'salaries_payable', 'salary_tax_payable', 'nssf_payable', 'other_tax_payable')");
        }
    }
}
