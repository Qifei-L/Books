using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class JournalClassesAndTax : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "class_id",
                table: "journal_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "location_id",
                table: "journal_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "tax_amount",
                table: "journal_lines",
                type: "numeric(20,4)",
                precision: 20,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "tax_calculated",
                table: "journal_lines",
                type: "numeric(20,4)",
                precision: 20,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "tax_code_id",
                table: "journal_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tax_direction",
                table: "journal_lines",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "classes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_classes", x => x.id);
                    table.UniqueConstraint("ak_classes_ledger_id_id", x => new { x.ledger_id, x.id });
                    table.ForeignKey(
                        name: "fk_classes_ledgers_org_id_ledger_id",
                        columns: x => new { x.org_id, x.ledger_id },
                        principalTable: "ledgers",
                        principalColumns: new[] { "org_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_journal_lines_ledger_id_class_id",
                table: "journal_lines",
                columns: new[] { "ledger_id", "class_id" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_lines_ledger_id_location_id",
                table: "journal_lines",
                columns: new[] { "ledger_id", "location_id" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_lines_ledger_id_tax_code_id",
                table: "journal_lines",
                columns: new[] { "ledger_id", "tax_code_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_journal_lines_tax_direction",
                table: "journal_lines",
                sql: "tax_direction IS NULL OR tax_direction IN ('sale', 'sale_adjustment', 'purchase', 'purchase_adjustment')");

            migrationBuilder.CreateIndex(
                name: "ix_classes_ledger_id_name",
                table: "classes",
                columns: new[] { "ledger_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_classes_org_id_ledger_id",
                table: "classes",
                columns: new[] { "org_id", "ledger_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_journal_lines_classes_ledger_id_class_id",
                table: "journal_lines",
                columns: new[] { "ledger_id", "class_id" },
                principalTable: "classes",
                principalColumns: new[] { "ledger_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_journal_lines_locations_ledger_id_location_id",
                table: "journal_lines",
                columns: new[] { "ledger_id", "location_id" },
                principalTable: "locations",
                principalColumns: new[] { "ledger_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_journal_lines_tax_codes_ledger_id_tax_code_id",
                table: "journal_lines",
                columns: new[] { "ledger_id", "tax_code_id" },
                principalTable: "tax_codes",
                principalColumns: new[] { "ledger_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_journal_lines_classes_ledger_id_class_id",
                table: "journal_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_journal_lines_locations_ledger_id_location_id",
                table: "journal_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_journal_lines_tax_codes_ledger_id_tax_code_id",
                table: "journal_lines");

            migrationBuilder.DropTable(
                name: "classes");

            migrationBuilder.DropIndex(
                name: "ix_journal_lines_ledger_id_class_id",
                table: "journal_lines");

            migrationBuilder.DropIndex(
                name: "ix_journal_lines_ledger_id_location_id",
                table: "journal_lines");

            migrationBuilder.DropIndex(
                name: "ix_journal_lines_ledger_id_tax_code_id",
                table: "journal_lines");

            migrationBuilder.DropCheckConstraint(
                name: "ck_journal_lines_tax_direction",
                table: "journal_lines");

            migrationBuilder.DropColumn(
                name: "class_id",
                table: "journal_lines");

            migrationBuilder.DropColumn(
                name: "location_id",
                table: "journal_lines");

            migrationBuilder.DropColumn(
                name: "tax_amount",
                table: "journal_lines");

            migrationBuilder.DropColumn(
                name: "tax_calculated",
                table: "journal_lines");

            migrationBuilder.DropColumn(
                name: "tax_code_id",
                table: "journal_lines");

            migrationBuilder.DropColumn(
                name: "tax_direction",
                table: "journal_lines");
        }
    }
}
