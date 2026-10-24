using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentTaxCurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "document_tax_currencies",
                columns: table => new
                {
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    rate = table.Column<decimal>(type: "numeric(20,10)", precision: 20, scale: 10, nullable: false),
                    rate_date = table.Column<DateOnly>(type: "date", nullable: false),
                    rate_source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    gross_total = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    tax_total = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_tax_currencies", x => x.document_id);
                    table.ForeignKey(
                        name: "fk_document_tax_currencies_ledgers_ledger_id",
                        column: x => x.ledger_id,
                        principalTable: "ledgers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_document_tax_currencies_ledger_id",
                table: "document_tax_currencies",
                column: "ledger_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_tax_currencies");
        }
    }
}
