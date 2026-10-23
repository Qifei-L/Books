# System Design Principles

Status: System-design overview, not an implementation report. **Confirmed** identifies accepted business requirements. **Proposed** identifies how engineering intends to satisfy them. No code, deployment or real accounting changes are authorized.

## Six confirmed design principles

These six principles are confirmed. The Books examples explain their application; they do not approve every linked implementation proposal. Earlier confirmed business rules remain in force.

### Single Source of Truth

**Principle:** each concern has one authoritative owner.  
**Why:** independent copies of the same business fact can disagree.  
**Books example:** a subsystem owns its business document; Journal owns the effective accounting result. Reports derive accounting amounts from that result, not a second set of books. Linked source and Journal records have different responsibilities, not competing truth. [Journal core](05-journals-posting.md#journal-core-contract)

### Consistency by Design

**Principle:** related official changes succeed or fail together.  
**Why:** a saved official source without its matching accounting effect is misleading.  
**Books example:** accept the source version and its Journal effects atomically. A failed edit preserves both previous official versions. Candidate input and Error details may remain for correction; external files do not become transactional database records. The same-database transaction mechanism remains a proposal. [Transaction boundary](design/journal-posting-contract.md#4-durable-candidate-and-atomic-official-effects)

### Traceability

**Principle:** explain each number and each change through its source, actor and reason.  
**Why:** users need to investigate differences without guessing what happened.  
**Books example:** follow a report amount to Journal and, where applicable, its source revision. Keep who changed or deleted what, when and why. An open posted journal may still be edited or deleted under the confirmed Close rule; traceability does not force reversal-only accounting. Exact reason fields and retention remain design details. [Source traceability](05-journals-posting.md#proposed-source-traceability)

### Secure by Default

**Principle:** the server enforces organization, ledger and operation access at every entry point.  
**Why:** a client, link or earlier login is not proof of current permission.  
**Books example:** Frontend, direct API and MCP face the same current-access checks. Account Usage eligibility is not authorization. A removed member cannot use a saved request or guessed ID to recover protected data. This adds no role hierarchy or real permission grant. [Access design](design/organizations-permissions.md#check-access-on-every-operation)

### Explicit over Implicit

**Principle:** make rules, states and errors visible; do not silently guess or change their meaning.  
**Why:** hidden assumptions cause incorrect entries and unsafe retries.  
**Books example:** valid posted Suspense, definite Error and unknown Pending are different. Show the actual selected Account and specific validation issues. Common, subsystem and Account requirements add together; no layer silently waives another. A timeout is not proof of failure. [Combined requirements](05-journals-posting.md#confirmed-combined-requirements)

### Design for Change

**Principle:** connect modules through clear, stable contracts so change is controlled.  
**Why:** adding a subsystem should not require copying or bypassing the Journal rules.  
**Books example:** future subsystems use the common posting boundary; stable identities keep source and attachment references meaningful. Stable does not mean frozen forever: incompatible changes need explicit versioning and migration. This is not a promise of no future core changes or approval of a new module. [Posting contract](design/journal-posting-contract.md)

## How these principles apply in Books

The sections below summarize existing requirements and proposed mechanisms. Their status labels still apply; accepting a high-level principle is not blanket implementation approval.

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
