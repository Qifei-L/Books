using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CaseInsensitiveAccountCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_accounts_ledger_id_code",
                table: "accounts");

            // Rule 16: codes are unique within a ledger ignoring letter case.
            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_accounts_ledger_id_lower_code ON accounts (ledger_id, lower(code));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX ux_accounts_ledger_id_lower_code;");

            migrationBuilder.CreateIndex(
                name: "ix_accounts_ledger_id_code",
                table: "accounts",
                columns: new[] { "ledger_id", "code" },
                unique: true);
        }
    }
}
