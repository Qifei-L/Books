using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AccountGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_accounts_accounts_ledger_id_parent_id",
                table: "accounts");

            migrationBuilder.RenameColumn(
                name: "parent_id",
                table: "accounts",
                newName: "group_id");

            migrationBuilder.RenameIndex(
                name: "ix_accounts_ledger_id_parent_id",
                table: "accounts",
                newName: "ix_accounts_ledger_id_group_id");

            // Rule 15 changed: accounts no longer nest under accounts. Old account parents are not groups,
            // so existing links are cleared rather than reinterpreted.
            migrationBuilder.Sql("UPDATE accounts SET group_id = NULL WHERE group_id IS NOT NULL;");

            migrationBuilder.CreateTable(
                name: "account_groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_groups", x => x.id);
                    table.UniqueConstraint("ak_account_groups_ledger_id_id", x => new { x.ledger_id, x.id });
                    table.CheckConstraint("ck_account_groups_type", "type IN ('assets', 'liabilities', 'equity', 'income', 'cost_and_expense')");
                    table.ForeignKey(
                        name: "fk_account_groups_account_groups_ledger_id_parent_id",
                        columns: x => new { x.ledger_id, x.parent_id },
                        principalTable: "account_groups",
                        principalColumns: new[] { "ledger_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_account_groups_ledgers_org_id_ledger_id",
                        columns: x => new { x.org_id, x.ledger_id },
                        principalTable: "ledgers",
                        principalColumns: new[] { "org_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_account_groups_ledger_id_parent_id",
                table: "account_groups",
                columns: new[] { "ledger_id", "parent_id" });

            migrationBuilder.CreateIndex(
                name: "ix_account_groups_org_id_ledger_id",
                table: "account_groups",
                columns: new[] { "org_id", "ledger_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_accounts_account_groups_ledger_id_group_id",
                table: "accounts",
                columns: new[] { "ledger_id", "group_id" },
                principalTable: "account_groups",
                principalColumns: new[] { "ledger_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_accounts_account_groups_ledger_id_group_id",
                table: "accounts");

            migrationBuilder.DropTable(
                name: "account_groups");

            migrationBuilder.RenameColumn(
                name: "group_id",
                table: "accounts",
                newName: "parent_id");

            migrationBuilder.RenameIndex(
                name: "ix_accounts_ledger_id_group_id",
                table: "accounts",
                newName: "ix_accounts_ledger_id_parent_id");

            migrationBuilder.AddForeignKey(
                name: "fk_accounts_accounts_ledger_id_parent_id",
                table: "accounts",
                columns: new[] { "ledger_id", "parent_id" },
                principalTable: "accounts",
                principalColumns: new[] { "ledger_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }
    }
}
