# Ledger accounting policy

Status: Design baseline selected on 2026-10-23 under the owner's instruction to choose precision and fiscal-year rules. One-click Close with automatic FX revaluation is confirmed. Exact storage, algorithms and controls below are selected technical proposals, not implemented or tested.

This page supersedes earlier open proposals on precision, fiscal periods, accounting-date representation, year-end carryforward and separate revaluation before Close. It does not approve coding or deployment.

## 1. Ledger setup

Each ledger explicitly selects a base currency, a supported business timezone, a fiscal-year start month (1–12), and an opening accounting date. There is no implicit USD, server-timezone or calendar-year default.

Use one base currency. After the first effective posting, ordinary settings edits cannot change it. Base-currency migration is a separate future operation. Freeze opening date and fiscal start month after first posting as well; do not reinterpret history through settings edits.

Periods are calendar months. A fiscal year starts on the first day of the selected month and ends immediately before the next year's start. First-release opening-balance setup starts on a fiscal-year boundary. Midyear migration with year-to-date activity requires a separately validated import design, not a fabricated full-year result.

Reject effective postings before the ledger opening date O; do not admit earlier Income/Cost & Expense activity outside the carryforward calculation. Imported earlier history requires a separate validated migration policy.

Accounting date is C# DateOnly / PostgreSQL date. Close is an inclusive accounting date per ledger. Dates entered explicitly are not converted through timezone. Use the ledger timezone only to suggest today's date; store audit timestamps in UTC. Changing timezone must not move historical dates.

## 2. Amounts, rates and rounding

Selected storage baseline:

| Value | Proposed representation |
| --- | --- |
| Posted transaction and base amounts | PostgreSQL numeric(20,4); C# decimal; JSON decimal strings |
| Exchange rate | numeric(20,10), positive; base-currency units per one transaction-currency unit |
| Accounting date / ClosedThroughDate | date / nullable date |
| Fiscal year start | integer month 1–12 |
| Currency minor units | Versioned supported-currency configuration, 0–4 decimal places |

Support only currencies whose adopted minor-unit precision fits this baseline; do not silently truncate others. For example, JPY uses zero minor units and USD uses two. A base-currency transaction has rate 1. Rates are explicit saved evidence, with Manual source for manual input. Do not infer a missing market rate.

Validate scale and range before database assignment. PostgreSQL numeric coercion must not be used to quietly round invalid supplied amounts. Normal submitted transaction/base values must have no nonzero digits beyond their currency's supported minor units. Reject invalid precision or overflow with a field error.

Use exact scaled-integer/rational working arithmetic for multiplication and allocation (for example, System.Numerics.BigInteger coefficients with explicit scales), then round once to the target currency minor units and check the final storage range. C# decimal remains the DTO/storage value type, not an assumption that every intermediate product is exact: numeric(20,4) multiplied by numeric(20,10) can exceed decimal's coefficient precision. Never silently reduce intermediate scale or use binary floating point for money. Round derived monetary values to the target currency's minor units using MidpointAwayFromZero: USD 1.005 becomes 1.01; -1.005 becomes -1.01. Apply sign-aware rounding before converting signed calculations into debit/credit sides.

### Deterministic conversion and exact balance

A normal Journal uses one transaction currency and one shared positive rate. Persist explicit transaction debit/credit and base debit/credit on every official line. The server checks or derives base amounts using the saved rate and selected rounding policy; client-supplied totals cannot override this calculation.

Transaction debits equal transaction credits exactly at transaction precision. Base debits equal base credits exactly at base precision. Each nonzero ordinary line uses one side, not both. There is no arbitrary balance tolerance.

For first release, if individually rounded converted lines leave a nonzero base imbalance, return a visible rounding error and retain the candidate. Do not automatically add a plug or silently change a supplied line. Automatic rounding-difference postings are not enabled by this baseline. The earlier conditional Rounding Differences system purpose remains available for a separately defined future rule. This deliberately favors visible correction over concealed source errors.

A validated system FX adjustment is the explicit exception: original-currency movement is zero while base movements are nonzero and exactly balanced. Do not fake a rate or foreign principal. Rate evidence explains its valuation calculation, not zero multiplied into a nonzero posting.

### Proposed persisted constraints

Official Journal lines store non-null transaction_debit, transaction_credit, base_debit and base_credit, each nonnegative numeric(20,4). Enforce at most one nonzero side per currency pair. Normal lines have matching transaction/base directions; explicit generated FX kinds have transaction amounts zero and at least one nonzero base side. Keep organization/ledger composite foreign keys and stable line/revision identity.

Use deferred database validation of each effective Journal's transaction and base debit/credit sums within the official-effects transaction. Check currency minor-unit scale and kind eligibility before narrowing a candidate into numeric(20,4); the application must not let database coercion erase invalid source precision. Only validated official state enters the reporting source. Saved invalid candidates remain separately representable.

