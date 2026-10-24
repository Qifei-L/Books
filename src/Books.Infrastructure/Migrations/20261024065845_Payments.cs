using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Payments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_no = table.Column<long>(type: "bigint", nullable: false),
                    is_receipt = table.Column<bool>(type: "boolean", nullable: false),
                    bank_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    party_id = table.Column<Guid>(type: "uuid", nullable: true),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    issues = table.Column<string>(type: "jsonb", nullable: true),
                    journal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                    table.UniqueConstraint("ak_payments_ledger_id_id", x => new { x.ledger_id, x.id });
                    table.CheckConstraint("ck_payments_status", "status IN ('draft', 'posted', 'deleted') AND (status <> 'posted' OR journal_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_payments_accounts_ledger_id_bank_account_id",
                        columns: x => new { x.ledger_id, x.bank_account_id },
                        principalTable: "accounts",
                        principalColumns: new[] { "ledger_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payments_journals_ledger_id_journal_id",
                        columns: x => new { x.ledger_id, x.journal_id },
                        principalTable: "journals",
                        principalColumns: new[] { "ledger_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payments_ledgers_org_id_ledger_id",
                        columns: x => new { x.org_id, x.ledger_id },
                        principalTable: "ledgers",
                        principalColumns: new[] { "org_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payments_parties_ledger_id_party_id",
                        columns: x => new { x.ledger_id, x.party_id },
                        principalTable: "parties",
                        principalColumns: new[] { "ledger_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payments_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_lines",
                columns: table => new
                {
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    control_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    unit_price = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    net_amount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    tax_code_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tax_calculated = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    tax_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    valid_tax_invoice = table.Column<bool>(type: "boolean", nullable: false),
                    withholding_code_id = table.Column<Guid>(type: "uuid", nullable: true),
                    withholding_amount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_lines", x => new { x.payment_id, x.line_no });
                    table.CheckConstraint("ck_payment_lines_kind", "(kind = 'invoice' AND invoice_id IS NOT NULL AND control_account_id IS NOT NULL AND account_id IS NULL) OR (kind = 'party' AND invoice_id IS NULL AND control_account_id IS NOT NULL AND account_id IS NULL) OR (kind = 'account' AND invoice_id IS NULL AND control_account_id IS NULL AND account_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_payment_lines_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payment_lines_accounts_control_account_id",
                        column: x => x.control_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payment_lines_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payment_lines_payments_payment_id",
                        column: x => x.payment_id,
                        principalTable: "payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payment_lines_tax_codes_tax_code_id",
                        column: x => x.tax_code_id,
                        principalTable: "tax_codes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payment_lines_tax_codes_withholding_code_id",
                        column: x => x.withholding_code_id,
                        principalTable: "tax_codes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payment_lines_account_id",
                table: "payment_lines",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_lines_control_account_id",
                table: "payment_lines",
                column: "control_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_lines_invoice_id",
                table: "payment_lines",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_lines_item_id",
                table: "payment_lines",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_lines_tax_code_id",
                table: "payment_lines",
                column: "tax_code_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_lines_withholding_code_id",
                table: "payment_lines",
                column: "withholding_code_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_created_by",
                table: "payments",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_payments_ledger_id_bank_account_id_date",
                table: "payments",
                columns: new[] { "ledger_id", "bank_account_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_ledger_id_is_receipt_payment_no",
                table: "payments",
                columns: new[] { "ledger_id", "is_receipt", "payment_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payments_ledger_id_journal_id",
                table: "payments",
                columns: new[] { "ledger_id", "journal_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_ledger_id_party_id",
                table: "payments",
                columns: new[] { "ledger_id", "party_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_org_id_ledger_id",
                table: "payments",
                columns: new[] { "org_id", "ledger_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_lines");

            migrationBuilder.DropTable(
                name: "payments");
        }
    }
}
