using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Assemblies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "assemblies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assembly_no = table.Column<long>(type: "bigint", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    inventory_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    labour_amount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    labour_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    journal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assemblies", x => x.id);
                    table.CheckConstraint("ck_assemblies_status", "status IN ('draft', 'posted', 'deleted') AND (status <> 'posted' OR journal_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_assemblies_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_assemblies_journals_ledger_id_journal_id",
                        columns: x => new { x.ledger_id, x.journal_id },
                        principalTable: "journals",
                        principalColumns: new[] { "ledger_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_assemblies_ledgers_org_id_ledger_id",
                        columns: x => new { x.org_id, x.ledger_id },
                        principalTable: "ledgers",
                        principalColumns: new[] { "org_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_assemblies_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "item_components",
                columns: table => new
                {
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    component_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_components", x => new { x.item_id, x.component_id });
                    table.CheckConstraint("ck_item_components_quantity", "quantity > 0 AND component_id <> item_id");
                    table.ForeignKey(
                        name: "fk_item_components_items_component_id",
                        column: x => x.component_id,
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_item_components_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "assembly_lines",
                columns: table => new
                {
                    assembly_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    component_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    value = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    inventory_account_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assembly_lines", x => new { x.assembly_id, x.line_no });
                    table.ForeignKey(
                        name: "fk_assembly_lines_assemblies_assembly_id",
                        column: x => x.assembly_id,
                        principalTable: "assemblies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_assembly_lines_items_component_id",
                        column: x => x.component_id,
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assemblies_created_by",
                table: "assemblies",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_assemblies_item_id",
                table: "assemblies",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_assemblies_ledger_id_assembly_no",
                table: "assemblies",
                columns: new[] { "ledger_id", "assembly_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_assemblies_ledger_id_journal_id",
                table: "assemblies",
                columns: new[] { "ledger_id", "journal_id" });

            migrationBuilder.CreateIndex(
                name: "ix_assemblies_org_id_ledger_id",
                table: "assemblies",
                columns: new[] { "org_id", "ledger_id" });

            migrationBuilder.CreateIndex(
                name: "ix_assembly_lines_component_id",
                table: "assembly_lines",
                column: "component_id");

            migrationBuilder.CreateIndex(
                name: "ix_item_components_component_id",
                table: "item_components",
                column: "component_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assembly_lines");

            migrationBuilder.DropTable(
                name: "item_components");

            migrationBuilder.DropTable(
                name: "assemblies");
        }
    }
}
