using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCompaniesAndLedgers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "companies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_companies", x => x.id);
                    table.UniqueConstraint("ak_companies_org_id_id", x => new { x.org_id, x.id });
                    table.ForeignKey(
                        name: "fk_companies_organizations_org_id",
                        column: x => x.org_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ledgers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    base_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    fy_start_month = table.Column<int>(type: "integer", nullable: false),
                    closed_through = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledgers", x => x.id);
                    table.UniqueConstraint("ak_ledgers_org_id_company_id", x => new { x.org_id, x.company_id });
                    table.UniqueConstraint("ak_ledgers_org_id_id", x => new { x.org_id, x.id });
                    table.CheckConstraint("ck_ledgers_fy_start_month", "fy_start_month BETWEEN 1 AND 12");
                    table.ForeignKey(
                        name: "fk_ledgers_companies",
                        columns: x => new { x.org_id, x.company_id },
                        principalTable: "companies",
                        principalColumns: new[] { "org_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_companies_org_id_created_at_id",
                table: "companies",
                columns: new[] { "org_id", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_ledgers_company_id",
                table: "ledgers",
                column: "company_id",
                unique: true);

            // Exactly one ledger per company: each side references the other, checked at commit,
            // so a company can only be committed together with its ledger (rule 1).
            migrationBuilder.Sql("""
                ALTER TABLE ledgers ALTER CONSTRAINT fk_ledgers_companies DEFERRABLE INITIALLY DEFERRED;
                ALTER TABLE companies ADD CONSTRAINT fk_companies_ledgers
                    FOREIGN KEY (org_id, id) REFERENCES ledgers (org_id, company_id) DEFERRABLE INITIALLY DEFERRED;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE companies DROP CONSTRAINT fk_companies_ledgers;");

            migrationBuilder.DropTable(
                name: "ledgers");

            migrationBuilder.DropTable(
                name: "companies");
        }
    }
}
