using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DocumentDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "issues",
                table: "bank_transfers",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "bank_transfers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "posted");

            // Existing transfers were all posted when saved; carry the old deleted flag over.
            migrationBuilder.Sql("UPDATE bank_transfers SET status = 'deleted' WHERE deleted;");
            migrationBuilder.Sql("ALTER TABLE bank_transfers ALTER COLUMN status DROP DEFAULT;");

            migrationBuilder.DropColumn(
                name: "deleted",
                table: "bank_transfers");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bank_transfers_status",
                table: "bank_transfers",
                sql: "status IN ('draft', 'posted', 'deleted') AND (status <> 'posted' OR journal_id IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_bank_transfers_status",
                table: "bank_transfers");

            migrationBuilder.DropColumn(
                name: "issues",
                table: "bank_transfers");

            migrationBuilder.DropColumn(
                name: "status",
                table: "bank_transfers");

            migrationBuilder.AddColumn<bool>(
                name: "deleted",
                table: "bank_transfers",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
