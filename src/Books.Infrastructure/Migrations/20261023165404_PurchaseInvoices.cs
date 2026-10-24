using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PurchaseInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "purchase_invoices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_no = table.Column<long>(type: "bigint", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payable_account_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_purchase_invoices", x => x.id);
                    table.UniqueConstraint("ak_purchase_invoices_ledger_id_id", x => new { x.ledger_id, x.id });
                    table.CheckConstraint("ck_purchase_invoices_status", "status IN ('draft', 'posted', 'deleted') AND (status <> 'posted' OR journal_id IS NOT NULL) AND due_date >= date");
                    table.ForeignKey(
                        name: "fk_purchase_invoices_accounts_ledger_id_payable_account_id",
                        columns: x => new { x.ledger_id, x.payable_account_id },
                        principalTable: "accounts",
                        principalColumns: new[] { "ledger_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_invoices_journals_ledger_id_journal_id",
                        columns: x => new { x.ledger_id, x.journal_id },
                        principalTable: "journals",
                        principalColumns: new[] { "ledger_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_invoices_ledgers_org_id_ledger_id",
                        columns: x => new { x.org_id, x.ledger_id },
                        principalTable: "ledgers",
                        principalColumns: new[] { "org_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_invoices_parties_ledger_id_supplier_id",
                        columns: x => new { x.ledger_id, x.supplier_id },
                        principalTable: "parties",
                        principalColumns: new[] { "ledger_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_invoices_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "purchase_invoice_lines",
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
                    tax_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    valid_tax_invoice = table.Column<bool>(type: "boolean", nullable: false),
                    withholding_code_id = table.Column<Guid>(type: "uuid", nullable: true),
                    withholding_amount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_invoice_lines", x => new { x.invoice_id, x.line_no });
                    table.ForeignKey(
                        name: "fk_purchase_invoice_lines_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_invoice_lines_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_invoice_lines_purchase_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "purchase_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_invoice_lines_tax_codes_tax_code_id",
                        column: x => x.tax_code_id,
                        principalTable: "tax_codes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_invoice_lines_tax_codes_withholding_code_id",
                        column: x => x.withholding_code_id,
                        principalTable: "tax_codes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoice_lines_account_id",
                table: "purchase_invoice_lines",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoice_lines_item_id",
                table: "purchase_invoice_lines",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoice_lines_tax_code_id",
                table: "purchase_invoice_lines",
                column: "tax_code_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoice_lines_withholding_code_id",
                table: "purchase_invoice_lines",
                column: "withholding_code_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoices_created_by",
                table: "purchase_invoices",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoices_ledger_id_invoice_no",
                table: "purchase_invoices",
                columns: new[] { "ledger_id", "invoice_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoices_ledger_id_journal_id",
                table: "purchase_invoices",
                columns: new[] { "ledger_id", "journal_id" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoices_ledger_id_payable_account_id",
                table: "purchase_invoices",
                columns: new[] { "ledger_id", "payable_account_id" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoices_ledger_id_supplier_id",
                table: "purchase_invoices",
                columns: new[] { "ledger_id", "supplier_id" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoices_org_id_ledger_id",
                table: "purchase_invoices",
                columns: new[] { "org_id", "ledger_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "purchase_invoice_lines");

            migrationBuilder.DropTable(
                name: "purchase_invoices");
        }
    }
}
