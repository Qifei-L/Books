using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PurchaseDebitNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_purchase_invoices_ledger_id_invoice_no",
                table: "purchase_invoices");

            migrationBuilder.AddColumn<bool>(
                name: "is_debit_note",
                table: "purchase_invoices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "original_invoice_id",
                table: "purchase_invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoices_ledger_id_is_debit_note_invoice_no",
                table: "purchase_invoices",
                columns: new[] { "ledger_id", "is_debit_note", "invoice_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoices_original_invoice_id",
                table: "purchase_invoices",
                column: "original_invoice_id");

            migrationBuilder.AddForeignKey(
                name: "fk_purchase_invoices_purchase_invoices_original_invoice_id",
                table: "purchase_invoices",
                column: "original_invoice_id",
                principalTable: "purchase_invoices",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_purchase_invoices_purchase_invoices_original_invoice_id",
                table: "purchase_invoices");

            migrationBuilder.DropIndex(
                name: "ix_purchase_invoices_ledger_id_is_debit_note_invoice_no",
                table: "purchase_invoices");

            migrationBuilder.DropIndex(
                name: "ix_purchase_invoices_original_invoice_id",
                table: "purchase_invoices");

            migrationBuilder.DropColumn(
                name: "is_debit_note",
                table: "purchase_invoices");

            migrationBuilder.DropColumn(
                name: "original_invoice_id",
                table: "purchase_invoices");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoices_ledger_id_invoice_no",
                table: "purchase_invoices",
                columns: new[] { "ledger_id", "invoice_no" },
                unique: true);
        }
    }
}
