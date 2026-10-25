# Domain rules

Each rule is enforced by the server and, where marked **[T]**, has an invariant test. Rules marked *(default)* are proposed defaults awaiting confirmation; see [Decisions](05-decisions.md).

## Principles

Single source of truth · Consistency by design · Traceability · Secure by default · Explicit over implicit · Design for change.

## Organizations and access

1. An organization contains members and companies. Each company has exactly one ledger. **[T]**
2. Roles: **administrator** (all ledgers, members and grants in that organization) and **member** (only assigned ledgers, each read or edit).
3. Only administrators invite, grant and revoke. Edit access never includes granting access.
4. A role in one organization gives nothing in another. Guessed IDs grant nothing. **[T]**
5. Every protected operation checks current membership, ledger ownership and permission inside its transaction. API and MCP use the same check. **[T]**
6. Invitations target one verified identity, last 7 days, can be revoked, and must be explicitly accepted. They become invalid when the inviter stops being an administrator.
7. Removed or demoted users get no old grants back; rejoining needs new grants.
8. An organization never loses its last administrator through normal changes, including concurrent ones. **[T]** Security suspension is still allowed.
9. Administrator rights never bypass accounting validation, Close or audit.

## Ledger setup

10. Base currency is chosen at setup and cannot change after the first posting. *(default)*
11. Amounts are stored as decimals, never floats; posted amounts use the currency's ISO 4217 minor units (USD 2, KHR 0). *(default)*
12. Fiscal year is defined by a start month; periods are calendar months. *(default)*
13. Close is one inclusive `closed_through` date per ledger, set by an administrator. *(Close by administrators is decided; per-ledger is a default.)*

## Accounts

14. Five types: Assets, Liabilities, Equity, Income, Cost & Expense. P&L = Income (add) − Cost & Expense (subtract), keeping signed and contra balances.
15. The chart has groups and accounts, as in Manager.io. Groups are headings that never take postings and may sit inside a group of the same type. Accounts sit under at most one group of the same type in the same ledger and never contain other accounts. **[T]**
16. Code is user-entered and editable, unique within a ledger ignoring letter case. The stable Account ID is what journals reference.
17. System purposes: Retained Earnings, Opening Balance Clearing (must be zero after setup), Rounding Differences (only if needed).
18. Control accounts (AR, AP) carry a Party. Documents post them directly; a manual journal may also post them when every control line names an active party. Inventory, fixed assets and payroll remain document-only. **[T]**
19. Used accounts are deactivated, not deleted; accounts that were never used may be deleted. Type and parent of a used account do not change. *(default)*
20. Current Year Profit is calculated in reports, never posted to.

## Journals

21. A journal has one transaction currency and one shared rate; manual rates are recorded with source Manual.
22. A posted journal balances exactly in transaction and base currency. **[T]** No balancing line is invented and nothing is silently rounded.
23. Lines carry Account, optional Party, optional Item (which may suggest an account), optional Qty, optional Class and optional Location. The selected Account is what is saved. Class and Location are reporting dimensions; they never change the account or the subledger. A manual line may also carry a VAT or other tax code: the entered amount is gross, the account gets the net, and a tax line goes to the code's tax account.
24. Journal No. is system-generated and unique per ledger. Reference is free text and may repeat.
25. **Save** saves and posts in one action. **Save Draft** saves without posting.
26. Outcomes: **Posted** (includes valid postings to Suspense), **Error** (definite validation failure, no accounting effect), **Pending** (unknown, reconcile before retrying). A timeout is not a failure.
27. Posted journals may be edited or deleted while their date is after Close. An edit must keep both the old and the new date open. **[T]**
28. A failed edit leaves the previous posting and source unchanged. An edit replaces the old effect exactly once. **[T]**
29. Retrying the same request never posts twice. **[T]** Stale edits are rejected, not merged.
30. Every change keeps actor, time and previous values.

## Documents (purchases, sales, bank)

31. A document and its journal are saved in one transaction. Either both change or neither does. **[T]**
32. Journals generated from documents are changed only by editing the document. *(default)*
33. Payments and receipts allocate to invoices; open balance = total − allocations. No stored balance fields.
34. Taxes are chosen by people through tax codes. Tax is calculated per line, rounded per line, and summed. Line tax may be adjusted within a small tolerance to match a supplier invoice, with audit. Input VAT is creditable or non-creditable by the chosen tax code (a separate non-creditable code), never by a per-line flag.
35. Bank reconciliation only marks lines as cleared. It never changes accounting amounts.
36. Changing the amount, date or bank account of a cleared item unclears it and flags the reconciliation.

## Reports

37. Reports read only the current effective postings. Drafts, Error attempts and superseded revisions do not count. **[T]**
38. Trial Balance: Code, Name, opening, period and closing debit/credit, with a full-ledger balance check. **[T]**
39. GL Detail: date, Journal No., Reference, description, Class, Location, Party, debit, credit, running balance, with opening balance and period totals.
40. Balance Sheet (as of date): Assets = Liabilities + Equity, with Current Year Profit calculated and never double-counted with Retained Earnings. **[T]**
41. P&L (date range): Income − Cost & Expense.
42. Report totals and their drilldown come from the same database snapshot.
43. AR/AP aging totals equal the control account balances. **[T]**
44. Tax report totals equal the movement on tax accounts for the period, or the difference is shown. **[T]**

## Attachments

45. Attachments are optional. Journals reference stable Attachment IDs, so storage can move without breaking links.
