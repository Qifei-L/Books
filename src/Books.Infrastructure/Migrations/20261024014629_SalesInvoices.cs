using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SalesInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sales_invoices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_no = table.Column<long>(type: "bigint", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_credit_note = table.Column<bool>(type: "boolean", nullable: false),
                    original_invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    receivable_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
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
                    table.PrimaryKey("pk_sales_invoices", x => x.id);
                    table.UniqueConstraint("ak_sales_invoices_ledger_id_id", x => new { x.ledger_id, x.id });
                    table.CheckConstraint("ck_sales_invoices_status", "status IN ('draft', 'posted', 'deleted') AND (status <> 'posted' OR journal_id IS NOT NULL) AND due_date >= date");
                    table.ForeignKey(
                        name: "fk_sales_invoices_accounts_ledger_id_receivable_account_id",
                        columns: x => new { x.ledger_id, x.receivable_account_id },
                        principalTable: "accounts",
                        principalColumns: new[] { "ledger_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_invoices_journals_ledger_id_journal_id",
                        columns: x => new { x.ledger_id, x.journal_id },
                        principalTable: "journals",
                        principalColumns: new[] { "ledger_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_invoices_ledgers_org_id_ledger_id",
                        columns: x => new { x.org_id, x.ledger_id },
                        principalTable: "ledgers",
                        principalColumns: new[] { "org_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_invoices_parties_ledger_id_customer_id",
                        columns: x => new { x.ledger_id, x.customer_id },
                        principalTable: "parties",
                        principalColumns: new[] { "ledger_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_invoices_sales_invoices_original_invoice_id",
                        column: x => x.original_invoice_id,
                        principalTable: "sales_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_invoices_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_invoice_lines",
                columns: table => new
                {
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    unit_price = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    net_amount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    tax_code_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tax_calculated = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    tax_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_invoice_lines", x => new { x.invoice_id, x.line_no });
                    table.ForeignKey(
                        name: "fk_sales_invoice_lines_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_invoice_lines_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_invoice_lines_sales_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "sales_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_invoice_lines_tax_codes_tax_code_id",
                        column: x => x.tax_code_id,
                        principalTable: "tax_codes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoice_lines_account_id",
                table: "sales_invoice_lines",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoice_lines_item_id",
                table: "sales_invoice_lines",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoice_lines_tax_code_id",
                table: "sales_invoice_lines",
                column: "tax_code_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoices_created_by",
                table: "sales_invoices",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoices_ledger_id_customer_id",
                table: "sales_invoices",
                columns: new[] { "ledger_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoices_ledger_id_is_credit_note_invoice_no",
                table: "sales_invoices",
                columns: new[] { "ledger_id", "is_credit_note", "invoice_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoices_ledger_id_journal_id",
                table: "sales_invoices",
                columns: new[] { "ledger_id", "journal_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoices_ledger_id_receivable_account_id",
                table: "sales_invoices",
                columns: new[] { "ledger_id", "receivable_account_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoices_org_id_ledger_id",
                table: "sales_invoices",
                columns: new[] { "org_id", "ledger_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoices_original_invoice_id",
                table: "sales_invoices",
                column: "original_invoice_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sales_invoice_lines");

            migrationBuilder.DropTable(
                name: "sales_invoices");
        }
    }
}
