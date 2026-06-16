using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMinimalGeneralLedgerControls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""UPDATE "Accounts" SET "Type" = 'Revenue' WHERE "Type" = 'Income';""");

            migrationBuilder.AddColumn<bool>(
                name: "AllowDeletePostedJournal",
                table: "Ledgers",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllowManualJournal",
                table: "Accounts",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSystemReserved",
                table: "Accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowDeletePostedJournal",
                table: "Ledgers");

            migrationBuilder.DropColumn(
                name: "AllowManualJournal",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "IsSystemReserved",
                table: "Accounts");
        }
    }
}
