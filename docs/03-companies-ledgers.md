# Companies and Ledgers

Status: Review draft. The organization, company and ledger relationship is confirmed. The accounting settings below are proposals, not approved rules. No implementation is authorized by this document.

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

## Proposed: Required ledger settings

Before the first posting, record:

- Base currency: the currency used for ledger balances and reports.
- Financial year: its start date and how accounting periods are defined.
- Business timezone: used when suggesting today's accounting date.
- Opening date: when this ledger starts recording activity.

No currency, year start, timezone or decimal precision has been selected. Do not silently use the server's timezone, a calendar year or a two-decimal currency as a business default.

An accounting date is a date chosen for the transaction. It is separate from the time someone entered or posted it. Changing timezone must not move historical accounting dates.

Proposed first-release scope: one base currency per ledger, with no foreign-currency conversion. This still needs approval. If foreign currency is required, specify transaction currency, exchange rates, conversion dates and rounding before developing that workflow.

## Proposed: Changes and historical protection

Allow name corrections, with a history of who changed them. Once a ledger has posted entries, do not directly change its base currency, opening date or financial-year structure in a way that reinterprets those entries. Such changes need a separate, reviewed process.

Archive rather than delete a company with accounting history. Archiving would stop new accounting writes but keep authorized historical reading and reporting available. Restoring an archived company would require an administrator. Archive and restore rules still need approval.

Opening balances would be recorded as a balanced opening journal, not typed into report totals. Whether previous periods must also be imported is an open business choice.

## Proposed: First-release boundary

Queries and reports operate on one ledger at a time. Cross-company consolidation, intercompany eliminations and automatic currency conversion are not included in this proposal. Any later combined view must define its currency, periods and access rules first.

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

1. What base currency, financial-year start and business timezone does each ledger need? Are periods monthly?
2. Is single-currency accounting sufficient for the first release?
3. Should setup accept opening balances only, or also import historical transactions?
4. Is the proposed administrator-only setup and archive workflow acceptable?

[Business workflow diagrams](11-workflows.md)
[Next: Chart of Accounts](04-chart-of-accounts.md) · [Document index](README.md)
