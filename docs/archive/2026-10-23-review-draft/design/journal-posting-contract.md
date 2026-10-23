# Journal posting contract

Status: Technical proposal, not implemented or tested. This document explains the confirmed business rules in [Journals and Posting](../05-journals-posting.md). It does not authorize coding, database changes or deployment. C#, ASP.NET Core, EF Core and PostgreSQL remain the selected direction.

## 1. Business rules versus design choices

**Confirmed requirements**

- JournalLine includes Qty, Item and optional PartyId separately from Account. Party requiredness follows the applicable business and Account rules, not a blanket rule for every line.
- Effective requirements combine Journal common rules, the subsystem's rules and each selected Account's rules. They add requirements; one layer cannot waive another.
- Normal Save preserves input and attempts posting in one user action. Save Draft does not post.
- Valid official source changes and their Journal/accounting effects must succeed together. A failed edit preserves the last valid source version and posting.
- Current permission, one transaction currency/shared rate, balance validation, Close, Error and Pending rules still apply.

**Proposed technical details**

The types, versions, rule metadata and transaction sequence below are a design for one modular monolith and one PostgreSQL database. They are not tested contracts. No queue, microservice or separate database is required. Future external subsystems would need a separate integration design; this same-database guarantee must not be claimed across independent systems.

Qty does not automatically mean Qty × Price, stock movement, inventory costing or a tax base. Unit is a proposed companion reference only where a selected rule needs it. No unit conversion is implied. Specific Customer, Supplier, stock and tax requirements are examples to define per module, not approved features.

## 2. One authoritative requirement contract

The server resolves the operation, source kind, ledger, selected Accounts and current rule versions. The client cannot supply a trusted rule set or mark a required field optional.

For each line:

**Effective requirements = common Journal requirements + source subsystem requirements + that Account's requirements.**

For a manual journal there is no subsystem layer; common and Account rules still apply. Apply common header and cross-line checks as well as line requirements.

Proposed composition:

1. Resolve allowlisted typed field rules for this source kind and ledger.
2. Add the selected Account's applicable rules for each line.
3. Required fields are the union. Allowed values/ranges must satisfy all applicable constraints, not an override chosen by priority.
4. Apply conditional rules against the actual current input, for example a required Unit only when a particular Qty rule needs one.
5. Reject incompatible rule definitions as a configuration conflict. Do not hide a required field or silently choose the less restrictive rule.
6. Validate references, ownership, active/usable state, decimal rules, currency and balanced amounts on the server.
7. Recheck the rule set at commit. A changed rule set requires revalidation; do not trust an earlier Frontend preview.

A rule may describe a field's type, required condition, allowed reference scope and user-facing explanation. It must not contain arbitrary user-provided code, SQL or expressions executed as code. Account rule-configuration permissions and actual module-specific rules need separate design.

Frontend, direct API and MCP use this same server contract. A proposed requirements query returns the permitted fields, required conditions, safe labels and rule-set version for the current authorized context. Frontend uses it to show relevant details; API/MCP callers receive the same meanings and errors. Hiding a field is not enforcement, and a stale preview does not authorize a write.

Return errors with stable line ID, field, code and rule origin (Common, Subsystem or Account). Do not reveal another ledger's account or rule details. Unknown fields and unauthorized reference values are rejected, not saved as unrestricted extension data.

<a id="account-usage-is-eligibility-not-a-default"></a>

### Account Usage describes business purpose

Account Usage is a many-to-many business-purpose association, such as Receivable or Revenue, not module/caller authorization. Several accounts may share a Usage and one account may have several Usages. Prohibit only the duplicate mapping **ledger + account + usage**; do not require unique Usage across accounts or one Usage per Account. Account Code remains unique within its ledger.

A future Sales A/R slot can offer several accounts with Receivable purpose. The subsystem owns its role-to-purpose mapping and business validation; Journal owns common and Account-detail checks. The composed contract still enforces all applicable layers. Concrete purpose vocabulary and subsystem mappings are defined when those subsystems are designed, not imposed by this document.

Blank Usage is undeclared, not universal suitability and not an automatic denial of manual Journal posting. A manual Journal can use an otherwise valid account without a Usage declaration, subject to common/Account rules and current permissions. A subsystem cannot infer suitability for its role merely from a blank value.

Save the actual AccountId. Suggestions and later Usage changes never rewrite historical selections. Item may still suggest an Account without introducing Default Account settings. No Default Usage, Default Account, Link or Account Non-Usage field is added.

Confirmed source relationship: a Receipt settling a particular Sales source keeps the corresponding Party and receivable account rather than silently choosing another account with the same Usage. This does not approve the complete Receipt allocation policy or implementation.

## 3. Proposed typed C# shapes

