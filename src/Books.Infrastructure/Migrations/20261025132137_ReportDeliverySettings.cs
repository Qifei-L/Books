using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReportDeliverySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ledger_report_settings",
                columns: table => new
                {
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    language = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    comparative = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    show_zero_balances = table.Column<bool>(type: "boolean", nullable: false),
                    show_account_codes = table.Column<bool>(type: "boolean", nullable: false),
                    paper_size = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    orientation = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    watermark = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    pack_sections = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    share_days = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_report_settings", x => x.ledger_id);
                    table.CheckConstraint("ck_ledger_report_settings_language", "language IN ('en', 'km', 'bilingual')");
                    table.CheckConstraint("ck_ledger_report_settings_orientation", "orientation IN ('portrait', 'landscape')");
                    table.CheckConstraint("ck_ledger_report_settings_watermark", "watermark IN ('none', 'draft', 'final')");
                    table.ForeignKey(
                        name: "fk_ledger_report_settings_ledgers_ledger_id",
                        column: x => x.ledger_id,
                        principalTable: "ledgers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "organization_branding",
                columns: table => new
                {
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    legal_name_km = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    vat_tin = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    brand_color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    footer = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    prepared_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    reviewed_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organization_branding", x => x.org_id);
                    table.ForeignKey(
                        name: "fk_organization_branding_organizations_org_id",
                        column: x => x.org_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ledger_report_settings");

            migrationBuilder.DropTable(
                name: "organization_branding");
        }
    }
}
