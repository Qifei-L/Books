using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Locations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "location_id",
                table: "stock_adjustment_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "location_id",
                table: "sales_invoice_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "location_id",
                table: "purchase_invoice_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "locations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_locations", x => x.id);
                    table.UniqueConstraint("ak_locations_ledger_id_id", x => new { x.ledger_id, x.id });
                    table.ForeignKey(
                        name: "fk_locations_ledgers_org_id_ledger_id",
                        columns: x => new { x.org_id, x.ledger_id },
                        principalTable: "ledgers",
                        principalColumns: new[] { "org_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_transfers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transfer_no = table.Column<long>(type: "bigint", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    from_location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_transfers", x => x.id);
                    table.CheckConstraint("ck_stock_transfers", "status IN ('posted', 'deleted') AND from_location_id <> to_location_id");
                    table.ForeignKey(
                        name: "fk_stock_transfers_ledgers_org_id_ledger_id",
                        columns: x => new { x.org_id, x.ledger_id },
                        principalTable: "ledgers",
                        principalColumns: new[] { "org_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_transfers_locations_ledger_id_from_location_id",
                        columns: x => new { x.ledger_id, x.from_location_id },
                        principalTable: "locations",
                        principalColumns: new[] { "ledger_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_transfers_locations_ledger_id_to_location_id",
                        columns: x => new { x.ledger_id, x.to_location_id },
                        principalTable: "locations",
                        principalColumns: new[] { "ledger_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_transfers_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_transfer_lines",
                columns: table => new
                {
                    transfer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_transfer_lines", x => new { x.transfer_id, x.line_no });
                    table.CheckConstraint("ck_stock_transfer_lines_quantity", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_stock_transfer_lines_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_transfer_lines_stock_transfers_transfer_id",
                        column: x => x.transfer_id,
                        principalTable: "stock_transfers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stock_adjustment_lines_location_id",
                table: "stock_adjustment_lines",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoice_lines_location_id",
                table: "sales_invoice_lines",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoice_lines_location_id",
                table: "purchase_invoice_lines",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_locations_ledger_id",
                table: "locations",
                column: "ledger_id",
                unique: true,
                filter: "is_default");

            migrationBuilder.CreateIndex(
                name: "ix_locations_org_id_ledger_id",
                table: "locations",
                columns: new[] { "org_id", "ledger_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_lines_item_id",
                table: "stock_transfer_lines",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfers_created_by",
                table: "stock_transfers",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfers_ledger_id_from_location_id",
                table: "stock_transfers",
                columns: new[] { "ledger_id", "from_location_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfers_ledger_id_to_location_id",
                table: "stock_transfers",
                columns: new[] { "ledger_id", "to_location_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfers_ledger_id_transfer_no",
                table: "stock_transfers",
                columns: new[] { "ledger_id", "transfer_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfers_org_id_ledger_id",
                table: "stock_transfers",
                columns: new[] { "org_id", "ledger_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_purchase_invoice_lines_locations_location_id",
                table: "purchase_invoice_lines",
                column: "location_id",
                principalTable: "locations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_sales_invoice_lines_locations_location_id",
                table: "sales_invoice_lines",
                column: "location_id",
                principalTable: "locations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_adjustment_lines_locations_location_id",
                table: "stock_adjustment_lines",
                column: "location_id",
                principalTable: "locations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_purchase_invoice_lines_locations_location_id",
                table: "purchase_invoice_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_sales_invoice_lines_locations_location_id",
                table: "sales_invoice_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_adjustment_lines_locations_location_id",
                table: "stock_adjustment_lines");

            migrationBuilder.DropTable(
                name: "stock_transfer_lines");

            migrationBuilder.DropTable(
                name: "stock_transfers");

            migrationBuilder.DropTable(
                name: "locations");

            migrationBuilder.DropIndex(
                name: "ix_stock_adjustment_lines_location_id",
                table: "stock_adjustment_lines");

            migrationBuilder.DropIndex(
                name: "ix_sales_invoice_lines_location_id",
                table: "sales_invoice_lines");

            migrationBuilder.DropIndex(
                name: "ix_purchase_invoice_lines_location_id",
                table: "purchase_invoice_lines");

            migrationBuilder.DropColumn(
                name: "location_id",
                table: "stock_adjustment_lines");

            migrationBuilder.DropColumn(
                name: "location_id",
                table: "sales_invoice_lines");

            migrationBuilder.DropColumn(
                name: "location_id",
                table: "purchase_invoice_lines");
        }
    }
}