The finite stored maximum is 9,999,999,999,999,999.9999 before currency-specific minor-unit validation; rates allow up to 9,999,999,999.9999999999 and must be greater than zero. Enforce stricter range limits if aggregate/derived results cannot fit; never clamp. Fiscal month is checked 1–12, accounting date is not earlier than opening date, and the ordinary Close command is monotonic.

## 3. Opening balances and one year-end calculation

Opening entries are balanced official Journals tagged as opening entries at the ledger's opening date. They contain balance-sheet accounts only. Imported pre-opening profit is already included in Retained Earnings; imported Income and Cost & Expense opening balances must be zero. Opening Balance Clearing must net to zero before setup is accepted. Do not import both the same opening balances and their already-included historical transactions.

No automatic physical profit-clearing Journal is generated at year end. Use one canonical reporting transformation for TB, GL, Balance Sheet and P&L. Never maintain separate manually editable report balances.

### Canonical calculation

Use signed debit-minus-credit amounts from effective official postings only. Let O be the opening date and F the fiscal-year start containing the report date D.

- For each balance-sheet account other than the mapped Retained Earnings account, carry forward all effective movements through D.
- For each Income or Cost & Expense account, the closing balance is its effective movement from F through D.
- Compute prior profit as the negative of the sum of signed Income and Cost & Expense movements from O up to, but excluding, F.
- The Retained Earnings signed closing balance is its direct effective posted balance through D **minus prior profit**. Its imported opening balance already includes pre-O profit, which is not counted again.
- Current-year profit is the negative of signed Income and Cost & Expense movements from F through D. Show it as calculated equity on the Balance Sheet, not a posting account.
- P&L reports actual Income and Cost & Expense movements for the requested date range. It does not include virtual carryforward transfers.

Implement this as a single derived event stream: at each fiscal start, create reporting-only transfer events that zero the previous fiscal year's Income/Cost & Expense balances and transfer their net signed balance to Retained Earnings. These are not stored Journals and never acquire a Journal No. The formulas above and the event stream must reconcile.

TB opening plus period movement equals closing using this same stream. GL shows derived transfer rows labelled **Calculated year-end carryforward**, with a link to the source-period calculation instead of a fake Journal. Keep actual posted debit/credit movement separate from calculated transfer columns; a displayed combined movement is explicitly labelled. Balance Sheet includes current-year profit once, while TB already includes its underlying Income/Cost & Expense balances.

This calculation naturally covers profits and losses. Example: before year end, net Income/Cost & Expense has a credit balance of 10. At the next fiscal start, its calculated transfer totals a debit of 10 and Retained Earnings a credit of 10. Total debits and credits remain equal. No second physical closing posting is added.

### Prevent double counting

Do not accept imported or manually prepared year-end clearing entries as ordinary unclassified transactions on top of this calculated policy. First-release policy rejects recognized closing-transfer imports and requires reconciliation of opening data. Require a clear operation purpose and review ambiguous Income/Cost & Expense-to-Retained Earnings entries; account names alone cannot reliably identify intent.

Normal justified accounting corrections remain possible subject to permissions and Close. A future physical-closing mode would need an explicit alternative policy; it must not run alongside calculated carryforward. Retained Earnings direct capital adjustments are not automatically treated as closing transfers.

## 4. One-click Close

An organization administrator selects the ledger and cutoff date, then clicks **Close** once. The operation automatically performs required FX revaluation and advances the cutoff together. No separate revaluation confirmation is required. An optional preview is informational, not an extra mandatory step.

The cutoff may advance or repeat, never move backwards through this operation. An equal cutoff returns the saved completed result without recalculating or posting, even if rates have since changed. Only a cutoff strictly later than the current ClosedThroughDate may generate new adjustments. A correction to a closed valuation requires the separately controlled reopening process. Earlier dates require the separate, not-yet-defined reopening workflow.

Required trusted, user-provided valuation rates must exist for the chosen date and every non-base exposure currency. Missing or invalid rates block the whole Close with an actionable list. Do not guess, use an unstated fallback date or fetch a new rate without an adopted source policy. Rate creation/editing remains a separate authorized action.

### Proposed atomic transaction

1. Preserve the Close request identity, selected date and candidate evidence without changing official books.
2. Acquire the common organization authorization and ledger operation locks in the existing deterministic order. Recheck current administrator membership, ledger status and current cutoff.
3. Read current effective postings and monetary settings as of the selected date. Include effective adjustments already posted on that date; exclude future-dated activity.
4. Check rates, system accounts, source versions and required details. Calculate incremental adjustments for all eligible currency groups.
5. Commit **all** Close-generated FX Journals, exposure links, the advanced cutoff, success receipt and audit record in one database transaction.
6. On definite failure, commit none of those official effects. Keep the prior cutoff and books unchanged; retain the candidate and error separately. Unknown outcome is Pending until the saved result is reconciled.

