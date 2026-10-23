using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddJournals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "last_journal_no",
                table: "ledgers",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "journals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    journal_no = table.Column<long>(type: "bigint", nullable: false),
                    effective_revision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    current_revision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revision_count = table.Column<int>(type: "integer", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_journals", x => x.id);
                    table.UniqueConstraint("ak_journals_ledger_id_id", x => new { x.ledger_id, x.id });
                    table.ForeignKey(
                        name: "fk_journals_ledgers_org_id_ledger_id",
                        columns: x => new { x.org_id, x.ledger_id },
                        principalTable: "ledgers",
                        principalColumns: new[] { "org_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_journals_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "journal_revisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    journal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_no = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    accounting_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    issues = table.Column<string>(type: "jsonb", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    superseded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_journal_revisions", x => x.id);
                    table.UniqueConstraint("ak_journal_revisions_journal_id_id", x => new { x.journal_id, x.id });
                    table.UniqueConstraint("ak_journal_revisions_ledger_id_id", x => new { x.ledger_id, x.id });
                    table.CheckConstraint("ck_journal_revisions_kind", "kind IN ('manual', 'opening')");
                    table.CheckConstraint("ck_journal_revisions_status", "status IN ('draft', 'error', 'posted', 'superseded', 'deleted')");
                    table.ForeignKey(
                        name: "fk_journal_revisions_journals_ledger_id_journal_id",
                        columns: x => new { x.ledger_id, x.journal_id },
                        principalTable: "journals",
                        principalColumns: new[] { "ledger_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_journal_revisions_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "journal_lines",
                columns: table => new
                {
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_journal_lines", x => new { x.revision_id, x.line_no });
                    table.CheckConstraint("ck_journal_lines_amount", "amount <> 0");
                    table.ForeignKey(
                        name: "fk_journal_lines_accounts_ledger_id_account_id",
                        columns: x => new { x.ledger_id, x.account_id },
                        principalTable: "accounts",
                        principalColumns: new[] { "ledger_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_journal_lines_journal_revisions_ledger_id_revision_id",
                        columns: x => new { x.ledger_id, x.revision_id },
                        principalTable: "journal_revisions",
                        principalColumns: new[] { "ledger_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_journal_lines_ledger_id_account_id",
                table: "journal_lines",
                columns: new[] { "ledger_id", "account_id" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_lines_ledger_id_revision_id",
                table: "journal_lines",
                columns: new[] { "ledger_id", "revision_id" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_lines_line_id",
                table: "journal_lines",
                column: "line_id");

            migrationBuilder.CreateIndex(
                name: "ix_journal_revisions_created_by",
                table: "journal_revisions",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_journal_revisions_journal_id_revision_no",
                table: "journal_revisions",
                columns: new[] { "journal_id", "revision_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_journal_revisions_ledger_id_journal_id",
                table: "journal_revisions",
                columns: new[] { "ledger_id", "journal_id" });

            migrationBuilder.CreateIndex(
                name: "ix_journals_created_by",
                table: "journals",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_journals_ledger_id_journal_no",
                table: "journals",
                columns: new[] { "ledger_id", "journal_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_journals_org_id_ledger_id",
                table: "journals",
                columns: new[] { "org_id", "ledger_id" });

            // Journal invariants that hold even if the service is bypassed.
            migrationBuilder.Sql("""
                ALTER TABLE journals ADD CONSTRAINT fk_journals_effective_revision
                    FOREIGN KEY (id, effective_revision_id) REFERENCES journal_revisions (journal_id, id) DEFERRABLE INITIALLY DEFERRED;
                ALTER TABLE journals ADD CONSTRAINT fk_journals_current_revision
                    FOREIGN KEY (id, current_revision_id) REFERENCES journal_revisions (journal_id, id) DEFERRABLE INITIALLY DEFERRED;

                -- Revisions are never changed after they are written, except to mark them superseded.
                CREATE FUNCTION journal_revisions_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'UPDATE'
                        AND NEW.status = 'superseded' AND OLD.status IN ('draft', 'error', 'posted') AND NEW.superseded_at IS NOT NULL
                        AND (NEW.id, NEW.org_id, NEW.ledger_id, NEW.journal_id, NEW.revision_no, NEW.kind, NEW.accounting_date,
                             NEW.reference, NEW.description, NEW.issues::text, NEW.created_by, NEW.created_at)
                            IS NOT DISTINCT FROM
                            (OLD.id, OLD.org_id, OLD.ledger_id, OLD.journal_id, OLD.revision_no, OLD.kind, OLD.accounting_date,
                             OLD.reference, OLD.description, OLD.issues::text, OLD.created_by, OLD.created_at)
                    THEN
                        RETURN NEW;
                    END IF;
                    RAISE EXCEPTION 'journal revisions are immutable except for being superseded' USING ERRCODE = 'insufficient_privilege';
                END $$;
                CREATE TRIGGER journal_revisions_immutable BEFORE UPDATE OR DELETE ON journal_revisions
                    FOR EACH ROW EXECUTE FUNCTION journal_revisions_guard();

                CREATE FUNCTION journal_lines_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'journal lines are immutable' USING ERRCODE = 'insufficient_privilege';
                END $$;
                CREATE TRIGGER journal_lines_immutable BEFORE UPDATE OR DELETE ON journal_lines
                    FOR EACH ROW EXECUTE FUNCTION journal_lines_guard();

                -- Rule 22: at commit, every posted revision has at least two lines summing to exactly zero.
                CREATE FUNCTION ensure_posted_revision_balances() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE
                    revision uuid;
                    revision_status text;
                    total numeric;
                    line_count int;
                BEGIN
                    IF TG_TABLE_NAME = 'journal_lines' THEN
                        revision := NEW.revision_id;
                    ELSE
                        revision := NEW.id;
                    END IF;
                    SELECT status INTO revision_status FROM journal_revisions WHERE id = revision;
                    IF revision_status = 'posted' THEN
                        SELECT COALESCE(SUM(amount), 0), COUNT(*) INTO total, line_count FROM journal_lines WHERE revision_id = revision;
                        IF total <> 0 OR line_count < 2 THEN
                            RAISE EXCEPTION 'posted revision % does not balance', revision
                                USING ERRCODE = 'check_violation', CONSTRAINT = 'posted_revision_balances';
                        END IF;
                    END IF;
                    RETURN NULL;
                END $$;
                CREATE CONSTRAINT TRIGGER journal_lines_balance AFTER INSERT ON journal_lines
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION ensure_posted_revision_balances();
                CREATE CONSTRAINT TRIGGER journal_revisions_balance AFTER INSERT ON journal_revisions
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION ensure_posted_revision_balances();

                -- Rule 37: the effective revision that reports read is always a posted one.
                CREATE FUNCTION ensure_effective_revision_posted() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW.effective_revision_id IS NOT NULL
                        AND (SELECT status FROM journal_revisions WHERE id = NEW.effective_revision_id) IS DISTINCT FROM 'posted'
                    THEN
                        RAISE EXCEPTION 'effective revision of journal % is not posted', NEW.id
                            USING ERRCODE = 'check_violation', CONSTRAINT = 'effective_revision_posted';
                    END IF;
                    RETURN NULL;
                END $$;
                CREATE CONSTRAINT TRIGGER journals_effective_posted AFTER INSERT OR UPDATE ON journals
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION ensure_effective_revision_posted();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER journals_effective_posted ON journals;
                DROP FUNCTION ensure_effective_revision_posted();
                DROP TRIGGER journal_revisions_balance ON journal_revisions;
                DROP TRIGGER journal_lines_balance ON journal_lines;
                DROP FUNCTION ensure_posted_revision_balances();
                DROP TRIGGER journal_lines_immutable ON journal_lines;
                DROP FUNCTION journal_lines_guard();
                DROP TRIGGER journal_revisions_immutable ON journal_revisions;
                DROP FUNCTION journal_revisions_guard();
                ALTER TABLE journals DROP CONSTRAINT fk_journals_current_revision;
                ALTER TABLE journals DROP CONSTRAINT fk_journals_effective_revision;
                """);

            migrationBuilder.DropTable(
                name: "journal_lines");

            migrationBuilder.DropTable(
                name: "journal_revisions");

            migrationBuilder.DropTable(
                name: "journals");

            migrationBuilder.DropColumn(
                name: "last_journal_no",
                table: "ledgers");
        }
    }
}
