# Journals and Posting

Status: Review draft. Access boundaries are confirmed. The journal workflow and accounting controls below are proposals, not approved accounting policy.

These workflows are proposed for a later accounting stage. They do not add journal endpoints or other accounting work to the organization foundation's phase-one API contract.

## Confirmed

Every journal belongs to one ledger. A member needs edit access to change ledger data; read-only access never permits a write. An organization administrator's access is limited to that organization and does not bypass accounting checks. Members cannot delegate access.

## Proposed: First-release scope

Start with manual general journals, including opening-balance and correction journals. Sales invoices, purchase bills, bank reconciliation, payroll, tax calculations and automatic source-document posting are separate features, not implied by this workflow.

Ledger editors and organization administrators would be able to prepare, post and reverse journals. A separate preparer/approver workflow is not included in this proposal and needs an explicit decision if required.

## Proposed: Journal content

A journal contains its ledger, accounting date, description, stable identity, status and lines. Each line contains an account, an optional description, and debit or credit amount. Keep the creator, creation time, latest editor and posting actor/time separately.

Use a stable internal identity from creation. Assign an immutable human-readable posting number when posting succeeds. The numbering format and sequence scope remain open; do not promise gap-free numbering before those rules are defined.

## Proposed: Draft and posted states

1. Save a draft. It may be incomplete or unbalanced, with visible validation warnings. It does not affect any ledger balance or posted report.
2. Edit the draft. Reject a stale edit if another person has changed it; do not silently overwrite their work.
3. Post after all checks pass. All lines and the posted status become effective together, or nothing does.
4. Keep the posted journal unchanged. Corrections use linked journals rather than editing or deleting the original.

An abandoned draft can be cancelled and retained outside the working list. It has no accounting effect. Posting or editing a cancelled draft is rejected; a new draft can be prepared instead. Hard deletion is outside this proposal.

## Proposed: Posting checks

At the moment of posting, verify:

- The user still has edit or administrator access to this ledger.
- The company is active and its required accounting settings are complete.
- There are at least two non-zero lines. Each line has a debit or a credit, never both, and never a negative amount.
- Every account belongs to this ledger and is active and eligible for posting.
- Debit total equals credit total exactly at the agreed amount precision.
- The accounting date is valid, no earlier than the ledger opening date, and belongs to an open accounting period.
- The journal has not already been posted or cancelled, and the submitted draft version is current.

Do not invent a balancing line or silently round an unbalanced journal into balance. A failed check explains what must be corrected and leaves no partial posting.

## Proposed: Amounts and dates

Use decimal amounts, not binary floating point, throughout entry, storage, calculations and exports. The number of decimal places, maximum amount and rounding rule remain open. In this proposal, input beyond the approved precision is rejected rather than silently rounded.

Single-base-currency journals are the proposed starting point. Foreign-currency amounts and exchange gains or losses require a separate approved design.

Keep three concepts separate:

- Accounting date: determines the period and report inclusion.
- Entry time: when the draft was created.
- Posting time: when it became effective in the ledger.

Proposed period control: administrators can close an accounting period; posting into a closed period is blocked for everyone. Reopening requires a recorded reason and an audit trail. Closing a period does not itself create year-end entries or certify compliance. Closing and posting at the same time must have a definite order, never a partial result.

Whether future-dated journals are allowed, how far back users can post, and how year-end closing works remain open. Do not infer them from the timezone or financial-year setting.

## Proposed: Reversals and corrections

A reversal creates a separate journal with the original lines' debit and credit sides swapped. It records the original journal, a reason and a chosen accounting date. A replacement journal can then record the corrected entry.

- Keep the original posted entry and its number unchanged.
- A reversal draft has no effect until posted. Mark the original as reversed only when the reversal posts successfully.
- Allow at most one posted full reversal of an original journal. Prevent concurrent duplicate reversals.
- Include both the original and its posted reversal in reports, each on its own accounting date. Never remove the original from history.
- If the original period is closed, use an eligible open-period date or follow the approved reopening process. Do not silently change the original date.
- The same validation applies to reversals. If an original account is inactive, explain the blocker; reactivation requires an authorized, audited change. Do not quietly bypass account controls.

Partial reversals, editing a reversal and reversing a reversal are outside this proposed first-release workflow. A further error would require an explicitly linked correction journal with a reason.

## Proposed: Retries, concurrency and audit

A repeated request must not create duplicate journals, postings or reversals. Give each intended write a request identity: repeating that identity with the same content returns the original result; changing the content under the same identity is rejected. Request identities are scoped so one ledger or user cannot retrieve another's result.

An uncertain response is not proof of failure. Check the operation's saved result before trying a new request identity. Two different requests that try to post the same draft still produce only one posting. This does not automatically detect two separately entered journals with identical business content; source-document duplicate detection is a separate scope decision.

Record who performed each action, the ledger and journal, when it happened, the relevant before/after values and the reason for a correction or reversal. Preserve historical actor identity after membership removal. Do not record passwords, tokens or unrelated personal data.

The proposed implementation approach remains separate from these business requirements. C#, ASP.NET Core, EF Core and PostgreSQL are the selected stack; ABP is not used. This document authorizes no code, migration or deployment.

## Review checks

- An unbalanced draft can be saved but cannot post or affect a report.
- Cross-ledger accounts, inactive accounts, negative lines and excess precision are rejected at posting.
- A posting either appears fully in journal history and balances or not at all.
- Retrying after a lost response returns the same posting, not a duplicate.
- Concurrent edits, posting attempts, reversals and period closure produce one clear valid outcome.
- A reversal leaves the original visible and affects only the reversal's accounting date.
- Revoked access cannot be reused by an old screen or MCP request.

These are proposed acceptance scenarios, not completed tests.

## Open Questions

1. Are manual journals enough initially? Do editors also post and reverse, or is approval required?
2. What amount precision, maximum value and rounding rule are required?
3. What numbering rule, date restrictions and close/reopen process should apply?
4. Is full reversal plus a replacement journal sufficient? Are partial reversals needed?

[Next: Balances and Reports](06-reports.md) · [Document index](README.md)