This all-or-nothing Close rule supersedes the earlier independent per-currency batch proposal. Generate one exposure-currency Journal per group as needed, but commit the entire Close together. A zero-delta group needs no zero-value Journal.

Concurrent posting, editing, deleting, monetary-setting changes, rate updates and Close share the ledger serialization boundary. A posting either commits before Close and is included, or validates after Close and is rejected if its accounting date is at/before the cutoff. No race may insert an unvalued prior-date posting behind a successful Close.

Idempotency protects both retries with the same key and concurrent requests with different keys. Recompute against the locked current state rather than trusting a cached preview. A failed request does not consume a successful Close or block a corrected request forever; exact receipt retention/recovery follows the technical contract.

## 5. FX scope and system accounts

Classify monetary exposure explicitly. A foreign-currency amount alone does not qualify an account. Initial grouping is ledger + Account + transaction currency + Party where present. Aggregate effective original principal and base carrying value as of the date within that group. Do not net different accounts, currencies or Parties to hide exposure.

Use one FX Gain purpose and one FX Loss purpose in the first version. Do not split realized and unrealized accounts. Both revaluation and later settlement use this pair with traceable operation purposes. Existing split tree examples are superseded, not automatic account-creation instructions.

System adjustments link to the valued group and its source evidence. Balance-level revaluation does not require an invoice-allocation engine. Detailed invoice matching and partial settlement remain future subsystem design, while settlement must still consider existing carrying value and cannot invent foreign principal.

Valid manual revaluation may remain available for an open date; Close does not require it and only posts any remaining difference. No automatic daily job or next-month reversal is introduced.

## 6. Closed history and dependent results

At or before ClosedThroughDate, prohibit new posting and changes/deletion of effective accounting. For a date change, check both original and target accounting dates. Draft saving does not authorize effective posting.

Reopening authority and workflow remain open. Do not implement an implicit backward cutoff edit. A future controlled reopen/correction must recalculate dependent revaluation, settlement and calculated carryforward results before declaring the affected books current.

Open-period historical changes can still invalidate later open-period valuations. Flag these dependencies and avoid certifying stale results. Successful Close must not silently certify known stale downstream results within its cutoff; recalculate them through a validated correction process or block with an actionable error. The exact dependency rebuild process remains a technical proposal, not permission to delete past adjustments.

## 7. Acceptance checklist

All checks are **Not run**.

- USD 1.005 and -1.005 derived amounts round to 1.01 and -1.01; supplied excess-precision amounts are rejected.
- JPY posted values have zero fractional minor units; unsupported currency scale is rejected rather than truncated.
- Exact high-precision intermediate arithmetic rounds once; final range overflow produces Error, not partial posting or silent intermediate rounding.
- Unbalanced source lines and rounding-induced base imbalance are rejected without a hidden tolerance or automatic plug.
- An ordinary Journal balances in both currencies; a validated base-only FX adjustment leaves original units unchanged.
- EUR 100 carrying USD 110 closes at target USD 112 with one +2 adjustment; repeated Close adds nothing.
- Missing one currency's rate or failing the last adjustment leaves all FX groups and cutoff unchanged.
- Simultaneous Close requests cannot double-post; concurrent prior-date posting is included or rejected under the lock.
- Company A's Close cannot affect Company B; revoked administrators cannot close.
- Fiscal boundary rolls prior P&L into calculated Retained Earnings once; TB and GL transfer rows reconcile to Balance Sheet.
- A fiscal loss carries forward with the correct sign. P&L does not contain virtual clearing movements.
- Opening import already includes prior profit in Retained Earnings; no duplicate imported closing entries or activity.
- Date inputs remain unchanged by timezone; ledger-local date defaults and UTC audit timestamps are distinct.
- Future-dated postings do not affect an earlier Close valuation; postings before ledger opening date are rejected.
- A crash after commit returns the original successful result after reconciliation; Pending is not blindly retried.
- Reopening is unavailable until its authority and controlled correction contract are defined.

## Why these choices

Currency minor units and explicit rounding make amounts predictable. Date-only accounting avoids hidden midnight conversions. Calculated year-end carryforward avoids a second set of clearing entries, but requires one shared reporting algorithm and explicit import validation. One atomic ledger Close makes the user's single action match one official outcome.

The next step is implementation planning and tests after separate authorization, not a claim that these controls already run.

[Companies and ledgers](03-companies-ledgers.md) · [FX workflow](15-foreign-currency.md) · [Reports](06-reports.md) · [Documentation index](README.md)
