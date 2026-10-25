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

| Milestone | Done when | Status (2026-10-25) |
| --- | --- | --- |
| M1 Foundation | Org isolation, invitation and last-admin tests pass; accounts CRUD | Done |
| M2 Journal core | Post, edit, delete, Close; TB and GL Detail; invariants tested | Done |
| M3 Statements | Balance Sheet, P&L, year-end calculation; Excel export | Done |
| M4 Document framework | Parties, tax codes, bank accounts, posting adapter, transfers, items | Done |
| M5 Purchases | Purchase invoices, debit notes, WHT, AP aging, Cambodia template | Done |
| M6 Sales | Sales invoices, credit notes, KHR totals, AR aging, MCP batch tools | Done |
| M7 Cash and bank | Payments and receipts (Manager.io style), settlement by party, statement import through MCP, clearing and reconciliation | Done |
| M8 Tax reporting | Monthly return on GDT forms (T7 01, WT 03), versioned definitions, reconciliation, filing | Done |
| M9 Invoice printing | Sales invoices and credit notes as PDF in Khmer and English, three styles, KHR total | Part 1 done (PDF, settings) |
| M10 Inventory and fixed assets | Inventory items, stock movements from purchases and sales, cost of sales; stock adjustments and counts; fixed asset register and depreciation | Done |
| M11 Other monthly taxes | Tax on salary from imported payroll results (TOS 01) is done; specific, accommodation and public lighting tax (T7 01) | Done |
| M12 Multi-currency | Foreign-currency bank accounts, documents and parties; exchange differences | Done: invoices and notes, KHR bank accounts, realized differences, year-end revaluation |
| M13 Attachments | Files attached to documents and journals, shared by every document type | Planned |
| M14 Organization-shared information | Information kept once per organization and used by every company (ledger) in it: customers and suppliers, items, tax codes, return definitions and templates; exchange rates are already shared. Each ledger still keeps its own balances and documents | Planned (scope to confirm) |
| M15 Data entry for people | Forms in the app for the work people do by hand: customers and suppliers, sales, purchases, payments and receipts, journals; same API, rules and audit as AI clients | In progress (journal dimensions done) |
| M15.1 Journal dimensions | Manual journals post AR/AP with a party; class and location on journal, sales and purchase lines; class master data, API and page; manual VAT/other tax entered gross and split into net + tax; GL Detail shows class, location and party | Done |
| M16 Report dimensions | TB, BS, P&L and GL Detail filter by class and location; the same filters reach the app and Excel; unfiltered totals unchanged | Done |
| M17 Attachments (implements M13) | Files attached to invoices, bills, payments and journals; upload, download, delete, audit and MCP | Planned (next, after the full-page record surface) |
| M18 Report delivery | Organization branding and per-ledger output settings; Stimulsoft PDF/Excel report packs; tax workpapers before official GDT forms; English default | In progress (branding, client pack PDF and tax workpaper done; templates/share next) |
| Later | Remaining document dimensions; M14 organization-shared data; annual income tax return; CamInvoice | Planned |

MCP tools ship with each milestone, not at the end.
