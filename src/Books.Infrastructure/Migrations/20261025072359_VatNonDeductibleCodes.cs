using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <summary>
    /// Moves "non-deductible input VAT" from a per-line flag to its own tax code kind.
    ///
    /// Before dropping the old columns it clones every input VAT code that had a non-deductible account into a
    /// vat_input_non_deductible code pointing at that account, and repoints the lines that were saved with
    /// valid_tax_invoice = false to the clone. Lines that were deductible keep the original code.
    /// </summary>
    public partial class VatNonDeductibleCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE tax_codes DROP CONSTRAINT ck_tax_codes_kind;
                ALTER TABLE tax_codes ALTER COLUMN kind TYPE character varying(40);

                -- One non-creditable code per input VAT code that had a non-deductible account.
                CREATE TEMP TABLE vat_code_map (old_id uuid PRIMARY KEY, new_id uuid NOT NULL);
                INSERT INTO vat_code_map (old_id, new_id)
                SELECT id, gen_random_uuid()
                FROM tax_codes
                WHERE kind = 'vat_input' AND non_deductible_account_id IS NOT NULL;

                INSERT INTO tax_codes (id, org_id, ledger_id, name, kind, rate, account_id,
                                       effective_from, effective_to, return_category, active, created_at, version)
                SELECT m.new_id, t.org_id, t.ledger_id, left(t.name, 180) || ' (non-creditable)',
                       'vat_input_non_deductible', t.rate, t.non_deductible_account_id,
                       t.effective_from, t.effective_to, NULL, t.active, now(), 1
                FROM tax_codes t
                JOIN vat_code_map m ON m.old_id = t.id;

                UPDATE purchase_invoice_lines l
                SET tax_code_id = m.new_id
                FROM vat_code_map m
                WHERE l.tax_code_id = m.old_id AND l.valid_tax_invoice = false;

                UPDATE payment_lines l
                SET tax_code_id = m.new_id
                FROM vat_code_map m
                WHERE l.tax_code_id = m.old_id AND l.valid_tax_invoice = false;

                DROP TABLE vat_code_map;

                ALTER TABLE tax_codes DROP CONSTRAINT fk_tax_codes_accounts_ledger_id_non_deductible_account_id;
                DROP INDEX ix_tax_codes_ledger_id_non_deductible_account_id;
                ALTER TABLE tax_codes DROP COLUMN non_deductible_account_id;
                ALTER TABLE purchase_invoice_lines DROP COLUMN valid_tax_invoice;
                ALTER TABLE payment_lines DROP COLUMN valid_tax_invoice;

                ALTER TABLE tax_codes ADD CONSTRAINT ck_tax_codes_kind
                    CHECK (kind IN ('vat_output', 'vat_input', 'vat_input_non_deductible', 'vat_zero_rated', 'non_vat', 'withholding', 'other_tax'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE tax_codes DROP CONSTRAINT ck_tax_codes_kind;

                ALTER TABLE purchase_invoice_lines ADD COLUMN valid_tax_invoice boolean NOT NULL DEFAULT true;
                ALTER TABLE payment_lines ADD COLUMN valid_tax_invoice boolean NOT NULL DEFAULT true;
                ALTER TABLE tax_codes ADD COLUMN non_deductible_account_id uuid;

                -- Best effort: point non-creditable lines back at an input VAT code with the same ledger and rate.
                CREATE TEMP TABLE vat_code_back (new_id uuid PRIMARY KEY, old_id uuid, ledger_id uuid, account_id uuid);
                INSERT INTO vat_code_back (new_id, old_id, ledger_id, account_id)
                SELECT n.id,
                       (SELECT t.id FROM tax_codes t
                        WHERE t.ledger_id = n.ledger_id AND t.kind = 'vat_input' AND t.rate = n.rate
                        ORDER BY t.id LIMIT 1),
                       n.ledger_id, n.account_id
                FROM tax_codes n
                WHERE n.kind = 'vat_input_non_deductible';

                UPDATE purchase_invoice_lines l
                SET valid_tax_invoice = false, tax_code_id = b.old_id
                FROM vat_code_back b
                WHERE l.tax_code_id = b.new_id AND b.old_id IS NOT NULL;

                UPDATE payment_lines l
                SET valid_tax_invoice = false, tax_code_id = b.old_id
                FROM vat_code_back b
                WHERE l.tax_code_id = b.new_id AND b.old_id IS NOT NULL;

                UPDATE purchase_invoice_lines l
                SET valid_tax_invoice = false, tax_code_id = NULL
                FROM vat_code_back b
                WHERE l.tax_code_id = b.new_id AND b.old_id IS NULL;

                UPDATE payment_lines l
                SET valid_tax_invoice = false, tax_code_id = NULL
                FROM vat_code_back b
                WHERE l.tax_code_id = b.new_id AND b.old_id IS NULL;

                UPDATE tax_codes t
                SET non_deductible_account_id = b.account_id
                FROM vat_code_back b
                WHERE t.id = b.old_id AND b.old_id IS NOT NULL;

                DELETE FROM tax_codes WHERE kind = 'vat_input_non_deductible';
                DROP TABLE vat_code_back;

                ALTER TABLE tax_codes ALTER COLUMN kind TYPE character varying(20);
                ALTER TABLE tax_codes ADD CONSTRAINT ck_tax_codes_kind
                    CHECK (kind IN ('vat_output', 'vat_input', 'vat_zero_rated', 'non_vat', 'withholding', 'other_tax'));

                CREATE INDEX ix_tax_codes_ledger_id_non_deductible_account_id
                    ON tax_codes (ledger_id, non_deductible_account_id);
                ALTER TABLE tax_codes ADD CONSTRAINT fk_tax_codes_accounts_ledger_id_non_deductible_account_id
                    FOREIGN KEY (ledger_id, non_deductible_account_id)
                    REFERENCES accounts (ledger_id, id) ON DELETE RESTRICT;
                """);
        }
    }
}
