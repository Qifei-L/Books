using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SalesLineStateCharge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_sales_invoice_lines_tax_codes_state_charge_code_id",
                table: "sales_invoice_lines");

            migrationBuilder.DropIndex(
                name: "ix_sales_invoice_lines_state_charge_code_id",
                table: "sales_invoice_lines");

            migrationBuilder.DropColumn(
                name: "state_charge_amount",
                table: "sales_invoice_lines");

            migrationBuilder.DropColumn(
                name: "state_charge_code_id",
                table: "sales_invoice_lines");
        }
    }
}
