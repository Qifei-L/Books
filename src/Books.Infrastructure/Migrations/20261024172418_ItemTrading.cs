using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ItemTrading : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "code",
                table: "items",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "purchase_description",
                table: "items",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "purchase_price",
                table: "items",
                type: "numeric(20,4)",
                precision: 20,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "purchase_tax_code_id",
                table: "items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sales_description",
                table: "items",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "sales_price",
                table: "items",
                type: "numeric(20,4)",
                precision: 20,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "sales_tax_code_id",
                table: "items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_items_ledger_id_code",
                table: "items",
                columns: new[] { "ledger_id", "code" },
                unique: true,
                filter: "code IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_items_ledger_id_purchase_tax_code_id",
                table: "items",
                columns: new[] { "ledger_id", "purchase_tax_code_id" });

            migrationBuilder.CreateIndex(
                name: "ix_items_ledger_id_sales_tax_code_id",
                table: "items",
                columns: new[] { "ledger_id", "sales_tax_code_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_items_tax_codes_ledger_id_purchase_tax_code_id",
                table: "items",
                columns: new[] { "ledger_id", "purchase_tax_code_id" },
                principalTable: "tax_codes",
                principalColumns: new[] { "ledger_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_items_tax_codes_ledger_id_sales_tax_code_id",
                table: "items",
                columns: new[] { "ledger_id", "sales_tax_code_id" },
                principalTable: "tax_codes",
                principalColumns: new[] { "ledger_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_items_tax_codes_ledger_id_purchase_tax_code_id",
                table: "items");

            migrationBuilder.DropForeignKey(
                name: "fk_items_tax_codes_ledger_id_sales_tax_code_id",
                table: "items");

            migrationBuilder.DropIndex(
                name: "ix_items_ledger_id_code",
                table: "items");

            migrationBuilder.DropIndex(
                name: "ix_items_ledger_id_purchase_tax_code_id",
                table: "items");

            migrationBuilder.DropIndex(
                name: "ix_items_ledger_id_sales_tax_code_id",
                table: "items");

            migrationBuilder.DropColumn(
                name: "code",
                table: "items");

            migrationBuilder.DropColumn(
                name: "purchase_description",
                table: "items");

            migrationBuilder.DropColumn(
                name: "purchase_price",
                table: "items");

            migrationBuilder.DropColumn(
                name: "purchase_tax_code_id",
                table: "items");

            migrationBuilder.DropColumn(
                name: "sales_description",
                table: "items");

            migrationBuilder.DropColumn(
                name: "sales_price",
                table: "items");

            migrationBuilder.DropColumn(
                name: "sales_tax_code_id",
                table: "items");
        }
    }
}
