# Architecture

## Shape

One ASP.NET Core application (modular monolith) and one PostgreSQL database.

| Module | Owns |
| --- | --- |
| Access | Users, identities, organizations, memberships, invitations, grants, MCP delegations |
| Ledger | Ledgers, accounts, parties, tax codes, bank accounts |
| Journal | Journals, revisions, lines, posting service, Close |
| Documents | Purchase, sales, payment, receipt, transfer documents; allocations; posting adapters |
| Bank | Statements, statement lines, matches, reconciliations |
| Tax | Tax entries, tax periods, filed snapshots, tax reports |
| Reporting | TB, GL Detail, Balance Sheet, P&L, aging, statements |
| Mcp | MCP tools that call the same application services as HTTP |

EF Core handles ordinary CRUD. Posting and reporting use explicit SQL through Npgsql, because their correctness depends on locks, constraints and exact queries. Authentication uses an external OIDC provider; Books stores only `issuer + subject`.

## Core data model

```sql
ledgers(id, org_id, company_id UNIQUE, base_currency, fy_start_month,
        closed_through date NULL, version)

accounts(id, ledger_id, parent_id, code, name, type, is_leaf,
         is_control, is_bank, currency NULL, active,
         UNIQUE(ledger_id, code))

journals(id, ledger_id, journal_no, source_kind NULL, source_id NULL,
         effective_revision_id NULL, version,
         UNIQUE(ledger_id, journal_no), UNIQUE(source_kind, source_id))

journal_revisions(id, journal_id, kind,        -- manual | document | opening | fx_reval
         status,                                -- draft | error | posted | superseded | deleted
         accounting_date, currency, rate, rate_source,
         issues jsonb, created_by, created_at, superseded_at NULL)

journal_lines(revision_id, line_id, account_id, party_id NULL, item_id NULL,
         qty NULL, description,
         amount_txn numeric(20,4),              -- signed, debit positive
         amount_base numeric(20,4))
```

- Revisions are never updated after they are written, except for the `superseded_at` marker. An edit writes a new revision and moves `effective_revision_id`. History and "as known at time T" reports come from revisions.
- One signed amount column instead of debit/credit columns. Balance means `SUM(amount_base) = 0`, and for normal kinds also `SUM(amount_txn) = 0`. A deferred constraint trigger enforces it for posted revisions.
- `line_id` is stable across revisions, so bank matches and tax entries can follow a line.

## Posting flow (one transaction)

```
BEGIN
  SELECT ledger FOR UPDATE                 -- serializes writes per ledger
  check current access (membership + grant)
  check idempotency key (unique row; replay returns the saved result)
  check expected version
  validate (pure function): dates vs closed_through (old and new), leaf and active accounts,
        control account ⇒ party, currency and precision, balance, document rules
  ok    → insert posted revision + lines, move pointer, write tax entries, audit, receipt
  error → insert error revision with issues; effective posting untouched
COMMIT
```

Validation runs before any write, so a validation failure still commits the draft and its errors in the same transaction. Only infrastructure failures roll back. The client then retries with the same idempotency key, and that retry is how Pending outcomes get resolved.

Validation rules are plain C# classes (`IJournalRule`) driven by account flags. There is no rule engine until a second real need appears.

## Documents

Each document type has its own tables (for example `purchase_invoices`, `purchase_invoice_lines`) and a `PostingAdapter` that turns the document into journal lines. Saving a document writes the document version and calls the posting service in the same transaction. One document produces one journal.

```
purchase invoice:  Dr expense/asset (net) · Dr input VAT · Cr AP (gross, party)
                   Cr WHT payable / Dr AP when withholding applies
payment:           Dr AP (party) · Cr bank      or  Dr expense · Cr bank (direct)
transfer:          Dr to-bank · Cr from-bank (same currency in v1)
```

- `allocations(payment_id, invoice_id, amount)`: an invoice's open balance is derived from this table.
- Tax is a line attribute on documents (`tax_code_id`, `tax_amount`). On the journal, tax becomes separate lines summed by tax account, with `source_line_id` pointing back to the document line.

## Bank reconciliation

```
bank_statements(id, bank_account_id, period_from, period_to, opening, closing)
bank_statement_lines(id, statement_id, date, amount, description, reference, status)
bank_matches(statement_line_id, source_kind, source_id, line_id, amount)
```

Import CSV with a per-bank column mapping and duplicate detection. The system suggests matches by amount, date within ±3 days and reference. A user can create a receipt or payment directly from an unmatched statement line. A reconciliation is complete only when book balance ± outstanding items = statement balance.

## Tax module

```
tax_entries(ledger_id, tax_code_id, kind,          -- vat_input | vat_output | wht
            taxable_base, tax_amount, tax_amount_khr, khr_rate, khr_rate_date,
            accounting_date, source_kind, source_id, source_line_id, party_id)
tax_periods(ledger_id, period, status, filed_at, snapshot jsonb)
```

Tax entries are written in the posting transaction and are rebuildable from effective documents. The tax module reads them; it never edits documents. Filing a period stores a snapshot, and later changes to that period are reported as differences. See [Cambodia tax](04-cambodia-tax.md).

## Reports

All reports are SQL over the effective lines (`journals.effective_revision_id`) and run in one `REPEATABLE READ` read-only transaction, so totals and drilldown agree. Year-end needs no closing entries: Retained Earnings = posted Retained Earnings + P&L before the current fiscal year start; Current Year Profit = P&L from the fiscal year start to the report date.

## MCP

Uses the official C# MCP SDK in the same process. Tools receive the verified principal and call application services directly. Effective access = token scopes ∩ the person's current access ∩ delegation limits. Updates are partial (PATCH semantics); an omitted field is never cleared.

## Testing

- Testcontainers PostgreSQL. No EF InMemory for anything involving transactions.
- Property tests generate random journals and edits, then check the **[T]** invariants in [Domain rules](02-domain-rules.md).
- Race tests use real connections and barriers: double submit, edit vs Close, access revocation vs write, delete vs reference.
