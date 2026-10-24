using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WithholdingCategoriesWt03 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Withholding categories now follow form WT 03 lines.
            migrationBuilder.Sql("""
                UPDATE tax_codes SET return_category = CASE return_category
                    WHEN 'services' THEN 'r1_services' WHEN 'rent' THEN 'r5_rent' WHEN 'non_resident' THEN 'n3_management' END
                WHERE return_category IN ('services', 'rent', 'non_resident');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE tax_codes SET return_category = CASE return_category
                    WHEN 'r1_services' THEN 'services' WHEN 'r5_rent' THEN 'rent' WHEN 'n3_management' THEN 'non_resident' END
                WHERE return_category IN ('r1_services', 'r5_rent', 'n3_management');
                """);
        }
    }
}
