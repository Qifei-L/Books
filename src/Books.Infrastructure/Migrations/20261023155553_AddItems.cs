using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "item_id",
                table: "journal_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "quantity",
                table: "journal_lines",
                type: "numeric(20,4)",
                precision: 20,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "item_categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_categories", x => x.id);
                    table.UniqueConstraint("ak_item_categories_ledger_id_id", x => new { x.ledger_id, x.id });
                    table.ForeignKey(
                        name: "fk_item_categories_ledgers_org_id_ledger_id",
                        columns: x => new { x.org_id, x.ledger_id },
                        principalTable: "ledgers",
                        principalColumns: new[] { "org_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    default_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    default_tax_code_id = table.Column<Guid>(type: "uuid", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_items", x => x.id);
                    table.UniqueConstraint("ak_items_ledger_id_id", x => new { x.ledger_id, x.id });
                    table.ForeignKey(
                        name: "fk_items_accounts_ledger_id_default_account_id",
                        columns: x => new { x.ledger_id, x.default_account_id },
                        principalTable: "accounts",
                        principalColumns: new[] { "ledger_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_items_item_categories_ledger_id_category_id",
                        columns: x => new { x.ledger_id, x.category_id },
                        principalTable: "item_categories",
                        principalColumns: new[] { "ledger_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_items_ledgers_org_id_ledger_id",
                        columns: x => new { x.org_id, x.ledger_id },
                        principalTable: "ledgers",
                        principalColumns: new[] { "org_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_items_tax_codes_ledger_id_default_tax_code_id",
                        columns: x => new { x.ledger_id, x.default_tax_code_id },
                        principalTable: "tax_codes",
                        principalColumns: new[] { "ledger_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_journal_lines_ledger_id_item_id",
                table: "journal_lines",
                columns: new[] { "ledger_id", "item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_item_categories_org_id_ledger_id",
                table: "item_categories",
                columns: new[] { "org_id", "ledger_id" });

            migrationBuilder.CreateIndex(
                name: "ix_items_ledger_id_category_id",
                table: "items",
                columns: new[] { "ledger_id", "category_id" });

            migrationBuilder.CreateIndex(
                name: "ix_items_ledger_id_default_account_id",
                table: "items",
                columns: new[] { "ledger_id", "default_account_id" });

            migrationBuilder.CreateIndex(
                name: "ix_items_ledger_id_default_tax_code_id",
                table: "items",
                columns: new[] { "ledger_id", "default_tax_code_id" });

            migrationBuilder.CreateIndex(
                name: "ix_items_org_id_ledger_id",
                table: "items",
                columns: new[] { "org_id", "ledger_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_journal_lines_items_ledger_id_item_id",
                table: "journal_lines",
                columns: new[] { "ledger_id", "item_id" },
                principalTable: "items",
                principalColumns: new[] { "ledger_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_journal_lines_items_ledger_id_item_id",
                table: "journal_lines");

            migrationBuilder.DropTable(
                name: "items");

            migrationBuilder.DropTable(
                name: "item_categories");

            migrationBuilder.DropIndex(
                name: "ix_journal_lines_ledger_id_item_id",
                table: "journal_lines");

            migrationBuilder.DropColumn(
                name: "item_id",
                table: "journal_lines");

            migrationBuilder.DropColumn(
                name: "quantity",
                table: "journal_lines");
        }
    }
}
