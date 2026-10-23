# Balances and Reports

Status: Requirements review draft. The four reports, their core fields and statement layout below are confirmed. Technical mechanisms and explicitly marked proposals still need design and validation. No statutory reporting compliance is claimed.

These reports belong to a later accounting stage. They do not expand the organization foundation's phase-one API contract.

## Confirmed

Reports belong to a specific ledger. Read-only members can read authorized ledger information; they cannot change it. Organization administrators can access their own organization's ledgers, not another organization's data. Neither a report link nor an export may bypass ledger access.

There is no automatic consolidated report for all companies in an organization.

The P&L uses two top-level groups: **Income (add)** and **Cost & Expense (subtract)**. There is no separate top-level Cost group. This confirms the grouping, not every calculation, closing rule or report feature below.

## Confirmed: First-release reports

Use four reports. **Trial Balance (TB)** is the account summary; do not create a separate GL Summary.

### Trial Balance (TB)

One row per account, with Code, Name, Opening Debit/Credit, Period Debit/Credit and Closing Debit/Credit. Show debit/credit totals and a balance check for the full ledger. A selected-account subtotal is not a full-ledger balance check. Click an account to open GL Detail.

### General Ledger Detail (GL Detail)

Show Accounting Date, Journal No., Reference, Description, Debit, Credit and Running Balance. Include the opening balance and period totals. Each movement links to its journal.

### Balance Sheet

Use an **As of Date**. Show:

- Assets: Current Assets, Non-current Assets and Total Assets.
- Liabilities: Current Liabilities, Non-current Liabilities and Total Liabilities.
- Equity: Share Capital, Retained Earnings, Current Year Profit / Loss and Total Equity.
- Check: Total Assets = Total Liabilities + Total Equity.

Expand a group to its accounts, then open GL Detail. Current Year Profit / Loss is calculated, not an ordinary posting account, and must not duplicate amounts already included in Retained Earnings. The selected [ledger policy](16-ledger-accounting-policy.md) defines calculated year-end carryforward and the common reconciliation algorithm.

### Profit & Loss (P&L)

Use a From/To date range. Show **Income (add)** minus **Cost & Expense (subtract)** to calculate profit or loss. Groups can be expanded to accounts and their GL Detail. Preserve signed reductions and contra balances.

Report totals use the ledger's base currency. Journal list and journal detail remain supporting views, not a fifth summary report.

Cash-flow statements, budgets, tax returns, comparative periods, segment reporting and consolidated accounts remain outside the proposed first release. Year-end processing now has a selected design baseline, not an implemented feature. Export and presentation options below remain proposals.

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

For the five accepted account types, the proposed display convention is:

- Assets and Cost & Expense normally display debit-minus-credit amounts.
- Liabilities, equity and income normally display credit-minus-debit amounts.
- An opposite balance stays visible; do not hide it or force it to zero.
- P&L profit is signed Income less signed Cost & Expense for the selected range.

The add/subtract groups are not debit/credit labels. Preserve contra movements and opposite balances. For example, Income of 100 with an income reduction of 10 gives net Income of 90. Cost & Expense of 40 with a refund of 5 gives net Cost & Expense of 35. Proposed presentation: profit is 90 minus 35, or 55. Do not take absolute values and turn reductions into extra income or expense.

Current Year Profit / Loss is confirmed as a calculated report value, not an ordinary posting account. The accepted Retained Earnings purpose and other system-function mappings are described in [Chart of Accounts](04-chart-of-accounts.md#system-accounts-and-default-accounts). Acceptance of a purpose does not settle year-end calculations or create accounts.

A balance sheet also needs a defined treatment of current-year profit, previous-year results and closing entries. Use the selected calculated carryforward algorithm in the ledger policy, not the earlier unresolved year-end proposal. Do not add an unexplained “balancing figure,” double-count transferred profit, or label an unbalanced result as final. The layout above is confirmed, and the selected carryforward calculation must be tested before claiming a complete implementation; TB and GL Detail remain reconciliation references.

Report classification follows the account type and hierarchy. Fixing the type and parent of used accounts is proposed in [Chart of Accounts](04-chart-of-accounts.md) to avoid silent changes to earlier classifications.

## Proposed: Drilldown behavior

Engineering should carry the selected organization, ledger, account or group, dates and measure into each drilldown. These are business meanings, not approved API paths or payload fields.

- A period Debit value opens the matching debit movements, not all movements.
- A period Credit value opens the matching credit movements.
- A closing balance opens a reconciliation of opening balance plus period movements, not just the period's lines.
- A statement group expands to its accounts without counting descendants twice.
- The server rechecks access. A link or client-supplied filter never grants permission.
- Keep the report and its detail on the same data revision where supported. Otherwise explain that data changed and refresh the related results; do not silently display nonmatching totals.

The exact filter contract, snapshots and navigation remain engineering proposals. No Frontend or API implementation is approved.

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
- Current-year profit and closing entries do not enter equity twice; validate the detailed calculation against the confirmed layout.

These checks have not been executed. Proposed accounting behavior must be reviewed before implementation.

## Open Questions

1. Are CSV exports enough, or are Excel and PDF required?
2. Should zero-balance accounts appear by default? Are comparative periods or saved historical report snapshots needed?

[Document index](README.md)

## Foreign-currency revaluation

The confirmed [FX workflow](15-foreign-currency.md) produces official base-currency Journal adjustments without changing original-currency principal. Include each effective adjustment once in TB, GL Detail, Balance Sheet and P&L. Proposed presentation: disclose stale downstream valuations caused by historical edits and link to the affected sources and adjustments; do not present those valuations as reviewed/current or silently patch report totals. Detailed reprocessing and reporting status controls still need design.

## Selected year-end reconciliation

The [canonical ledger calculation](16-ledger-accounting-policy.md#3-opening-balances-and-one-year-end-calculation) supersedes a raw-postings-only formula at fiscal boundaries. TB and GL include clearly labelled calculated carryforward rows; actual posting movements remain distinguishable from derived transfers. P&L excludes virtual transfers. Balance Sheet shows direct Retained Earnings plus prior-year results and current-year profit once. Imports cannot duplicate pre-opening profit or physical closing entries. These are selected design rules; tests remain Not run.
