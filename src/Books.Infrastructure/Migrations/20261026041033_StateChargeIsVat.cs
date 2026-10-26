using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StateChargeIsVat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_sales_invoice_lines_tax_codes_state_charge_code_id",
                table: "sales_invoice_lines");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tax_codes_kind",
                table: "tax_codes");

            migrationBuilder.DropIndex(
                name: "ix_sales_invoice_lines_state_charge_code_id",
                table: "sales_invoice_lines");

            migrationBuilder.DropColumn(
                name: "state_charge_amount",
                table: "sales_invoice_lines");

            migrationBuilder.DropColumn(
                name: "state_charge_code_id",
                table: "sales_invoice_lines");

            // State charge is VAT: any pre-release state_charge code becomes an output VAT code.
            // Its account still has to be given the Output VAT usage by the ledger administrator.
            migrationBuilder.Sql("UPDATE tax_codes SET kind = 'vat_output', return_category = NULL WHERE kind = 'state_charge';");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tax_codes_kind",
                table: "tax_codes",
                sql: "kind IN ('vat_output', 'vat_input', 'vat_input_non_deductible', 'vat_zero_rated', 'non_vat', 'withholding', 'other_tax')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_tax_codes_kind",
                table: "tax_codes");

            migrationBuilder.AddColumn<decimal>(
                name: "state_charge_amount",
                table: "sales_invoice_lines",
                type: "numeric(20,4)",
                precision: 20,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "state_charge_code_id",
                table: "sales_invoice_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_tax_codes_kind",
                table: "tax_codes",
                sql: "kind IN ('vat_output', 'vat_input', 'vat_input_non_deductible', 'vat_zero_rated', 'non_vat', 'withholding', 'other_tax', 'state_charge')");

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoice_lines_state_charge_code_id",
                table: "sales_invoice_lines",
                column: "state_charge_code_id");

            migrationBuilder.AddForeignKey(
                name: "fk_sales_invoice_lines_tax_codes_state_charge_code_id",
                table: "sales_invoice_lines",
                column: "state_charge_code_id",
                principalTable: "tax_codes",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
