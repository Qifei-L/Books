# Foreign currency and revaluation

Status: The business behavior marked Confirmed below was approved on 2026-10-23. Technical mechanisms are proposals. No implementation or runtime tests have been completed.

## Confirmed: Normal transactions

A normal Journal has one transaction currency and one shared exchange rate. Save original-currency amounts and their base-currency values. Manual rates retain backend source information labelled Manual. This does not approve mixed-currency ordinary journals.

Daily transactions use their saved conversion information. Revaluation is a separate action initiated by a person, not an automatic daily rewrite of the books.

## Confirmed: Revaluation workflow

1. Select the ledger, accounting date and rates for the relevant currencies.
2. Preview eligible foreign-currency monetary balances and the change in their base-currency carrying values.
3. Confirm the preview to generate linked system Journal adjustments.
4. Retain the revaluation in the books. The first version does not automatically reverse it next month.

Revalue explicitly identified monetary exposures, not every account containing a foreign-currency amount. Historical-cost non-monetary items are not included in this workflow. Hedging and consolidation translation are outside this scope.

A revaluation changes only base-currency value. It does not change the original-currency principal. Base-currency debits and credits must balance. The generated Journal links to the balances or open source items being valued.

## Confirmed: Later settlement and repeat processing

Settlement uses the current base-currency carrying value, including prior effective revaluation, rather than starting again from the original transaction rate. Keep source and exposure links so the same balance is not valued or settled twice.

Repeated submission must not repeat the accounting effect. If the target value and current carrying value are equal, the adjustment is zero. If rates or source balances change, calculate the new difference, not the whole target value again.

A date at or before Close cannot receive a revaluation posting. Changes to earlier records that affect later revaluation must identify the affected results and prompt reprocessing. Revaluation does not silently reverse the already confirmed permission to edit open-period postings.

## Example: EUR receivable, USD base currency

An outstanding EUR 100 receivable was recorded at USD 110.

- At the selected revaluation date, its target value is USD 112.
- Post a base-only adjustment: debit Receivable USD 2; credit FX Gain USD 2. The EUR principal movement is zero.
- Repeat at the same target with no other change: adjustment zero.
- Change the target to USD 113 after the USD 112 adjustment: add USD 1, not USD 3.

In a separate settlement example, settle the whole receivable while its carrying value is USD 112 and the received value is USD 111: debit Bank USD 111, debit FX Loss USD 1, credit Receivable USD 112. If EUR 100 is received into a EUR bank account, the bank still holds EUR 100; USD 111 is its base-currency value, not a claim that USD cash was received. An actual conversion between currencies needs its own future transfer design.

## Proposed: System Journal contract

Use an explicit system valuation-adjustment kind through the shared [Journal contract](design/journal-posting-contract.md), not a second posting engine.

- Keep one original exposure currency per generated Journal. Split a multi-currency preview into currency-specific Journals.
- The valuation kind permits zero original-currency movement with nonzero balanced base-currency movements. Do not invent a fake conversion rate or foreign principal to satisfy the normal transaction formula.
- Store the valuation rate as calculation evidence, not as a rate that converts a zero principal movement into the adjustment.
- Permit this kind only for a validated generated revaluation operation. It is not a general manual bypass of currency, balance, Account or permission rules.
- Use the current organization/ledger permissions, Account validity, system-purpose mappings and required details. Exact initiating permission and monetary-classification configuration remain design choices, not new roles.
- Preserve operation, source exposure, rate evidence, generated Journal and revision links. Reports read the official Journal effect once.

### Proposed: Settlement representation

The settlement example shows the combined accounting result, not a normal single-rate Journal payload. Proposed representation: post the EUR principal at the shared settlement rate (debit EUR Bank USD 111, credit Receivable USD 111), plus an atomically linked base-only settlement FX adjustment (debit FX Loss USD 1, credit Receivable USD 1). Together these remove EUR 100 and USD 112 of receivable and record EUR 100 / USD 111 in Bank.

The linked settlement adjustment needs its own validated system purpose under the same posting contract. It cannot be submitted as an arbitrary manual revaluation or independently succeed while its principal settlement fails. Commit both official components and their source/allocation links together. This is a proposed representation for the confirmed carrying-value behavior, not an approved new ordinary mixed-rate Journal. No fake rate or new foreign principal is introduced.

## Proposed: As-of calculation and partial settlement

For accounting date D, use remaining original-currency exposure and its effective base carrying value **as of D**. Include prior effective adjustments on D; exclude future-dated transactions and revaluations. A rerun must use the current effective versions, not superseded audit revisions.

For a positive receivable exposure, target base value is outstanding original amount multiplied by the chosen base-per-original valuation rate. Adjustment equals target value minus current carrying value. Liability postings use the corresponding debit/credit direction; preserve signed values instead of taking absolute values.

