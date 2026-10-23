# System Design Principles

Status: System-design overview, not an implementation report. **Confirmed** identifies accepted business requirements. **Proposed** identifies how engineering intends to satisfy them. No code, deployment or real accounting changes are authorized.

## What each document does

- **Business requirements:** what users can do and the rules that must hold.
- **System design:** module responsibilities and how they work together.
- **Technical design:** C# types, APIs, storage, transactions and failure handling.
- **Acceptance:** evidence needed to prove the implementation works. All current cases are Not run.

## 1. Organization and ledger boundaries

**Confirmed:** an organization manages a team and several companies; each company has one independent ledger initially. Administrator rights stay inside that organization. Members receive read/edit access to assigned ledgers, not permission-management powers.

The server checks current access on every protected operation. Knowing an ID or switching the selected organization grants no permission. [Access rules](02-organizations-permissions.md)

## 2. One Journal accounting core

**Confirmed:** valid posting changes accounting results; drafts and failed attempts do not. Open posted journals may be edited or deleted with permission; history remains traceable.

**Proposed system boundary:** manual journals and future subsystem-generated journals use one common posting service. Subsystems prepare business details and journal proposals, not direct balance updates. This is the Subsystem Posting Contract; it need not be HTTP or a separate service. No Sales or inventory module is implemented by this design. [Journal core](05-journals-posting.md#journal-core-contract)

## 3. Requirements add together

**Confirmed:** final validation combines Journal common rules, subsystem requirements and every line's Account requirements. A manual journal still applies common and Account rules.

**Proposed mechanism:** the server returns a typed requirement description for Frontend, API and MCP. Required fields form a union; compatible constraints all apply. Conflicting configuration fails rather than silently weakening validation. Qty does not itself imply inventory or Qty × Price. [Combined requirements](05-journals-posting.md#confirmed-combined-requirements)

## 4. One Save, safe accounting changes

**Confirmed:** normal Save attempts posting in one user action; optional Save Draft does not. The accepted source business version and Journal/accounting effects must be atomic. A failed edit preserves the last valid source and posting.

**Proposed mechanism:** preserve candidate input durably first, then commit official source/Journal changes, successful audit and result together in one database transaction. Failure details may be recorded afterward because the input already survives. Unknown outcomes are reconciled before retry. One click is not a claim that external uploads or every failure-status update share that transaction. [Transaction design](design/journal-posting-contract.md#4-durable-candidate-and-atomic-official-effects)

## 5. Suspense, Error and Pending are different

- **Suspense:** valid posted accounting awaiting classification. It affects balances.
- **Error:** definite validation or processing failure needing correction. Failed content has no new accounting effect; an unsuccessful edit keeps the previous valid effect.
- **Pending:** genuinely unfinished or unknown outcome. Check what committed before retrying.

These categories are confirmed; a specific page layout is not. [Outcomes](05-journals-posting.md#suspense-error-and-pending)

## 6. Close protects the accounting cutoff

**Confirmed:** organization administrators control Close. The cutoff is inclusive and uses accounting date/time, not entry time. New posting at/before the cutoff is blocked; covered posted journals cannot be edited or deleted. Date-changing edits check both original and target dates. Reopening authority remains open.

**Proposed mechanism:** posting and Close share an ordered transaction boundary so a concurrent change cannot slip through. [Close rules](05-journals-posting.md#confirmed-edit-or-delete-a-posted-journal-before-close)

## 7. Trace sources without losing history

**Proposed:** generated journals keep stable source, request and line identities, with the source revision used for posting. Source status and effective posting must agree. Retries do not duplicate effects; stale writes cannot silently replace newer ones. Preserve links and audit after a permitted deletion.

Whether users correct generated journals directly or only through the source remains a business choice before connecting each subsystem. [Source traceability](05-journals-posting.md#proposed-source-traceability)

## 8. Attachments keep their identity

**Confirmed:** attachments are optional; changing storage containers must preserve files and links.

**Proposed:** a Document Provider maps stable Attachment IDs to verified storage locations. Uploads are outside the accounting database transaction; accepted references are checked for access and availability. Copy verification and rollback are required design work, not a guarantee already tested. [Attachment portability](09-deployment-operations.md#attachment-storage-portability)

## 9. Reports share one accounting truth

**Confirmed:** use TB, GL Detail, Balance Sheet and P&L; no duplicate GL Summary. Reports use the effective posting, not failed candidates or retained superseded versions. Calculated Current Year Profit / Loss must not duplicate Retained Earnings.

**Proposed:** report totals and drilldown keep consistent scope and data revision, or explicitly refresh after changes. Detailed year-end transfer calculations still need design. [Reports](06-reports.md)

## 10. Keep the system small and the status honest

**Confirmed direction:** C#, ASP.NET Core, EF Core and PostgreSQL; no ABP in the first phase. API and MCP share backend rules. Frontend implementation is deferred.

**Proposed structure:** one modular monolith. No queue, microservice or separate posting database is required by the current design. Exact provider, versions, typed contracts and transaction mechanisms remain unverified proposals.

The organization foundation task list remains separate from accounting implementation. Documents are not deployed controls. Development requires a separate instruction; deployment requires separate authorization.

[Business requirements and technical details](README.md) · [Acceptance plan](08-testing-acceptance.md) · [Decision record](10-decisions.md)
