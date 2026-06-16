using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEntityLedgerStructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Accounts_Ledgers_LedgerId",
                table: "Accounts");

            migrationBuilder.RenameColumn(
                name: "LedgerId",
                table: "Accounts",
                newName: "EntityId");

            migrationBuilder.RenameIndex(
                name: "IX_Accounts_LedgerId_Code",
                table: "Accounts",
                newName: "IX_Accounts_EntityId_Code");

            migrationBuilder.CreateTable(
                name: "Entities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Entities", x => x.Id);
                });

            migrationBuilder.Sql("""
                INSERT INTO "Entities" ("Code", "Name", "IsActive")
                SELECT 'DEMO', 'Demo Company', TRUE
                WHERE NOT EXISTS (SELECT 1 FROM "Entities" WHERE "Code" = 'DEMO');
                """);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Ledgers",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "EntityId",
                table: "Ledgers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LedgerType",
                table: "Ledgers",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Main");

            migrationBuilder.Sql("""
                UPDATE "Ledgers"
                SET
                    "EntityId" = (SELECT "Id" FROM "Entities" WHERE "Code" = 'DEMO' LIMIT 1),
                    "Code" = CASE
                        WHEN "Name" = 'Demo Ledger' THEN 'MAIN'
                        WHEN "Code" = '' THEN 'LEDGER-' || "Id"::text
                        ELSE "Code"
                    END,
                    "LedgerType" = 'Main';
                """);

            migrationBuilder.Sql("""
                UPDATE "Accounts" a
                SET "EntityId" = l."EntityId"
                FROM "Ledgers" l
                WHERE a."EntityId" = l."Id";
                """);

            migrationBuilder.Sql("""
                UPDATE "Accounts" a
                SET "Code" = '1200'
                WHERE a."Code" = '1100'
                    AND a."Name" = 'Accounts Receivable'
                    AND NOT EXISTS (
                        SELECT 1
                        FROM "Accounts" existing
                        WHERE existing."EntityId" = a."EntityId"
                            AND existing."Code" = '1200'
                    );
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Ledgers_EntityId_Code",
                table: "Ledgers",
                columns: new[] { "EntityId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Entities_Code",
                table: "Entities",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Accounts_Entities_EntityId",
                table: "Accounts",
                column: "EntityId",
                principalTable: "Entities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Ledgers_Entities_EntityId",
                table: "Ledgers",
                column: "EntityId",
                principalTable: "Entities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Accounts_Entities_EntityId",
                table: "Accounts");

            migrationBuilder.DropForeignKey(
                name: "FK_Ledgers_Entities_EntityId",
                table: "Ledgers");

            migrationBuilder.DropIndex(
                name: "IX_Ledgers_EntityId_Code",
                table: "Ledgers");

            migrationBuilder.RenameColumn(
                name: "EntityId",
                table: "Accounts",
                newName: "LedgerId");

            migrationBuilder.RenameIndex(
                name: "IX_Accounts_EntityId_Code",
                table: "Accounts",
                newName: "IX_Accounts_LedgerId_Code");

            migrationBuilder.Sql("""
                UPDATE "Accounts" a
                SET "LedgerId" = ledger_ids."LedgerId"
                FROM (
                    SELECT "EntityId", MIN("Id") AS "LedgerId"
                    FROM "Ledgers"
                    GROUP BY "EntityId"
                ) ledger_ids
                WHERE a."LedgerId" = ledger_ids."EntityId";
                """);

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Ledgers");

            migrationBuilder.DropColumn(
                name: "EntityId",
                table: "Ledgers");

            migrationBuilder.DropColumn(
                name: "LedgerType",
                table: "Ledgers");

            migrationBuilder.DropTable(
                name: "Entities");

            migrationBuilder.AddForeignKey(
                name: "FK_Accounts_Ledgers_LedgerId",
                table: "Accounts",
                column: "LedgerId",
                principalTable: "Ledgers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
