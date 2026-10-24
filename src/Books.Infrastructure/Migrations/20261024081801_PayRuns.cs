using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PayRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pay_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pay_run_no = table.Column<long>(type: "bigint", nullable: false),
                    period = table.Column<DateOnly>(type: "date", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    issues = table.Column<string>(type: "jsonb", nullable: true),
                    journal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    nssf_expense_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nssf_payable_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    other_deductions_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    salaries_payable_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    salary_expense_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    salary_tax_payable_account_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pay_runs", x => x.id);
                    table.UniqueConstraint("ak_pay_runs_ledger_id_id", x => new { x.ledger_id, x.id });
                    table.CheckConstraint("ck_pay_runs_status", "status IN ('draft', 'posted', 'deleted') AND (status <> 'posted' OR journal_id IS NOT NULL) AND EXTRACT(DAY FROM period) = 1");
                    table.ForeignKey(
                        name: "fk_pay_runs_journals_ledger_id_journal_id",
                        columns: x => new { x.ledger_id, x.journal_id },
                        principalTable: "journals",
                        principalColumns: new[] { "ledger_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pay_runs_ledgers_org_id_ledger_id",
                        columns: x => new { x.org_id, x.ledger_id },
                        principalTable: "ledgers",
                        principalColumns: new[] { "org_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pay_runs_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pay_run_lines",
                columns: table => new
                {
                    pay_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    employee_code = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    resident = table.Column<bool>(type: "boolean", nullable: false),
                    gross = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    fringe_benefits = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    salary_tax = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    fringe_benefit_tax = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    nssf_employee = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    nssf_employer = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    other_deductions = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    net = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pay_run_lines", x => new { x.pay_run_id, x.line_no });
                    table.ForeignKey(
                        name: "fk_pay_run_lines_pay_runs_pay_run_id",
                        column: x => x.pay_run_id,
                        principalTable: "pay_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pay_runs_created_by",
                table: "pay_runs",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_pay_runs_ledger_id_journal_id",
                table: "pay_runs",
                columns: new[] { "ledger_id", "journal_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pay_runs_ledger_id_pay_run_no",
                table: "pay_runs",
                columns: new[] { "ledger_id", "pay_run_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pay_runs_ledger_id_period",
                table: "pay_runs",
                columns: new[] { "ledger_id", "period" });

            migrationBuilder.CreateIndex(
                name: "ix_pay_runs_org_id_ledger_id",
                table: "pay_runs",
                columns: new[] { "org_id", "ledger_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pay_run_lines");

            migrationBuilder.DropTable(
                name: "pay_runs");
        }
    }
}
