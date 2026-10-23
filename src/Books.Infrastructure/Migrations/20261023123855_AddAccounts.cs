using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_control = table.Column<bool>(type: "boolean", nullable: false),
                    is_bank = table.Column<bool>(type: "boolean", nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounts", x => x.id);
                    table.UniqueConstraint("ak_accounts_ledger_id_id", x => new { x.ledger_id, x.id });
                    table.CheckConstraint("ck_accounts_type", "type IN ('assets', 'liabilities', 'equity', 'income', 'cost_and_expense')");
                    table.ForeignKey(
                        name: "fk_accounts_accounts_ledger_id_parent_id",
                        columns: x => new { x.ledger_id, x.parent_id },
                        principalTable: "accounts",
                        principalColumns: new[] { "ledger_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_accounts_ledgers_org_id_ledger_id",
                        columns: x => new { x.org_id, x.ledger_id },
                        principalTable: "ledgers",
                        principalColumns: new[] { "org_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_accounts_ledger_id_code",
                table: "accounts",
                columns: new[] { "ledger_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_accounts_ledger_id_parent_id",
                table: "accounts",
                columns: new[] { "ledger_id", "parent_id" });

            migrationBuilder.CreateIndex(
                name: "ix_accounts_org_id_ledger_id",
                table: "accounts",
                columns: new[] { "org_id", "ledger_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accounts");
        }
    }
}
