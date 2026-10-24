using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DocumentCurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "base_total",
                table: "sales_invoices",
                type: "numeric(20,4)",
                precision: 20,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "currency",
                table: "sales_invoices",
                type: "character(3)",
                fixedLength: true,
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "exchange_rate",
                table: "sales_invoices",
                type: "numeric(20,10)",
                precision: 20,
                scale: 10,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "base_total",
                table: "purchase_invoices",
                type: "numeric(20,4)",
                precision: 20,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "currency",
                table: "purchase_invoices",
                type: "character(3)",
                fixedLength: true,
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "exchange_rate",
                table: "purchase_invoices",
                type: "numeric(20,10)",
                precision: 20,
                scale: 10,
                nullable: true);
        
            // Documents saved before currencies are in the ledger's currency; their base total is what they post.
            migrationBuilder.Sql("""
                UPDATE purchase_invoices p SET currency = l.base_currency,
                    base_total = COALESCE((SELECT SUM(x.net_amount + x.tax_amount - x.withholding_amount) FROM purchase_invoice_lines x WHERE x.invoice_id = p.id), 0)
                FROM ledgers l WHERE l.id = p.ledger_id AND p.currency = '';
                UPDATE sales_invoices s SET currency = l.base_currency,
                    base_total = COALESCE((SELECT SUM(x.net_amount + x.tax_amount + x.other_tax_amount) FROM sales_invoice_lines x WHERE x.invoice_id = s.id), 0)
                FROM ledgers l WHERE l.id = s.ledger_id AND s.currency = '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "base_total",
                table: "sales_invoices");

            migrationBuilder.DropColumn(
                name: "currency",
                table: "sales_invoices");

            migrationBuilder.DropColumn(
                name: "exchange_rate",
                table: "sales_invoices");

            migrationBuilder.DropColumn(
                name: "base_total",
                table: "purchase_invoices");

            migrationBuilder.DropColumn(
                name: "currency",
                table: "purchase_invoices");

            migrationBuilder.DropColumn(
                name: "exchange_rate",
                table: "purchase_invoices");
        }
    }
}
