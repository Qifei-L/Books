using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ManualControlAndStateCharge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_tax_codes_kind",
                table: "tax_codes");

            migrationBuilder.DropIndex(
                name: "ix_classes_ledger_id_name",
                table: "classes");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tax_codes_kind",
                table: "tax_codes",
                sql: "kind IN ('vat_output', 'vat_input', 'vat_input_non_deductible', 'vat_zero_rated', 'non_vat', 'withholding', 'other_tax', 'state_charge')");

            migrationBuilder.CreateIndex(
                name: "ix_classes_ledger_id_name",
                table: "classes",
                columns: new[] { "ledger_id", "name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_tax_codes_kind",
                table: "tax_codes");

            migrationBuilder.DropIndex(
                name: "ix_classes_ledger_id_name",
                table: "classes");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tax_codes_kind",
                table: "tax_codes",
                sql: "kind IN ('vat_output', 'vat_input', 'vat_input_non_deductible', 'vat_zero_rated', 'non_vat', 'withholding', 'other_tax')");

            migrationBuilder.CreateIndex(
                name: "ix_classes_ledger_id_name",
                table: "classes",
                columns: new[] { "ledger_id", "name" });
        }
    }
}
