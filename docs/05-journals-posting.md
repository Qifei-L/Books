# Journals and Posting

Status: Review draft. Access boundaries, the agreed Item behavior and one transaction currency per journal, Post validation outcomes and the Close-based edit/delete boundary are confirmed. The one-click Save behavior below is also confirmed. Other workflow details and accounting controls remain proposals unless explicitly confirmed.

These workflows are proposed for a later accounting stage. They do not add journal endpoints or other accounting work to the organization foundation's phase-one API contract.

## Confirmed

Every journal belongs to one ledger. A member needs edit access to change ledger data; read-only access never permits a write. An organization administrator's access is limited to that organization and does not bypass accounting checks. Members cannot delegate access.

A journal line includes **Item** (the product or service) separately from **Account** (the accounting destination). Adding Item does not approve the full journal header/line model, inventory processing, quantities, costing or foreign-currency rules.

Confirmed currency boundary: each journal has **one transaction currency** shared by all its lines. Lines cannot select or override their own transaction currency. A converted base-currency amount is a valuation of the same line, not a second transaction currency.

Confirmed Post outcome: validate the date, accounts, currency, amounts and debit/credit balance. Post only when all checks pass. A definite validation failure keeps the draft and specific errors in the **Error review list**, with no balance/report effect and no partial journal lines. Correct it and validate again before resubmission. This is not forced posting to a Suspense Account.

Current organization and ledger authorization still applies. Do not expose another organization's input or errors. A network timeout or lost response is an unknown outcome, not proof of validation failure; reconcile the saved outcome before retrying.

## Confirmed: Save and Save Draft

For a future posting-capable business subsystem, normal **Save** is one user action: preserve the business document, generate its proposed journal lines, validate them and attempt posting. Users do not need a second Post click for this normal path.

- Success: show **Saved & Posted** only after the posting result is confirmed.
- Definite validation failure: keep the input and specific errors in **Error**. A new failed attempt creates no accounting effect.
- Failed edit: keep the attempted revision for correction and preserve the previous valid posted effect.
- Unknown outcome: show **Pending**, reconcile the saved result and do not blindly post again.
- **Save Draft** is a separate optional action for unfinished work. It saves without posting.

One click does not mean input preservation and every possible posting outcome must share one all-or-nothing transaction. The input must survive a failed posting attempt; effective accounting changes still commit atomically. Current permissions, Close, validation, duplicate prevention and audit rules apply.

**Proposed name and boundary: Subsystem Posting Contract.** A business subsystem prepares its source document and journal proposal; the common accounting service applies the same posting rules and returns the confirmed outcome. This can be an internal application-service call in the same program, not a new HTTP service or separate accounting engine. A Sales document is an example, not approval to implement Sales now. Exact source links, payloads and lifecycle contracts belong to later design.

## Proposed: First-release scope

Start with manual general journals, including opening-balance and correction journals. Sales invoices, purchase bills, bank reconciliation, payroll, tax calculations and automatic source-document posting are separate features, not implied by this workflow.

Ledger editors and organization administrators would be able to prepare, post and reverse journals. A separate preparer/approver workflow is not included in this proposal and needs an explicit decision if required.

## Proposed: Journal content

A journal contains its ledger, accounting date, description, stable identity, status and lines. Each line contains an Account and a separate Item field, plus the proposed optional description and debit or credit amount. Keep the creator, creation time, latest editor and posting actor/time separately.

Confirmed Item behavior: Item is optional, so a pure adjustment journal need not select one. Selecting Item may suggest a default Account, but the line stores the actual Account selected for that entry. Changing an Item's default later must not rewrite historical lines or their Account references. Optionality, the default-Account suggestion and saving the actual line Account are confirmed. A suggested Account does not bypass account validity or posting checks.

Confirmed numbering: the system generates a unique **Journal No.**; the user enters **Reference**, which may repeat. Keep the stable internal journal identity separate from both. Number format, uniqueness scope, allocation timing and gap behavior remain design details; do not promise gap-free numbering. Journal No. does not make a posted journal immutable before Close.

## Attachment storage portability

Confirmed requirement: replacing the attachment storage container must preserve the files and their links to journals. This concerns file storage, not a new journal data container.

Proposed solution: a **Document Provider** separates attachment identity from storage location. Journals link to a stable Attachment ID rather than a container URL. Moving a file changes its storage-location record, not the journal's attachment reference. List versus Map does not provide migration safety.