For a partial settlement, allocate the carrying value of the matched exposure proportionally to the original amount settled. For example, settling EUR 40 of EUR 100 carrying USD 112 removes USD 44.80 of carrying value. Compare that amount with the base value received. Keep the remainder and residual rounding traceable to the same exposure. The exact allocation and rounding method must be validated before implementation; this example does not finalize full invoice-allocation features.

## Proposed: Concurrency, retries and stale previews

The preview identifies ledger/date/currency, selected source exposures, rate inputs and current source/Journal/rule versions. Revalidate all of them at confirmation under the same authorization, ledger and Close transaction boundary used by posting.

- A changed preview is rejected for refresh; never silently post a different amount.
- A stable operation/request ID and saved result make retries return the same result, subject to current authorization.
- Two requests with different IDs for the same unchanged target must still not double-post. Serialize calculation and commit against current carrying values.
- Commit each official adjustment, its source links and success receipt atomically. Proposed batch behavior: currency groups are separate atomic operations with explicit per-group outcomes, not a false all-or-nothing batch.
- Retain candidate inputs before attempting official effects. Definite validation failure is Error and has no new effect. A genuinely unknown result is Pending and must be reconciled before retry.
- Failed edits preserve the previous effective source and posting. External attachments remain outside the database transaction.

For any earlier effective change, including a new backdated posting/revaluation, edit, deletion or reversal, identify dependent later revaluations and settlements. Proposed handling: mark them stale and show a report warning until reviewed and recalculated in date order. Do not certify the affected valuation as current. Do not silently delete or rebuild closed records. Exact blocking, correction approval and rebuild steps remain open design work.

## Reports and traceability

TB, GL Detail, Balance Sheet and P&L include each effective revaluation Journal once in base currency. Original-currency principal totals remain unchanged by a base-only adjustment. Drilldown shows the source exposure, rate/date evidence and adjustment Journal.

An Error attempt adds nothing to totals; a failed edit keeps the previous effective posting. Pending is not proof of failure. A stale downstream valuation is a disclosed review issue, not permission to patch report totals directly.

## Acceptance scenarios

All scenarios are **Not run**.

| Scenario | Expected result |
| --- | --- |
| EUR 100 carrying USD 110, target USD 112 | Base adjustment +2; original principal unchanged; base debits equal credits |
| Same target submitted twice | One effective adjustment; repeat delta zero or original operation result |
| Target changes from USD 112 to USD 113 | Incremental +1 after preview refresh |
| Concurrent distinct requests use the same target | No duplicated valuation effect |
| Preview source or rate changes before confirmation | Conflict/refresh; no stale posting |
| Future-dated posting exists | It does not enter the earlier as-of calculation |
| Settlement principal succeeds but its linked FX adjustment fails | Neither official component takes effect; saved candidate/error remains |
| Partial settlement of EUR 40 from EUR 100 carrying USD 112 | Allocate USD 44.80 carrying value before calculating settlement difference |
| Close changes during confirmation | Recheck atomically; no posting at or before cutoff |
| Earlier open-period source is edited | Preserve allowed edit behavior; flag dependent valuations/settlements and report review status |
| Historical-cost non-monetary item selected | Not treated as an eligible monetary exposure by guessing |
| Timeout after commit | Reconcile saved operation and Journal; no blind second posting |
| Multi-currency preview | Separate single-currency system Journals with explicit group outcomes |
| Wrong ledger or revoked access | No preview data disclosure or posting |

## Still open

- Exact decimal scales, rounding, residual allocation and exchange-rate direction/source contract.
- Monetary classification and eligible exposure selection, including settlement allocation details.
- Exact accounting date/time and timezone representation, and permission to reopen Close.
- Backdated dependency correction and rebuild workflow.
- Base-currency changes, fiscal years and year-end profit transfer remain separate topics.

These are not newly approved by the FX workflow. No automatic account creation, tax treatment or full IFRS compliance is claimed.

## Accounting reference

IAS 21 distinguishes monetary remeasurement from historical-cost non-monetary translation. This supports the scope distinction, not a claim that this limited Books workflow implements every requirement or exception. See [IAS 21, paragraph 23](https://www.ifrs.org/content/dam/ifrs/publications/pdf-standards/english/2022/issued/part-a/ias-21-the-effects-of-changes-in-foreign-exchange-rates.pdf). The supplied official PDF currently redirects to subscription access; its paragraph 23 distinction was available in the official search extract. Further accounting review is needed before implementation.

[Journal requirements](05-journals-posting.md) · [Reports](06-reports.md) · [Documentation index](README.md)