These are illustrative design signatures, not source files or compiled code. Exact serialization and API routes remain to be reviewed. Decimal values use decimal strings on the wire and checked decimal representations internally; precision and ranges must be explicit. AccountingDate below means a dedicated accounting-date value, not an implicit UTC timestamp.

```csharp
enum SaveIntent { Draft, SaveAndPost }
enum PostingOutcome { DraftSaved, Posted, Error, Pending }

record SourceRef(string Kind, Guid Id);
record JournalLineCandidate(
    Guid LineId,
    Guid AccountId,
    Guid? PartyId,
    Guid? ItemId,
    decimal? Qty,
    Guid? UnitId,
    decimal? TransactionDebit,
    decimal? TransactionCredit,
    decimal? BaseDebit,
    decimal? BaseCredit,
    string? Description);

record SaveJournalRequest(
    Guid RequestId,
    Guid OrgId,
    Guid LedgerId,
    Guid JournalId,
    long? ExpectedJournalVersion,
    long? ExpectedCandidateVersion,
    SourceRef? Source,
    long? ExpectedSourceVersion,
    SaveIntent Intent,
    string RuleSetVersion,
    JournalCandidate Candidate);

record JournalCandidate(
    AccountingDate Date,
    string Currency,
    decimal? ExchangeRate,
    string? Reference,
    string? Description,
    IReadOnlyList<JournalLineCandidate> Lines,
    IReadOnlyList<Guid> AttachmentIds);

record FieldIssue(
    Guid? LineId, string Field, string Code,
    string RuleOrigin, string Message);

record SaveJournalResult(
    Guid RequestId,
    Guid JournalId,
    PostingOutcome Outcome,
    long CandidateVersion,
    long? EffectiveJournalVersion,
    long? EffectiveSourceVersion,
    IReadOnlyList<FieldIssue> Issues);
```

Authentication creates the actor Principal on the server. OrgId/LedgerId identify requested scope; they do not prove ownership or permission. There is no client actor-ID override. Exchange-rate source metadata is assigned server-side; manual entry records Manual.

These common types are not the whole future subsystem DTO. Each subsystem supplies its own allowlisted typed candidate, such as a dedicated source-document candidate, to the common application command. Do not replace it with an arbitrary Map/List of unknown fields. Its validated posting adapter derives the Journal proposal. Validate the composed rules against source details and generated lines together; copying selected source facts into a line must preserve meaning and traceability.

Qty is nullable in this proposed shape because requiredness is resolved by the composed rules. Missing is not zero; zero and negative Qty need explicit policy. Item remains optional unless an applicable accepted requirement makes it necessary. A draft can retain incomplete values; a posting must satisfy all active rules.

IDs are stable across retries. The server allocates or validates IDs in the authorized context; a client-supplied ID cannot take over an existing object. Keep JournalId, SourceRef and LineId separate from editable document numbers. New and removed lines retain traceability through revision history. Allocate IDs before the first posting attempt using a defined creation contract; do not generate a new business identity on every retry.

Expected versions refer to the current official versions for an edit. ExpectedCandidateVersion protects candidate revisions, including Save Draft; the initial candidate uses an explicit create-only expectation, not a wildcard update. New-record semantics, version tokens and identity allocation must be finalized before implementation. RuleSetVersion identifies a preview, not a permission or waiver.

### Party reference

Use [Party](../12-party.md) and its [technical design](party-technical-design.md) as supporting requirements, not a separate accounting engine. PartyId is an optional typed Journal line reference; when a rule requires Party, validate it along with the selected Account. Do not accept a separate CustomerId/SupplierId as an alternative path that bypasses the Party rule.

Proposed storage: Party records have stable identities and belong to the current ledger. A future shared master-data catalog would require explicit ownership/access mapping; it is not enabled by reusing an ID. All supplied references must pass current scope, availability and version checks. Preserve relevant historical Party labels by revision/snapshot, not by rewriting old postings from today's master settings.


Party is not an invoice-allocation key. Source links, line links and future allocation records remain separate identities. The confirmed source Party/A/R relationship is checked by the subsystem adapter and the common commit contract; full allocation behavior remains future scope.

## 4. Durable candidate and atomic official effects

Keep two logical concepts, which may share one database:

- **Candidate revision:** the user's saved input and attempted change, not yet an effective official source/posting.
- **Effective business state:** the last accepted official source version and the corresponding effective Journal/accounting result.

This is not a duplicate business document. One stable source/Journal identity can have saved candidate and effective versions. A failed edit must not overwrite the official source merely because its input was saved.

### Before attempting posting

Authenticate, check current access and bound/validate the transport payload. In a short transaction, preserve the authorized candidate and request identity durably with an input fingerprint. For a source document, preserve its candidate and linked Journal proposal together as applicable. Compare ExpectedCandidateVersion before persisting a new candidate revision; a stale candidate write fails without replacing newer saved input. Commit this before attempting official accounting effects. If candidate persistence fails, do not claim Saved or attempt posting.