Migration needs verified copying, version tracking, backups and a rollback path; changing containers alone cannot guarantee no data loss. See the [migration proposal](09-deployment-operations.md#attachment-storage-portability).

Attachment reads and migration access must preserve the same organization/ledger boundary. This requirement does not approve a storage provider, new credentials, wider sharing, actual migration or deletion. Confirmed: attachments are optional. A journal can be saved and posted without an attachment if all other checks pass. Attachment counts and retention rules remain separate decisions.

## Proposed: Draft and posted states

These are internal states, not a requirement for two user clicks. The confirmed normal Save action can move through saving and posting in one request; Save Draft stops at the draft state.

1. Save a draft. It may be incomplete or unbalanced, with visible validation warnings. It does not affect any ledger balance or posted report.
2. Edit the draft. Reject a stale edit if another person has changed it; do not silently overwrite their work.
3. Post after all checks pass. All lines and the posted status become effective together, or nothing does.
4. Apply the confirmed Close boundary: authorized users can edit or delete a posted journal while its applicable scope is not closed. Closed journals cannot be edited or deleted; reopening is a separate authorized human action.

An abandoned draft can be cancelled and retained outside the working list. It has no accounting effect. Posting or editing a cancelled draft is rejected; a new draft can be prepared instead. Hard deletion is outside this proposal.

## Posting checks

The date/account/currency/amount/balance checks and failure-to-Error outcome are confirmed. Detailed eligibility, precision, period and role rules below remain proposed where not separately confirmed.

At the moment of posting, verify:

- The user still has edit or administrator access to this ledger.
- The company is active and its required accounting settings are complete.
- There are at least two non-zero lines. Each line has a debit or a credit, never both, and never a negative amount.
- Every account belongs to this ledger and is active and eligible for posting.
- Enforce the confirmed single transaction currency for all lines; reject mixed-currency lines or independent line-currency overrides.
- Debit total equals credit total exactly at the agreed amount precision.
- The accounting date is valid, no earlier than the ledger opening date, and belongs to an open accounting period.
- The journal has not already been posted or cancelled, and the submitted draft version is current.

Do not invent a balancing line or silently round an unbalanced journal into balance. A definite validation failure explains what must be corrected and enters Error review without posting. Permission failures disclose no unauthorized record or payload and cannot be used to bypass ledger access.

## Proposed: Amounts and dates

Use decimal amounts, not binary floating point, throughout entry, storage, calculations and exports. The number of decimal places, maximum amount and rounding rule remain open. In this proposal, input beyond the approved precision is rejected rather than silently rounded.

The earlier base-currency-only starting point is a release-scope proposal, not the meaning of the confirmed one-currency-per-journal rule. That rule does not select supported currencies or approve an FX engine.

Proposed field placement: put Currency in the journal Header. Each line stores its amount in that shared transaction currency and, where conversion is supported, the corresponding ledger base-currency amount. Do not allow an independent Currency override on a line.

Confirmed: one journal uses one shared exchange rate. Users may enter it manually. Rate source is stored as backend information; a manually entered rate has source **Manual**. Rate direction, precision, rounding and account-currency constraints remain design details. Cross-currency transfers need a separate future workflow; they do not relax the one-transaction-currency rule. This does not approve FX implementation. Taxes are configured by people; the system validates and processes according to those settings, without choosing taxes on its own. Tax multiplicity, calculation methods and accounting versus reporting-only treatment are not settled by this rule.

Keep three concepts separate:

- Accounting date: determines the period and report inclusion.
- Entry time: when the draft was created.
- Posting time: when it became effective in the ledger.

Confirmed: a person chooses whether to Close or leave the scope open; there is no automatic Close. Close blocks edits and deletion of posted journals. A person with the required authority controls reopening; the system must not reopen automatically. Confirmed: organization administrators perform Close within their own organization. Close uses an inclusive cutoff: new posting, edits and deletion at or before it are blocked; later accounting dates/times remain open. Use the accounting date/time, not creation time. Reopening permissions remain open.

New posting at or before the cutoff is blocked. Proposed additional control: record a reason for reopening. Close does not itself create year-end entries or certify compliance. A concurrent Close and journal change must have one definite order, never partial results.

Whether future-dated journals are allowed, how far back users can post, and how year-end closing works remain open. Do not infer them from the timezone or financial-year setting.

## Confirmed: Edit or delete a posted journal before Close

Posted status alone does not lock a journal.

- With current ledger permission and an open applicable scope, a user may change amounts, accounts or the accounting date, or delete the posted journal.
- If the applicable scope is closed, reject the edit or deletion. Reopening requires an authorized person's explicit action.
- On a date change, both the original date and the new date must be in an editable scope. Moving the date cannot bypass Close.
- Validate an edited journal again. Only a successful edit replaces the previous accounting effect, with no intermediate partial result.
- If edit validation fails, keep the existing valid posted version and its balances unchanged. Put the attempted change and errors in Error review. Do not create a second posted journal or remove the existing effect.
- A successful deletion removes the journal's accounting effect consistently from balances and reports. Preserve evidence of who changed or deleted what and when, including the previous values.
- A failed or unauthorized change leaves the original unchanged. Do not expose another ledger's journal or error details.

Date-versus-instant and timezone interpretation must be defined explicitly in later design; do not assume a UTC-midnight cutoff. Reopening permissions and detailed audit, archive and deletion mechanisms require later design. Administrators perform Close; this does not automatically grant reopening authority. Deletion here is a business operation, not a promise of physical erasure or a particular recovery feature. No real journal may be changed by this document.

## Proposed: Optional reversals

A linked reversal may remain an optional correction workflow, but it is not mandatory for an open posted journal. If adopted, it would be a separate validated journal and have no accounting effect until posted. Preserve links and avoid duplicate reversal effects. Rules for reversal dates, partial reversals and handling edits or deletion of linked journals still need review.

## Proposed: Retries, concurrency and audit

A repeated request must not create duplicate journals, postings or reversals. Give each intended write a request identity: repeating that identity with the same content returns the original result; changing the content under the same identity is rejected. Request identities are scoped so one ledger or user cannot retrieve another's result.

An uncertain response is not proof of failure. Check the operation's saved result before trying a new request identity. Two different requests that try to post the same draft still produce only one posting. This does not automatically detect two separately entered journals with identical business content; source-document duplicate detection is a separate scope decision.

Record who performed each action, the ledger and journal, when it happened, the relevant before/after values and the reason for a correction or reversal. Preserve historical actor identity after membership removal. Do not record passwords, tokens or unrelated personal data.

The proposed implementation approach remains separate from these business requirements. C#, ASP.NET Core, EF Core and PostgreSQL are the selected stack; ABP is not used. This document authorizes no code, migration or deployment.

## Suspense, Error and Pending

Confirmed: Suspense is posted accounting awaiting classification; Error covers incomplete data, unusable accounts and definite validation or processing failures. Pending covers unfinished or unknown results. The [category distinction](04-chart-of-accounts.md#suspense-and-error) separates accounting effects from failed attempts.

Proposed handling:
- Valid balanced transactions with unresolved classification may use a Suspense Account under an agreed policy.
- Confirmed: records failing Post validation remain unposted in the Error review list with the draft and error reasons. Do not fill in missing amounts, bypass an inactive account or force a balancing entry just to post.
- If posting may have committed but the response was lost, mark the outcome Pending / Unknown and check the saved result before retrying. Do not assume failure or create a second journal.
- Corrections trigger fresh validation. Apply the confirmed Close-based edit/delete policy to posted items. Keep failed edit attempts and unposted error records out of ledger balances; preserve the previous valid posted effect.
- Preserve relevant source and resolution history with current ledger access checks. Error details must not contain authentication secrets or another organization's data.

### Proposed durable submission handling

This simplification applies only to **Error**. The confirmed categories remain distinct:
- **Suspense Account:** valid, balanced entries posted temporarily while classification is unresolved. These affect balances and later need reclassification.
- **Error:** incomplete, unusable or definitely failed attempts needing attention. Unposted attempts do not affect balances; a failed edit retains the previous effective posted result.
- **Pending:** unfinished or unknown outcomes. Reconcile whether posting committed before retrying; do not classify a timeout alone as Error.

Keep one business journal: **preserve input first, then attempt posting**. This describes backend ordering, not two required user actions; normal Save performs both, while Save Draft does not post. Error is a filtered worklist, not a copied business journal. Page layout remains proposed. Keep posted Suspense classification issues separate from Error attempts and Pending outcomes; never mix their balances.

1. Save the draft and its current content before Post. Saving alone has no balance effect.
2. On Post, validate and commit only valid, balanced accounting entries. A definite validation failure keeps the saved content and errors for Error review.
3. For an edit of a posted journal, retain the attempted revision separately from the effective posted version. Replace the accounting effect only after the edit succeeds. A failed edit leaves the old posted result intact.
4. After a timeout or crash, show Pending / Unknown and reconcile the saved outcome before retrying. Do not assume that a lost response means posting failed.

This proposal can use one database and does not require a message queue or a new submission platform. The storage design must preserve saved drafts or attempted revisions, current posted results and audit evidence without making duplicate business journals. Transactions and idempotency remain required safeguards.

Do not depend on saving an error only after rollback: a crash in between can lose that error. Save the content first; recovery must be able to determine the outcome even if the failure-status update did not complete. This is not a claim that posting and every failure-status update occur in one successful commit.

Balance rules protect effective accounting entries, not an invalid draft awaiting correction. Protect saved content and error details with current organization/ledger access and retention rules; store no authentication secrets or unrelated data.

This remains a proposal, not implemented or tested behavior. Crash recovery and duplicate prevention need fault tests before any reliability claim.

## Review checks

- An unbalanced draft can be saved but cannot post or affect a report.
- Cross-ledger accounts, inactive accounts, negative lines and excess precision are rejected at posting.
- A posting either appears fully in journal history and balances or not at all.
- Retrying after a lost response returns the same posting, not a duplicate.
- Concurrent edits, posting attempts, reversals and period closure produce one clear valid outcome.
- A reversal leaves the original visible and affects only the reversal's accounting date.
- Revoked access cannot be reused by an old Frontend session or MCP request.

These are proposed acceptance scenarios, not completed tests.

## Open Questions

1. Are manual journals enough initially? Do editors also post and reverse, or is approval required?
2. What amount precision, maximum value and rounding rule are required?
3. What numbering rule, date restrictions and close/reopen process should apply?
4. Who may reopen an administrator-controlled cutoff? Are optional reversals needed?

[Business workflow diagrams](11-workflows.md)
[Next: Balances and Reports](06-reports.md) · [Document index](README.md)
