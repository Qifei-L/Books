# Decisions

## Decided

| Date | Decision |
| --- | --- |
| 2026-10-23 | Organization-first model: many companies per organization, one ledger per company, two roles, per-ledger read/edit grants, invitation rules, last-admin protection. |
| 2026-10-23 | One backend for API and MCP; MCP has no extra rights. |
| 2026-10-23 | C#, ASP.NET Core, EF Core, PostgreSQL. No ABP. Modular monolith. |
| 2026-10-23 | Six design principles (see [Domain rules](02-domain-rules.md#principles)). |
| 2026-10-23 | Account tree baseline, Cost & Expense naming, two P&L groups, editable ledger-unique Code, Account Usage as a many-to-many purpose, three system purposes. |
| 2026-10-23 | Journal: one currency and one rate per journal; Item, Qty, optional Party; system Journal No.; Save = save + post; Save Draft; Suspense/Error/Pending; edit or delete before Close; administrators Close with an inclusive cutoff. |
| 2026-10-23 | Four reports: TB, GL Detail, Balance Sheet, P&L. |
| 2026-10-23 | Attachments optional and portable; taxes configured by people. |
| 2026-10-23 | FX revaluation workflow (human-initiated, base-only adjustments, carrying-value settlement). Implementation deferred past v1. |
| 2026-10-23 | v1 includes Purchases, Sales, receipts and payments with allocation, bank transfers and bank reconciliation. |
| 2026-10-23 | Tax per line, rounded per line, then summed. |
| 2026-10-23 | Tax reporting is a separate module. Jurisdiction: Cambodia. |
| 2026-10-23 | Documents compressed; the review draft moved to `docs/archive`. Rules become tests. |
| 2026-10-23 | Ledger base currencies: USD and KHR only. |
| 2026-10-23 | A company has only a name for now; a ledger has no name of its own and shows its company's name. |
| 2026-10-23 | Invitations reach the invitee through their in-app inbox after sign-in; no email in v1. |
| 2026-10-23 | Account codes are case-insensitive within a ledger, free text of 1–50 characters with no further format. |
| 2026-10-23 | Members with edit access to a ledger may maintain its chart of accounts. |
| 2026-10-23 | Accounts that were never used may be deleted; used accounts are deactivated (rule 19). |
| 2026-10-23 | MCP client sign-in (OAuth discovery) is deferred. |
| 2026-10-23 | Identity provider: the simplest option, Microsoft Entra ID from the existing Microsoft 365 tenant (one app registration exposing the Books API and MCP audiences). |
| 2026-10-23 | Emails match ignoring case, the whole address. |
| 2026-10-23 | Administrators see member emails in the member list. |
| 2026-10-23 | Removing a member also invalidates pending invitations addressed to them. |
| 2026-10-23 | Chart structure as in Manager.io: groups are headings that never take postings; accounts sit under groups and do not nest (rule 15). |
| 2026-10-23 | M2 journals are in the ledger's base currency only; currency and rate columns come with multi-currency later. |
| 2026-10-23 | Journal No. is a unique system number per ledger; the user's own numbering goes in the free-text Reference, with no fixed format. |
| 2026-10-23 | Administrators may move a Close date back (reopen). |
| 2026-10-23 | Drafts are visible only to their creator. |
| 2026-10-23 | Journal lines carry no Party or Item until M4. |
| 2026-10-23 | Suspense is a system account that keeps a journal balanced while a classification is not decided; the Trial Balance warns while it is not zero. |
| 2026-10-23 | AI clients (MCP) may save drafts only; a person posts, edits or deletes posted journals. |
| 2026-10-23 | System purposes are limited by type: Retained Earnings and Opening Balance Clearing are Equity; Rounding Differences is Income or Cost & Expense; Suspense is a balance-sheet account. |
| 2026-10-23 | An account referenced by any journal line, including drafts, counts as used. Opening balances are entered as journals against Opening Balance Clearing. |
| 2026-10-23 | Statements: no comparative columns in v1; zero-balance accounts are shown; every group level shows a subtotal; a P&L may span fiscal years (the fiscal year only decides profit carried forward); export to Excel only. |
| 2026-10-23 | Parties: one record for customers and suppliers, with tax ID, contact, email, phone, address and default payment terms; no party code. |
| 2026-10-23 | Tax codes are maintained by administrators. VAT 0% and non-VAT are separate categories. Tax is rounded per line to the currency's minor unit, half away from zero (0.005 → 0.01). |
| 2026-10-23 | A line's tax may be overridden without limit, with an optional reason; the calculated and entered amounts are both kept and differences are flagged for the tax module (replaces P8). |
| 2026-10-23 | One bank account per ledger account. Exchange rates are entered by hand for now. Documents get a system number; the user's own number goes in Reference. |
| 2026-10-23 | A simple Item list (category, default account, default tax code) in v1, designed to become inventory items later. |
| 2026-10-23 | Allocations are derived, never stored as balances: open balance = total − allocations; overpayments stay as unallocated amounts on the payment. |
| 2026-10-23 | A document that cannot be posted is kept as a draft with the reasons; a posted document whose change cannot be posted stays unchanged (rule 28). |
| 2026-10-23 | Input VAT: a person or the AI decides whether a valid tax invoice was received. Without one, the VAT goes to the tax code's own non-deductible account (a Cost & Expense account set on each input VAT code, required for that kind; from M5), not to input VAT and not into the line's expense account. The setting is generic, so other tax kinds can use it later. |
| 2026-10-23 | Default tax setup is a versioned Cambodia tax pack: (1) its standard tax codes, applied to a ledger by an administrator, creating missing tax accounts in a Taxes group that can be remapped (M5); (2) its monthly return workpaper, mapping tax codes to return boxes with USD and KHR amounts, drilldown, adjusted lines and reconciliation to tax accounts (M8). The box list is drafted from 04-cambodia-tax and marked to confirm with an adviser. |
| 2026-10-23 | Accounts Payable is chosen through Account Usage, not a new system purpose or default-account setting: documents offer active control accounts carrying the Payable usage (Receivable for sales). By default a ledger has one such account, selected automatically; adding the usage to more accounts later allows a choice without changing the model. Account Usage (many-to-many) is built in M5. |
| 2026-10-23 | Purchases: withholding tax is calculated per line and totalled on the invoice; the due date defaults to invoice date + the supplier's payment terms and can be changed on the invoice; attachments are left for a later, shared feature. |
| 2026-10-23 | Account Usage rules: (1) the usage list is defined by the system, per subsystem; (2) a ledger's starter template (with the Cambodia tax pack) assigns usages at setup, and when several accounts share a usage the document must name one; (3) usages carry constraints, for example Payable and Receivable accounts must be control accounts, checked in the same transaction as the change; (4) tax codes and bank accounts keep their direct account references, but the referenced account must carry the matching usage (output VAT, input VAT, non-deductible VAT, WHT payable, bank), so all account mappings follow one rule; (5) a usage cannot be removed while open documents rely on that account; (6) aging totals equal the sum of all accounts with that usage (rule 43). |
| 2026-10-23 | One Payment document with two kinds of lines: allocations to supplier invoices (Dr AP) and direct lines (account, item, quantity, price, tax code, valid-invoice flag, WHT), which may be mixed; one journal per payment. Direct payments are not split into an invoice plus a payment. Invoice lines and direct payment lines share one line component. Payments and receipts are deferred (2026-10-24): not in M5 or M6; scheduled with M7. Until then aging only grows. |
| 2026-10-23 | v1 stays USD-only; foreign-currency bank accounts come with multi-currency later. |
| 2026-10-23 | Reopening a Close needs no written reason. Prior years' profit shows as a calculated Retained Earnings line; GL Detail balances are signed. |
| 2026-10-23 | `docs/16-ledger-accounting-policy.md` dropped; 01–06 are the only current documents. |
| 2026-10-24 | Tax currency on documents is a slot filled by the ledger's tax pack, not KHR columns on each document. The Cambodia pack fills KHR: the NBC rate on the document date from the exchange rate table (entered by hand), the gross total and VAT in KHR rounded to 0 decimals. The rate and amounts are a snapshot kept with the document; a missing rate keeps the document a draft with the reason. Anyone who can edit the document may change the rate, with a reason. Purchase documents calculate KHR the same way (no entry of the supplier's printed KHR). Not posted to the ledger. |
| 2026-10-24 | Bulk work is done by AI through MCP, not by people through import screens. M7 has no bank file parsers, import wizard or manual matching screen; it keeps statement lines, de-duplication, balance checks, matching rules and reconciliation on the server, behind MCP batch tools. Original statement files are not stored for now. |
| 2026-10-24 | MCP batch tools succeed or fail as a whole: one transaction and one Idempotency-Key per batch; a rejected batch lists every error (document, line, reason); batches have a size limit. Business problems that keep a document a draft are not errors. |
| 2026-10-24 | Writes inside a ledger lock that ledger and only share the organization, so ledgers do not block each other; organization changes (members, access, rates) still wait for writes in flight. |
| 2026-10-24 | Monthly tax return (M8): the tax pack defines the return's boxes and the categories withholding codes are reported under; the return is calculated from posted documents by document date, so the API, the preview and MCP show the same figures, and AI clients read both the definition and the figures through MCP. Boxes are drafted from 04-cambodia-tax and marked to confirm with an adviser. |
| 2026-10-24 | Return KHR amounts start from each document's tax currency snapshot; the month's rate may be overridden on the return, with a reason. |
| 2026-10-24 | Filing a month's return keeps a snapshot of it and closes the ledger through the month end, so filed months cannot change. |
| 2026-10-24 | Purchase withholding tax is reported in the month of the invoice date (accrual, matching DFDL Instruction 37705). |
| 2026-10-24 | The prepayment of tax on income is still paid: the return calculates 1% of turnover excluding VAT, not just the base. |
| 2026-10-24 | Roadmap after M8: payments and receipts (M7), invoice printing as PDF with the KHR total, inventory, and other monthly taxes. Not planned: a full editing frontend (work goes through the API and MCP) and a parallel run. Updated the same day: inventory with fixed assets, other monthly taxes, multi-currency and attachments are planned (M10–M13); the annual income tax return is planned for later. Moving data from Manager.io is AI work through the MCP batch tools, not a separate import feature: AI-native import and human import screens are different designs. |
| 2026-10-24 | Tax return boxes are versioned data, not code: each ledger's definition has versions that take effect from a month; anyone who edits the ledger (AI clients too) drafts a version and previews it on real months, administrators publish it, published versions never change, and filed months keep the version they were filed with. Tax rates were already data (tax codes with effective dates). |
| 2026-10-24 | Payroll sheets differ by company: when salary tax is built (M11), the payroll layout (earnings, allowances, deductions, which items are taxable) is per-company data in the same versioned way, not one fixed form. |
| 2026-10-24 | Journal postings on tax accounts that do not come from purchase or sales documents (manual corrections) count in the return: output VAT, input VAT and a "withholding tax posted by journals" box take them, at the month-end rate, and every box's figure opens to the documents and journals behind it. A VAT credit is not carried forward automatically; it is visible on the return. |
| 2026-10-24 | Drafts dated in a month block filing that month (they would be stuck behind the close); reopening a filed month stays possible for administrators, with the differences from the filed return shown. Return definitions are kept per ledger, copied from the tax pack's template; a shared definition can be extracted later. |
| 2026-10-24 | The built-in return follows GDT's current forms (TTR001, T7 01, WT 03). A taxable person is a customer or supplier with a tax ID (no separate VAT-registered flag). The template keeps one non-resident 14% withholding code, reported on WT 03 II.3, as its label says. VAT 4% on fuel counts in the VAT 10% lines. Imports (T7 01 boxes 05-06) are connected later. |
| 2026-10-24 | Payments and receipts (M7) follow Manager.io: one document with many lines; a line settles a named invoice, goes to the party's balance (settled against the oldest open invoices; any excess stays as the party's balance), or posts to an account with optional VAT and withholding. Fees, discounts, short payments and tax withheld by customers are their own lines (negative where needed); which account a customer's withholding goes to is left to the accountant and the AI. Tax payments to the tax office are ordinary lines. |
| 2026-10-24 | Bank reconciliation follows Manager.io: bank transactions are pending or cleared, and a reconciliation compares the statement balance with the cleared book balance; statement lines arrive through MCP and clear matching transactions; unmatched lines go to Suspense for the accountant or AI to reclassify. |
| 2026-10-24 | Payroll: Books receives results from the client's payroll system (imported by AI; the AI maps whatever columns the system exports), it does not calculate pay, generate bank payroll files or keep a separate payroll permission. A pay run holds one line per employee and posts one summary journal; its lines feed form TOS 01 at the rate of the 15th. Income tax (annual return) is planned for later. |
| 2026-10-24 | Bank accounts are "cash and bank" accounts: petty cash is set up like a bank account (bank name "Cash"). Unclearing is not limited to administrators. Payroll systems report KHR, so pay runs keep the lines' currency with its rate (default: the rate of the 15th) and post the converted totals; the Cambodia template sets up the payroll accounts through payroll usages. |
| 2026-10-24 | Invoice printing (M9): Khmer and English; three fixed styles (commercial invoice, tax invoice including State Charge, and invoices mixing VAT 0% and non-VAT); invoice numbers with a per-ledger prefix; QuestPDF (community licence: revenue under USD 1M) with a Khmer font; CamInvoice later. |

