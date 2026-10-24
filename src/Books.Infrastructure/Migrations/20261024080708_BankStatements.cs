using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BankStatements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bank_statements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opening_balance = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    closing_balance = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    from_date = table.Column<DateOnly>(type: "date", nullable: false),
                    to_date = table.Column<DateOnly>(type: "date", nullable: false),
                    imported_by = table.Column<Guid>(type: "uuid", nullable: false),
                    imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_statements", x => x.id);
                    table.ForeignKey(
                        name: "fk_bank_statements_accounts_ledger_id_account_id",
                        columns: x => new { x.ledger_id, x.account_id },
                        principalTable: "accounts",
                        principalColumns: new[] { "ledger_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bank_statements_ledgers_org_id_ledger_id",
                        columns: x => new { x.org_id, x.ledger_id },
                        principalTable: "ledgers",
                        principalColumns: new[] { "org_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bank_statements_users_imported_by",
                        column: x => x.imported_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bank_statement_lines",
                columns: table => new
                {
                    statement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    external_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    journal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_statement_lines", x => new { x.statement_id, x.line_no });
                    table.CheckConstraint("ck_bank_statement_lines_status", "status IN ('matched', 'drafted', 'duplicate')");
                    table.ForeignKey(
                        name: "fk_bank_statement_lines_bank_statements_statement_id",
                        column: x => x.statement_id,
                        principalTable: "bank_statements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bank_statement_lines_payments_payment_id",
                        column: x => x.payment_id,
                        principalTable: "payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bank_statement_lines_external_id",
                table: "bank_statement_lines",
                column: "external_id");

            migrationBuilder.CreateIndex(
                name: "ix_bank_statement_lines_payment_id",
                table: "bank_statement_lines",
                column: "payment_id");

            migrationBuilder.CreateIndex(
                name: "ix_bank_statements_imported_by",
                table: "bank_statements",
                column: "imported_by");

            migrationBuilder.CreateIndex(
                name: "ix_bank_statements_ledger_id_account_id_to_date",
                table: "bank_statements",
                columns: new[] { "ledger_id", "account_id", "to_date" });

            migrationBuilder.CreateIndex(
                name: "ix_bank_statements_org_id_ledger_id",
                table: "bank_statements",
                columns: new[] { "org_id", "ledger_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bank_statement_lines");

            migrationBuilder.DropTable(
                name: "bank_statements");
        }
    }
}
