# Product

## Goal

Replace our Manager.io bookkeeping with a system that:

1. Treats AI (MCP) as a first-class user under the same permissions and validation as people.
2. Serves several companies under one organization, with per-ledger access for team members.
3. Keeps every number traceable to its document, revision and actor.

## Users

- **Organization administrator**: sets up companies, members, access and Close.
- **Bookkeeper** (member with edit access): enters documents and journals, reconciles banks.
- **Reader** (member with read access): views reports.
- **AI agent**: acts for one person through MCP, never with more access than that person.

## v1 scope

| Area | Included |
| --- | --- |
| Foundation | Sign-in (external OIDC), organizations, members, invitations, ledger access |
| Ledger setup | Base currency, fiscal year, Close date, chart of accounts, tax codes, bank accounts |
| Journal core | Manual journals, Save / Save Draft, edit and delete before Close, opening balances |
| Parties | Customers and suppliers as one Party record |
| Purchases (AP) | Purchase invoice, debit note, payment with allocation, direct payment, withholding tax |
| Sales (AR) | Sales invoice, credit note, receipt with allocation, KHR totals on invoices |
| Bank | Transfers between bank accounts, statement import (CSV), matching, reconciliation |
| Reports | Trial Balance, GL Detail, Balance Sheet, P&L, AR/AP aging, party statements |
| Tax reporting | Separate module: VAT and WHT summaries and detail for the Cambodian monthly return |
| MCP | Read and write tools that call the same application services as the API |
| Migration | Import accounts, parties, opening balances and open invoices from Manager.io |

**Not in v1:** inventory and costing, purchase/sales orders, approvals, payroll, fixed-asset register, recurring entries, multi-currency transactions and revaluation, e-filing and CamInvoice integration, a full web frontend (v1 ships a minimal review/report UI only), consolidation.

## Success criteria

1. One real company imported from Manager.io shows the same Trial Balance, account by account, and the same open AR/AP by party.
2. A month of real activity entered through MCP and the minimal UI produces a monthly tax summary that matches the filed return.
3. One month of parallel running with daily Trial Balance comparison, with no unexplained difference.

## Milestones

| Weeks | Milestone | Done when |
| --- | --- | --- |
| 1–2 | M1 Foundation | Org isolation, invitation and last-admin tests pass; accounts CRUD |
| 3–4 | M2 Journal core | Post, edit, delete, Close; TB and GL Detail; invariants tested |
| 5 | M3 Statements | Balance Sheet, P&L, year-end calculation |
| 6 | M4 Document framework | Parties, tax codes, bank accounts, posting adapter, allocations |
| 7–8 | M5 Purchases | AP documents, WHT, AP aging |
| 9 | M6 Sales | AR documents, KHR totals (tax currency slot), AR aging, MCP batch tools |
| 10–11 | M7 Bank | Payments and receipts, statement lines through MCP batch import, matching, reconciliation |
| 12 | M8 Tax reporting | VAT/WHT monthly summary and detail, filed-period snapshot |
| 13 | M9 Invoice printing | Sales invoices and credit notes as PDF, with the KHR total |
| 14–15 | M10 Inventory | Inventory items, stock movements from purchases and sales, cost of sales |
| 16 | M11 Other monthly taxes | Monthly taxes beyond VAT, WHT and prepayment, as confirmed with an adviser |

MCP tools ship with each milestone, not at the end.
