using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ItemAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "cost_of_sales_account_id",
                table: "items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "income_account_id",
                table: "items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "inventory_account_id",
                table: "items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_items_ledger_id_cost_of_sales_account_id",
                table: "items",
                columns: new[] { "ledger_id", "cost_of_sales_account_id" });

            migrationBuilder.CreateIndex(
                name: "ix_items_ledger_id_income_account_id",
                table: "items",
                columns: new[] { "ledger_id", "income_account_id" });

            migrationBuilder.CreateIndex(
                name: "ix_items_ledger_id_inventory_account_id",
                table: "items",
                columns: new[] { "ledger_id", "inventory_account_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_items_accounts_ledger_id_cost_of_sales_account_id",
                table: "items",
                columns: new[] { "ledger_id", "cost_of_sales_account_id" },
                principalTable: "accounts",
                principalColumns: new[] { "ledger_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_items_accounts_ledger_id_income_account_id",
                table: "items",
                columns: new[] { "ledger_id", "income_account_id" },
                principalTable: "accounts",
                principalColumns: new[] { "ledger_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_items_accounts_ledger_id_inventory_account_id",
                table: "items",
                columns: new[] { "ledger_id", "inventory_account_id" },
                principalTable: "accounts",
                principalColumns: new[] { "ledger_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_items_accounts_ledger_id_cost_of_sales_account_id",
                table: "items");

            migrationBuilder.DropForeignKey(
                name: "fk_items_accounts_ledger_id_income_account_id",
                table: "items");

            migrationBuilder.DropForeignKey(
                name: "fk_items_accounts_ledger_id_inventory_account_id",
                table: "items");

            migrationBuilder.DropIndex(
                name: "ix_items_ledger_id_cost_of_sales_account_id",
                table: "items");

            migrationBuilder.DropIndex(
                name: "ix_items_ledger_id_income_account_id",
                table: "items");

            migrationBuilder.DropIndex(
                name: "ix_items_ledger_id_inventory_account_id",
                table: "items");

            migrationBuilder.DropColumn(
                name: "cost_of_sales_account_id",
                table: "items");

            migrationBuilder.DropColumn(
                name: "income_account_id",
                table: "items");

            migrationBuilder.DropColumn(
                name: "inventory_account_id",
                table: "items");
        }
    }
}