## Proposed defaults (accept or change)

| # | Default | Why |
| --- | --- | --- |
| P1 | Base currency fixed after first posting; USD for Cambodian companies, with KHR for tax | Changing base currency means rebuilding the books |
| P2 | Store `numeric(20,4)`; validate posted amounts to ISO minor units; rates `numeric(20,10)` | Room in storage, strict at entry |
| P3 | Fiscal year start month, monthly periods, calendar year default | Matches the Cambodian default tax year |
| P4 | Year-end calculated in reports; no closing journal | Cannot double-count; same approach as Manager.io |
| P5 | Close is one date per ledger | Removes the scope ambiguity |
| P6 | Generated journals are edited only through their document | Documents and journals cannot drift apart |
| P7 | AR/AP control accounts accept document postings only; opening AR/AP enters as opening invoices | Aging always equals the control account |
| P8 | Line tax adjustment tolerance ±0.05 in document currency | Lets us match supplier invoices |
| P9 | Reconciled bank items: soft lock (unreconcile first, audited) | Prevents silent changes to reconciled months |
| P10 | Transfer fees as an optional line on the transfer | One document per bank movement |
| P11 | When FX arrives: one FX gain/loss account pair at first, revaluation by account + currency + party, revalue then Close | Avoids a wrong realized/unrealized split and stale-revaluation tracking |

## Open questions

1. Which banks, and what statement export formats do they provide?
2. Cambodian adviser review of [Cambodia tax](04-cambodia-tax.md#to-confirm-with-an-adviser).
3. Is v1 only for our own companies, or open to other teams from the start? Our own companies only lets us simplify invitation and delegation flows.
4. Which Manager.io modules are actually in use (to finalize the migration scope)?
