using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SuspensePurpose : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_account_purposes_purpose",
                table: "account_purposes");

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_purposes_purpose",
                table: "account_purposes",
                sql: "purpose IN ('retained_earnings', 'opening_balance_clearing', 'rounding_differences', 'suspense')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_account_purposes_purpose",
                table: "account_purposes");

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_purposes_purpose",
                table: "account_purposes",
                sql: "purpose IN ('retained_earnings', 'opening_balance_clearing', 'rounding_differences')");
        }
    }
}
