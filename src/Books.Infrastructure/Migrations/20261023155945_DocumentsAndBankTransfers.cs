using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DocumentsAndBankTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "source_id",
                table: "journals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_kind",
                table: "journals",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "bank_transfers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transfer_no = table.Column<long>(type: "bigint", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    from_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    fee_amount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    fee_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    journal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_transfers", x => x.id);
                    table.CheckConstraint("ck_bank_transfers_amounts", "amount > 0 AND fee_amount >= 0 AND from_account_id <> to_account_id");
                    table.ForeignKey(
                        name: "fk_bank_transfers_accounts_ledger_id_fee_account_id",
                        columns: x => new { x.ledger_id, x.fee_account_id },
                        principalTable: "accounts",
                        principalColumns: new[] { "ledger_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bank_transfers_accounts_ledger_id_from_account_id",
                        columns: x => new { x.ledger_id, x.from_account_id },
                        principalTable: "accounts",
                        principalColumns: new[] { "ledger_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bank_transfers_accounts_ledger_id_to_account_id",
                        columns: x => new { x.ledger_id, x.to_account_id },
                        principalTable: "accounts",
                        principalColumns: new[] { "ledger_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bank_transfers_journals_ledger_id_journal_id",
                        columns: x => new { x.ledger_id, x.journal_id },
                        principalTable: "journals",
                        principalColumns: new[] { "ledger_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bank_transfers_ledgers_org_id_ledger_id",
                        columns: x => new { x.org_id, x.ledger_id },
                        principalTable: "ledgers",
                        principalColumns: new[] { "org_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bank_transfers_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_journals_source_kind_source_id",
                table: "journals",
                columns: new[] { "source_kind", "source_id" },
                unique: true,
                filter: "source_kind IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_bank_transfers_created_by",
                table: "bank_transfers",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_bank_transfers_ledger_id_fee_account_id",
                table: "bank_transfers",
                columns: new[] { "ledger_id", "fee_account_id" });

            migrationBuilder.CreateIndex(
                name: "ix_bank_transfers_ledger_id_from_account_id",
                table: "bank_transfers",
                columns: new[] { "ledger_id", "from_account_id" });

            migrationBuilder.CreateIndex(
                name: "ix_bank_transfers_ledger_id_journal_id",
                table: "bank_transfers",
                columns: new[] { "ledger_id", "journal_id" });

            migrationBuilder.CreateIndex(
                name: "ix_bank_transfers_ledger_id_to_account_id",
                table: "bank_transfers",
                columns: new[] { "ledger_id", "to_account_id" });

            migrationBuilder.CreateIndex(
                name: "ix_bank_transfers_ledger_id_transfer_no",
                table: "bank_transfers",
                columns: new[] { "ledger_id", "transfer_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bank_transfers_org_id_ledger_id",
                table: "bank_transfers",
                columns: new[] { "org_id", "ledger_id" });

            // Per-ledger counters for document numbers (one row per document type), used under the write lock.
            migrationBuilder.Sql("""
                CREATE TABLE document_counters (
                    ledger_id uuid NOT NULL REFERENCES ledgers (id),
                    kind varchar(50) NOT NULL,
                    last_no bigint NOT NULL CHECK (last_no > 0),
                    PRIMARY KEY (ledger_id, kind)
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE document_counters;");

            migrationBuilder.DropTable(
                name: "bank_transfers");

            migrationBuilder.DropIndex(
                name: "ix_journals_source_kind_source_id",
                table: "journals");

            migrationBuilder.DropColumn(
                name: "source_id",
                table: "journals");

            migrationBuilder.DropColumn(
                name: "source_kind",
                table: "journals");
        }
    }
}
