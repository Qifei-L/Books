using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReturnDefinitionVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "return_definition_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_no = table.Column<int>(type: "integer", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    definition = table.Column<string>(type: "text", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_by = table.Column<Guid>(type: "uuid", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_return_definition_versions", x => x.id);
                    table.CheckConstraint("ck_return_definition_versions_month", "EXTRACT(DAY FROM effective_from) = 1");
                    table.CheckConstraint("ck_return_definition_versions_status", "status IN ('draft', 'published') AND (status = 'draft' OR published_at IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_return_definition_versions_ledgers_org_id_ledger_id",
                        columns: x => new { x.org_id, x.ledger_id },
                        principalTable: "ledgers",
                        principalColumns: new[] { "org_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_return_definition_versions_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_return_definition_versions_created_by",
                table: "return_definition_versions",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_return_definition_versions_ledger_id_effective_from",
                table: "return_definition_versions",
                columns: new[] { "ledger_id", "effective_from" },
                unique: true,
                filter: "status = 'published'");

            migrationBuilder.CreateIndex(
                name: "ix_return_definition_versions_ledger_id_version_no",
                table: "return_definition_versions",
                columns: new[] { "ledger_id", "version_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_return_definition_versions_org_id_ledger_id",
                table: "return_definition_versions",
                columns: new[] { "org_id", "ledger_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "return_definition_versions");
        }
    }
}
