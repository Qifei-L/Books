# Companies and Ledgers

Status: Review draft. The organization, company and ledger relationship is confirmed. Accounting settings now follow the delegated [ledger policy baseline](16-ledger-accounting-policy.md); unrelated company/archive proposals remain unconfirmed. No implementation is authorized by this document.

Scope note: Accounting setup, company archiving and the accounting workflows in chapters 04–06 are proposals for a later accounting stage. They do not add endpoints or expand the limited company/ledger operations in the organization foundation's phase-one API contract. Physical deletion remains outside that contract.

## Confirmed

- One organization can manage several companies.
- In the first release, each company has one separate ledger.
- Accounts, journals, balances and transactions belong to a specific ledger.
- An organization does not itself keep books. Its companies are not automatically combined into one set of accounts.
- Organization administrators manage all ledgers in their own organization. Members can access only assigned ledgers, with read-only or edit access for each ledger. Members cannot grant access to anyone else.

Example: Company A and Company B can both use account code 1000. Their journal entries and balances must remain separate.

See [Organizations and Permissions](02-organizations-permissions.md) for the complete access rules.

## Proposed: Company setup

Keep a company name and an internal identity that does not change when the name changes. Do not require a tax number or registration number unless a later business workflow needs it.

Create the company and its single ledger together. A failed request must not leave a usable company without a ledger or create a second ledger on retry.

Only an organization administrator would create or archive a company or change its basic ledger settings. A member's ledger edit access would cover accounting work, not company ownership, organization membership or access grants. This operation split is a proposal within the confirmed permission model, not a new role.

Moving a company between organizations is outside the proposed first release. It must not be treated as an ordinary name change.

## Selected accounting settings

Use the [Ledger accounting policy](16-ledger-accounting-policy.md): explicitly selected base currency and timezone, fiscal start month, monthly periods, date-only accounting and a validated opening date. Base currency cannot be changed through ordinary settings after posting. Supported foreign-currency transactions and automatic FX during one-click Close replace the earlier base-only/no-conversion proposal.

Precision, rounding, opening import boundaries and calculated year-end carryforward are defined in that policy. These are design specifications, not running code.

## Proposed: Changes and historical protection

Allow name corrections, with a history of who changed them. Once a ledger has posted entries, do not directly change its base currency, opening date or financial-year structure in a way that reinterprets those entries. Such changes need a separate, reviewed process.

Archive rather than delete a company with accounting history. Archiving would stop new accounting writes but keep authorized historical reading and reporting available. Restoring an archived company would require an administrator. Archive and restore rules still need approval.

Opening balances would be recorded as a balanced opening journal, not typed into report totals. The first-release opening import boundary and duplicate-history safeguards are selected in the ledger policy; broader historical/midyear imports remain separate scope.

## Proposed: First-release boundary

Queries and reports operate on one ledger at a time. Cross-company consolidation and intercompany eliminations are not included. Ordinary saved-rate conversion and Close-time monetary revaluation follow the selected ledger policy. Any later combined view must define its currency, periods and access rules first.

No report or ledger setting in these documents is claimed to meet a particular country's statutory, tax or filing requirements.

## Review checks

- Two companies with identical account codes never share balances or journals.
- An administrator in Organization A gains no access to Organization B through that role.
- An unassigned member cannot read a ledger by guessing its identifier.
- A retry does not create a second company or ledger for the same creation request.
- Missing accounting settings block the first posting with a clear explanation.
- Proposed archive and setting-change protections preserve historical entries.

These are review scenarios, not test results. Accounting scenarios depend on approval of the proposals above.

## Open Questions

Each ledger's actual base currency, fiscal start month and timezone are setup inputs, not unresolved system rules. Broader historical import remains future scope.

1. Is the proposed administrator-only setup and archive workflow acceptable?

[Business workflow diagrams](11-workflows.md)
[Next: Chart of Accounts](04-chart-of-accounts.md) · [Document index](README.md)
