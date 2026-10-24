using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TaxCodeReturnCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "return_category",
                table: "tax_codes",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            // Withholding codes created by the Cambodia template before categories existed.
            migrationBuilder.Sql("""
                UPDATE tax_codes t SET return_category = CASE t.name
                    WHEN 'WHT 15% services, interest, royalties' THEN 'services'
                    WHEN 'WHT 10% rent' THEN 'rent'
                    WHEN 'WHT 14% non-resident' THEN 'non_resident' END
                FROM ledgers l
                WHERE l.id = t.ledger_id AND l.tax_pack = 'cambodia-2026' AND t.kind = 'withholding'
                  AND t.name IN ('WHT 15% services, interest, royalties', 'WHT 10% rent', 'WHT 14% non-resident');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "return_category",
                table: "tax_codes");
        }
    }
}
