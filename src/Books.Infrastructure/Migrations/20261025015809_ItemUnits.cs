using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ItemUnits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "unit",
                table: "sales_invoice_lines",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "unit_factor",
                table: "sales_invoice_lines",
                type: "numeric(20,4)",
                precision: 20,
                scale: 4,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<string>(
                name: "unit",
                table: "purchase_invoice_lines",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "unit_factor",
                table: "purchase_invoice_lines",
                type: "numeric(20,4)",
                precision: 20,
                scale: 4,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.CreateTable(
                name: "item_units",
                columns: table => new
                {
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    factor = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_units", x => new { x.item_id, x.name });
                    table.CheckConstraint("ck_item_units_factor", "factor > 0");
                    table.ForeignKey(
                        name: "fk_item_units_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "item_units");

            migrationBuilder.DropColumn(
                name: "unit",
                table: "sales_invoice_lines");

            migrationBuilder.DropColumn(
                name: "unit_factor",
                table: "sales_invoice_lines");

            migrationBuilder.DropColumn(
                name: "unit",
                table: "purchase_invoice_lines");

            migrationBuilder.DropColumn(
                name: "unit_factor",
                table: "purchase_invoice_lines");
        }
    }
}
