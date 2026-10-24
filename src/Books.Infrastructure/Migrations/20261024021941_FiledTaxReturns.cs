using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FiledTaxReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "filed_tax_returns",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period = table.Column<DateOnly>(type: "date", nullable: false),
                    snapshot = table.Column<string>(type: "text", nullable: false),
                    filed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    filed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_filed_tax_returns", x => x.id);
                    table.CheckConstraint("ck_filed_tax_returns_period", "EXTRACT(DAY FROM period) = 1");
                    table.ForeignKey(
                        name: "fk_filed_tax_returns_ledgers_org_id_ledger_id",
                        columns: x => new { x.org_id, x.ledger_id },
                        principalTable: "ledgers",
                        principalColumns: new[] { "org_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_filed_tax_returns_users_filed_by",
                        column: x => x.filed_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_filed_tax_returns_filed_by",
                table: "filed_tax_returns",
                column: "filed_by");

            migrationBuilder.CreateIndex(
                name: "ix_filed_tax_returns_ledger_id_period",
                table: "filed_tax_returns",
                columns: new[] { "ledger_id", "period" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_filed_tax_returns_org_id_ledger_id",
                table: "filed_tax_returns",
                columns: new[] { "org_id", "ledger_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "filed_tax_returns");
        }
    }
}