For Save Draft, finish with DraftSaved and no new accounting effect. A draft edit does not remove an existing effective posting. Do not mislabel an intentionally incomplete draft as a failed posting attempt.

### Official-effects transaction

For SaveAndPost, begin the accounting transaction after the candidate exists:

1. Acquire the agreed authorization and ledger operation locks in a deterministic order. Recheck current actor access, ownership, current official/candidate versions and request state.
2. Check Close under the same serialization boundary used to change the cutoff. For an existing posting, both original and proposed accounting dates must be open. New postings must be after the cutoff.
3. Resolve current common, subsystem and Account rules. Revalidate the saved candidate and generate/check the journal proposal. Changes to posting-relevant rules and posting-relevant reference eligibility, including Account, Party, Item and Unit participate in the same lock/version protocol; verify validated versions remain current at commit so a change cannot slip between validation and posting.
4. Validate same-ledger references, one transaction currency, shared rate, amount precision and debit/credit balance. No source adapter may write balances directly.
5. Commit the accepted source version, Journal revision, complete accounting effect, source-to-journal linkage, successful operation receipt and audit evidence together.
6. For an edit, replace the old effective impact atomically; never expose old plus new as two current postings. For a permitted delete, remove the current effect and update the source's current-posting indication in the same transaction while retaining identity/history.
7. Return Posted only after commit is known to have succeeded.

The final version update uses optimistic checks as well as the serialization safeguards. A stale candidate or official version gives a conflict, not an overwrite. PostgreSQL constraints and reviewed transactions protect formal entries; EF Core SaveChanges alone is not proof of the entire boundary.

### Definite failure and uncertain outcome

Validation failure rolls back official effects. Record Error against the already durable candidate with safe field issues. The old effective source and accounting versions stay unchanged. Saving failure details after rollback is not the only copy of the input.

A crash may occur before the Error status update, or after a successful commit but before the response. Keep the request unresolved/Pending until recovery checks the committed receipt and effective versions. Do not label a known validation rejection Pending merely to avoid reporting it; do not label an unknown commit Error.

If recovery finds a committed success, return that receipt. If it can prove no official commit occurred, it may record the known error or safely resume the same request after current authorization, version and rule checks. Never start a new posting blindly. Candidate and request persistence makes recovery possible; it does not prove crash safety until fault tests pass.

Permission and transport errors are safe API errors, not automatically visible accounting Error-list items. An unauthorized caller cannot persist or retrieve another organization's payload.

## 5. Idempotency and concurrency

Scope RequestId by actor, client, ledger and operation; fingerprint the source/Journal identity, versions, intent and canonical candidate. Same identity and same input resolves to one outcome. Reuse with different input is a conflict. The fingerprint and retained candidate revision must be immutable for that request.

A correction uses a new candidate revision and request identity with expected current versions. Retries of the old attempt cannot overwrite it. Check current authorization before returning saved content or replaying a receipt; a cached result does not bypass revocation.

Protect both official effect uniqueness and source-revision linkage so concurrent requests cannot double-post the same intended revision. The exact unique constraints and request-retention policy remain engineering tasks. Do not claim that identical-looking but separately entered documents are automatically duplicates.

All official state, successful audit and success receipt commit together. Failure status may be a later update, recoverable from the durable candidate and request record. No external notification or upload participates in the database transaction.

### Request-state fencing

Serialize every official attempt and recovery decision on the same request record, using a row lock and a monotonic attempt/version fence. After acquiring it, read the committed success receipt before deciding to resume. Only the current fenced attempt may change processing or failure state. A delayed Error write must never replace a committed Posted result; a stale recovery worker must not start another official transaction.

Commit the Posted outcome and success receipt with official effects. Record a definite failure only through a compare-and-set on the unchanged current attempt after proving that attempt did not commit. Recovery takes the same lock/fence and rechecks receipts, candidate identity and current authorization. Terminal results are immutable for a request; a correction uses a new request. Exact SQL and crash behavior remain to be validated.

An otherwise valid posting to a Suspense Account has outcome **Posted**, not Error or Pending. Classification work remains separate from posting success.

## 6. Attachments and external effects

Attachment uploads happen outside the official-effects database transaction. Use stable Attachment IDs and verify ownership, availability and completed upload before accepting a reference. Optional means an attachment may be omitted. Every supplied AttachmentId must pass the checks; never silently discard a selected reference. A failed check preserves the attempted input and leaves previous official source, posting and attachment links unchanged. Accepted references identify a stable uploaded object/version. Return a clear failure rather than claim the database transaction rolled back the file upload.

