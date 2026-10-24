using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StockAdjustmentEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_adjustment_lines_quantity",
                table: "stock_adjustment_lines");

            migrationBuilder.AlterColumn<Guid>(
                name: "account_id",
                table: "stock_adjustment_lines",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateTable(
                name: "stock_adjustment_entries",
                columns: table => new
                {
                    adjustment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_adjustment_entries", x => new { x.adjustment_id, x.line_no });
                    table.CheckConstraint("ck_stock_adjustment_entries_amount", "amount <> 0");
                    table.ForeignKey(
                        name: "fk_stock_adjustment_entries_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_adjustment_entries_stock_adjustments_adjustment_id",
                        column: x => x.adjustment_id,
                        principalTable: "stock_adjustments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_adjustment_lines_quantity",
                table: "stock_adjustment_lines",
                sql: "quantity <> 0 OR value <> 0");

            migrationBuilder.CreateIndex(
                name: "ix_stock_adjustment_entries_account_id",
                table: "stock_adjustment_entries",
                column: "account_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stock_adjustment_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_adjustment_lines_quantity",
                table: "stock_adjustment_lines");

            migrationBuilder.AlterColumn<Guid>(
                name: "account_id",
                table: "stock_adjustment_lines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_adjustment_lines_quantity",
                table: "stock_adjustment_lines",
                sql: "quantity <> 0");
        }
    }
}
