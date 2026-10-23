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
| 2026-10-23 | `docs/16-ledger-accounting-policy.md` dropped; 01–06 are the only current documents. |

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
3. Who may reopen a Close date?
4. Is v1 only for our own companies, or open to other teams from the start? Our own companies only lets us simplify invitation and delegation flows.
5. Which Manager.io modules are actually in use (to finalize the migration scope)?