Only validated attachment links join the source/Journal transaction. Link creation and any later cleanup-eligibility decision must share coordination so cleanup cannot remove a newly linked object. This does not make external storage transactionally atomic. An upload failure never creates a half-posted journal. Unreferenced upload retention/cleanup is a later policy, not permission to delete files now. Optional attachments remain optional; no attachment is required merely because this contract supports them.

Storage-container portability follows [Document Provider requirements](../09-deployment-operations.md#attachment-storage-portability). Do not store access tokens or temporary signed download URLs as permanent identities.

## 7. Acceptance plan and readiness

All cases are **Not run**:

1. Common rule requires a field that a subsystem calls optional: it remains required.
2. Account adds a required detail: Frontend preview and API/MCP errors agree; a manual journal cannot bypass it.
3. Two layers impose incompatible ranges or reference types: fail configuration validation, not weaken a rule.
4. Change Account or rule version after preview: server recomposes requirements and refuses a stale invalid submission.
5. Missing, zero, negative and high-precision Qty remain distinct; Qty alone creates no inventory movement or calculated money.
6. Save a valid source plus Journal: either both official versions and accounting effects commit, or neither does.
7. Fail an edit at each transaction step: previous official source and posting remain effective; attempted input survives.
8. Crash after candidate save, during official commit, after commit and before failure-status update: reconcile one outcome without loss of saved input or duplicate effects.
9. Concurrent same-request retries and different requests for the same source revision: one effective result; changed fingerprints and stale versions conflict.
10. Race access revocation or Close with posting/edit/delete: ordered result, original/target cutoff checks, no partial effects.
11. Simulate unavailable attachment upload or wrong-ledger Attachment ID: safe failure, no unauthorized reference or false upload rollback.
12. Verify TB, GL Detail and statements use only the current effective posting; failed revisions do not change totals.
13. Follow or retry a saved request after access removal: no cached cross-organization disclosure.
14. Save Draft with incomplete input: no posting attempt; an existing valid posting remains unchanged. Concurrent saves using the same candidate version cannot silently overwrite each other.
15. Race a late Error update and recovery against an in-flight or successful official commit: no terminal-state regression or duplicate posting.
16. Change rule definitions or posting-relevant reference eligibility, including Account, Item and Unit between validation and commit: one consistent ordered result, never an unchecked posting.
17. Valid Suspense posting returns Posted. An invalid explicitly selected optional attachment is not silently omitted; cleanup cannot race a new accepted link.

**Engineering owns:** final DTOs, rule metadata/versions, lock order, constraints, decimal limits, idempotency retention, recovery process and HTTP/MCP error mapping.

**Business choices still needed when relevant:** concrete subsystem/Account requirements; permitted Qty signs and units for each use; source-only versus direct edits of generated journals; reopening authority; detailed tax treatment. Do not turn these into assumed Customer, Supplier or inventory features.

No provider has been selected, no code or migrations written, and no tests run by this document.

[Journal requirements](../05-journals-posting.md#journal-core-contract) · [Document index](../README.md)


## FX valuation integration

The [confirmed FX workflow](../15-foreign-currency.md) adds a generated base-only valuation case to the shared posting boundary. The normal transaction DTO above is not yet a complete valuation DTO. Proposed implementation: an explicit validated system kind with exposure currency, zero original principal movement, balanced base movements, rate evidence and stable exposure/run links. Do not fake an exchange rate or relax ordinary single-currency validation. Revalidate as-of versions and Close, serialize incremental adjustment calculation, and commit official links and effects together. Exact typed extension remains design work; no implementation is claimed.

Settlement at a new rate needs the separate proposed representation in that page: normal shared-rate principal plus an atomically linked base-only settlement FX component. The ordinary DTO must not silently accept different line rates to clear prior carrying value. Both official components succeed or fail together.

## Selected ledger and Close contract

Use [Ledger accounting policy](../16-ledger-accounting-policy.md) for numeric(20,4) amounts, numeric(20,10) positive base-per-transaction rate, currency minor-unit validation before database coercion, exact scaled-integer/rational intermediate arithmetic with range-checked decimal storage and MidpointAwayFromZero final derived rounding. Official ordinary lines persist explicit transaction/base debit and credit amounts with exact balance in both currencies. Incomplete candidates may remain nullable; official persisted amounts cannot. The explicit generated FX kind permits zero transaction movement and nonzero balanced base movement; ordinary conversion checks still apply to normal Journals.

Use DateOnly / PostgreSQL date. One administrator Close command coordinates all generated FX Journals, links, cutoff, audit and success receipt in the same database transaction. Candidate/error durability is separate. Posting, Close and relevant master/rate updates share the existing lock order. All generated currency groups succeed together, not per-group partial success. The canonical calculated year-end stream is shared by TB, GL and statements; it is not a second stored ledger or physical closing Journal. No code has been compiled or tested.
