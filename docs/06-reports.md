# Balances and Reports

Status: Review draft. Ledger access, separation, the Cost & Expense label and the two P&L groups are confirmed. Report scope, calculations and presentation below are proposed and need approval. No statutory reporting compliance is claimed.

These reports are proposed for a later accounting stage. They do not expand the organization foundation's phase-one API contract.

## Confirmed

Reports belong to a specific ledger. Read-only members can read authorized ledger information; they cannot change it. Organization administrators can access their own organization's ledgers, not another organization's data. Neither a report link nor an export may bypass ledger access.

There is no automatic consolidated report for all companies in an organization.

The P&L uses two top-level groups: **Income (add)** and **Cost & Expense (subtract)**. There is no separate top-level Cost group. This confirms the grouping, not every calculation, closing rule or report feature below.

## Proposed: First-release reports

- Journal list and journal detail, with status, accounting date and posting reference.
- Account transaction detail: opening balance, individual movements and closing balance.
- Trial balance: opening balances, period debits and credits, and closing balances by account.
- Balance sheet: assets, liabilities and equity at a selected date.
- Income statement: Income and Cost & Expense for a selected date range.

Cash-flow statements, budgets, tax returns, comparative periods, segment reporting and consolidated accounts are outside this proposed first release. The proposed balance sheet and income statement need an approved classification and year-end policy before they can be accepted as complete features.

## Proposed: One consistent accounting source

Use the currently effective posted journal lines as the source of accounting amounts. Under the confirmed Close-based policy, a successful edit replaces the old accounting effect and a successful deletion removes it. Do not count superseded or deleted versions merely because they are retained in audit history. A rejected edit leaves the previous valid posted effect unchanged. Drafts and cancelled drafts do not count. Do not allow users to edit a balance or report total independently of a journal.

If optional reversals are adopted, effective original journals and posted reversals count on their respective accounting dates. A “reversed” label does not remove the original amount.

Account name changes must not break references. Inactive accounts still appear when they have relevant balances or movements. Group totals include descendants once only; a parent total must not be added to its children again in the grand total.

## Proposed: Date ranges and balances

For a report from date S through date E, both dates are included:

- Opening balance: posted movements with an accounting date before S.
- Period movement: posted movements dated S through E.
- Closing balance: opening balance plus period movement.

For reconciliation, signed movement means debit minus credit. A positive signed balance is a debit balance; a negative signed balance is a credit balance. User-facing reports label the side instead of relying on an unexplained minus sign.

Example: an account opens with a debit balance of 100, then has debits of 30 and credits of 20. It closes with a debit balance of 110.

A cutoff report includes all posted movements with an accounting date on or before the selected date. Posting time is not a substitute for accounting date. If a backdated journal is posted later, rerunning an unlocked historical period may legitimately change its result; the report must show when it was generated.

Opening-balance journals enter the same calculations as other posted entries. Do not add a separate opening-balance table to these totals and count the same amount twice.

## Proposed: Statement calculations

The trial balance must reconcile total debits and credits for movements and for closing balances. Its amounts must agree with account detail for the same ledger, dates and reporting snapshot.

For the proposed five account types:

- Assets and Cost & Expense normally display debit-minus-credit amounts.
- Liabilities, equity and income normally display credit-minus-debit amounts.
- An opposite balance stays visible; do not hide it or force it to zero.
- P&L profit is signed Income less signed Cost & Expense for the selected range.

The add/subtract groups are not debit/credit labels. Preserve contra movements and opposite balances. For example, Income of 100 with an income reduction of 10 gives net Income of 90. Cost & Expense of 40 with a refund of 5 gives net Cost & Expense of 35. Proposed presentation: profit is 90 minus 35, or 55. Do not take absolute values and turn reductions into extra income or expense.

Current Year Profit is proposed as a calculated report value, not an ordinary posting account. The accepted Retained Earnings purpose and other system-function mappings are described in [Chart of Accounts](04-chart-of-accounts.md#system-accounts-and-default-accounts). Acceptance of a purpose does not settle year-end calculations or create accounts.

A balance sheet also needs a defined treatment of current-year profit, previous-year results and closing entries. Those rules are not yet approved. Do not add an unexplained “balancing figure,” double-count transferred profit, or label an unbalanced result as final. Until the year-end and equity presentation rules are agreed, treat statement layouts as review drafts; trial balance and detail remain the reconciliation references.

Report classification follows the account type and hierarchy. Fixing the type and parent of used accounts is proposed in [Chart of Accounts](04-chart-of-accounts.md) to avoid silent changes to earlier classifications.

## Proposed: Filters, pagination and exports

Always display ledger, currency, date range or cutoff date, applied filters and generation time. Show a clear empty result, not an invented zero balance, when no rows match a filter.

Allow filtering by account and journal reference, with an explicit option to include zero-balance accounts. Account detail uses stable ordering: accounting date, posting reference and line order. Pagination must not change totals or skip or repeat rows.

Totals cover the full filtered result, not just the visible page. A selected-account subtotal is labelled as such; it is not presented as a full-ledger trial balance.

Propose CSV as the first export format. Export all rows for the selected scope, with the same precision, filters and totals as the report. Spreadsheet interpretation must not turn user-entered labels into executable formulas. Excel and PDF layouts remain open choices.

Reading or exporting never grants access to someone else. Permission checks apply when creating an export and when downloading it. If background generation is added, revoked access must block later download.

## Proposed: Consistent results during concurrent activity

A report's totals and detail must represent one consistent set of posted data. Posting during a report request must not appear in the total but be missing from its detail, or vice versa.

If results span several pages or an export, keep the same snapshot or clearly require the user to refresh the entire result. Do not quietly mix earlier and later results. The technical method belongs in the design documents.

If cached summaries are introduced later, they must be rebuildable from posted entries and reconcilable to them. A mismatch is an error to investigate, not an amount to patch manually in the report.

## Suspense, Error and Pending visibility

Proposed presentation: show pending issues separately from accounting totals. Only successfully posted entries affect ledger balances and reports, including valid entries posted to a Suspense Account. The presence of a record in an Error review list is not evidence of posting.

Unposted invalid records have no balance effect. For Pending / Unknown processing outcomes, reconcile against the posted accounting source; never infer totals from an error or timeout flag. Show unresolved review items clearly without inventing balances or claiming the books are complete. Any issue count or detail must obey the same ledger access boundary.

## Review checks

- Drafts never change balances; posting changes all affected views consistently.
- Opening plus movement equals closing for every account.
- Trial balance totals match journal and account detail under the same scope.
- A later-period reversal leaves the original period's accounting movement intact.
- Inactive accounts remain included; parent and child totals are not double-counted.
- Full-result totals and exports match across pagination and concurrent posting.
- Cross-ledger identifiers and revoked permissions cannot expose report or export data.
- Current-year profit and closing entries do not enter equity twice once that policy is approved.

These checks have not been executed. Proposed accounting behavior must be reviewed before implementation.

## Open Questions

1. Which of the proposed reports are required for the first release?
2. What year-end, retained-earnings and current-year-profit rules should the statements use?
3. Are CSV exports enough, or are Excel and PDF required?
4. Should zero-balance accounts appear by default? Are comparative periods or saved historical report snapshots needed?

[Document index](README.md)
