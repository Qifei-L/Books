using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "invoice_settings",
                columns: table => new
                {
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_khmer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    name_english = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    tax_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    address_khmer = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    address_english = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    invoice_prefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    credit_note_prefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_settings", x => x.ledger_id);
                    table.ForeignKey(
                        name: "fk_invoice_settings_ledgers_ledger_id",
                        column: x => x.ledger_id,
                        principalTable: "ledgers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "invoice_settings");
        }
    }
}
