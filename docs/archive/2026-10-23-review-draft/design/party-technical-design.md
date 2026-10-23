# Party technical design

Status: Proposed C# / ASP.NET Core / EF Core / PostgreSQL design. No implementation, migration or runtime tests have run. The [Party requirements](../12-party.md) define confirmed relationships and proposed master policies.

## Purpose and boundary

This document makes the minimal Party master reviewable before coding: fields, API, storage, permission checks, concurrency, history and acceptance. It does not add CRM, balances, invoice allocation, Party attachments, a role taxonomy or a new accounting engine.

Party belongs to one ledger and its organization. The same identity may represent a customer and supplier without duplicate records. This master model is proposed; the optional JournalLine PartyId and conditional business/Account requiredness are confirmed. The organization foundation may exist before accounting settings are configured; creating a Party does not configure currency, fiscal year or posting readiness.

## 1. Field and normalization contract

All limits below are proposed, not previous business confirmations. Validate Unicode scalar counts consistently in .NET and PostgreSQL, not UTF-16 code-unit length.

| Field | Proposed contract |
| --- | --- |
| id, org_id, ledger_id | UUID, server-assigned/validated, immutable. |
| name | Required, NFC-normalized, trim outer whitespace, 1–200 Unicode scalar values. Preserve internal spacing/case. |
| code | Optional, trim; ASCII letters/digits and hyphen/underscore/period only; 1–64 characters. Store uppercase canonical form. Blank becomes null. |
| tax_number | Optional trimmed text, at most 80 scalars; preserve leading zeros. No legal validation or uniqueness. |
| address | Optional trimmed multiline plain text, at most 1,000 scalars. |
| contact | Optional trimmed plain text, at most 500 scalars; not a contact-list schema or messaging command. |
| is_active | Boolean, initially true; changed by state actions. |
| version | Positive bigint, initially 1; increment on each actual state change. Wire format is a decimal string. |
| created_at, updated_at | Server UTC timestamps, not accounting dates. |
| created_by, updated_by | Stable internal user IDs from the server Principal. |
| deleted_at | Nullable UTC tombstone time. Deleted implies inactive. |

Optional empty/whitespace-only text becomes null. Reject invalid Unicode, NUL and disallowed control characters; Address may contain normalized line breaks. No HTML interpretation. Unknown request fields are errors.

Code is unique by ledger when present, including tombstones. This reserves the current Code at deletion, not all former Codes: no alias registry is proposed. Names and tax numbers are not unique. Exact matching is not evidence of duplicate identity; no automatic merge.

## 2. Proposed DTOs and API

Illustrative signatures only. The HTTP representation uses snake_case. Decimal-string version serialization must be defined explicitly; these declarations alone do not implement it.

```csharp
record PartyFields(
    string Name, string? Code, string? TaxNumber,
    string? Address, string? Contact);

record PartyView(
    Guid Id, Guid OrgId, Guid LedgerId, PartyFields Fields,
    bool IsActive, long Version,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

record MutationReceipt(
    Guid RequestId, Guid PartyId, string Outcome, long Version);

record FieldIssue(string Field, string Code, string Message);
```

Authenticated actor identity is not a request field. Routes identify requested scope; the server verifies ownership. Keep transport DTOs separate from EF entities; clients cannot bind org_id, version, audit fields or deleted_at through mass assignment.

Prefix: `/api/v1/organizations/{org}/ledgers/{ledger}/parties`.

| Method/path | Input | Result |
| --- | --- | --- |
| POST collection | PartyFields | 201, PartyView and ETag. Active starts true. |
| GET collection | q, state, limit, cursor | Authorized nondeleted summaries and next_cursor. |
| GET /{party} | None | 200 PartyView and ETag, including inactive records. |
| PUT /{party} | Complete PartyFields | 200 new PartyView; omitted optional values are cleared. No partial-update ambiguity. |
| POST /{party}/deactivate | Empty object | 200 PartyView; state change only. |
| POST /{party}/activate | Empty object | 200 PartyView; deleted records cannot activate. |
| DELETE /{party} | No body | 200 MutationReceipt for eligible unused tombstone. |

Every mutation requires UUID `Idempotency-Key`; existing-record mutations also require `If-Match: "vN"`. Missing version: 428; stale version: 412. A successful actual change returns incremented Version and ETag. An already-active/inactive action using the current Version returns unchanged state plus a successful receipt, without inventing another state-change audit event.

Use the existing envelope: `data`, `request_id`; lists add `page.next_cursor`. Errors use application/problem+json with stable code, safe detail and optional field_errors. Proposed codes:

- 400 INVALID_ARGUMENT / INVALID_CURSOR.
- 401 UNAUTHENTICATED.
- 403 FORBIDDEN for a visible resource but forbidden operation.
- 404 NOT_FOUND for absent, deleted or invisible Party/scope; do not distinguish foreign-ledger existence.
- 409 PARTY_CODE_CONFLICT / PARTY_IN_USE / PARTY_DELETED / IDEMPOTENCY_KEY_REUSED.
- 412 VERSION_CONFLICT; 428 PRECONDITION_REQUIRED.
- 503 TEMPORARILY_UNAVAILABLE for unavailable dependencies, not an empty successful list.

Return PARTY_DELETED only when a safe authorized context already permits that disclosure; ordinary GET stays 404. Never reveal another ledger's Code collision or reference details.

### Search and paging

Proposed q searches Name or canonical Code by case-insensitive prefix, not arbitrary SQL. state is active (default), inactive or all. Deleted records are excluded. limit defaults 50, range 1–100. Sort created_at,id ascending. Authorize and scope before filtering/counting. Return no default total count.

Use the foundation's opaque signed cursor bound to actor, scope, filters, sort and first-page creation cutoff. Recheck access each page. This is stable keyset navigation, not a guaranteed cross-request snapshot; changes/removals may alter results. A cursor cannot change ledger scope.

MCP adapters may expose equivalent bounded operations only through the existing authenticated/delegated shared service. They cannot bypass authorization with direct DbContext access. No Party-specific superuser token or new delegation grant is created.

## 3. Illustrative PostgreSQL model

This is design text, not a runnable migration. Existing ledgers must provide the referenced composite uniqueness.

```sql
CREATE TABLE parties (
    id uuid PRIMARY KEY,
    org_id uuid NOT NULL,
    ledger_id uuid NOT NULL,
    name text NOT NULL,
    code text NULL,
    tax_number text NULL,
    address text NULL,
    contact text NULL,
    is_active boolean NOT NULL DEFAULT true,
    version bigint NOT NULL DEFAULT 1 CHECK (version > 0),
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    created_by uuid NOT NULL REFERENCES users(id),
    updated_by uuid NOT NULL REFERENCES users(id),
    deleted_at timestamptz NULL,
    UNIQUE (org_id, ledger_id, id),
    FOREIGN KEY (org_id, ledger_id)
      REFERENCES ledgers(org_id, id) ON DELETE RESTRICT,
    CHECK (char_length(name) BETWEEN 1 AND 200),
    CHECK (code IS NULL OR
      (char_length(code) BETWEEN 1 AND 64
       AND code ~ '^[A-Z0-9._-]+$')),
    CHECK (tax_number IS NULL OR char_length(tax_number) <= 80),
    CHECK (address IS NULL OR char_length(address) <= 1000),
    CHECK (contact IS NULL OR char_length(contact) <= 500),
    CHECK (deleted_at IS NULL OR is_active = false)
);
CREATE UNIQUE INDEX parties_ledger_code
  ON parties (ledger_id, code COLLATE "C")
  WHERE code IS NOT NULL;
CREATE INDEX parties_ledger_page
  ON parties (org_id, ledger_id, created_at, id)
  WHERE deleted_at IS NULL;
```

The unique Code index deliberately does not exclude deleted rows. Composite foreign keys on each integrated source/Journal/candidate reference include org_id, ledger_id and party_id with ON DELETE RESTRICT. Nullable PartyId does not make nonnull references cross-ledger safe by itself.

FKs preserve identity but do not enforce Active or tombstone eligibility. Every reference-writing service participates in the transaction protocol below. A new integration must declare its references and tests before launch. Do not claim a polymorphic string type/id registry is an enforceable FK.

Map Version as an EF Core concurrency token, not SQL Server rowversion. Use explicit scope conditions in reads and writes; query filters are supplementary only. Map nullable text and owned/request value objects without exposing EF entities to clients. Direct SQL/bulk writes must preserve the same scope, version and audit conditions.

Normalization is performed by one server routine and tested against DB constraints. The illustrative CHECKs are not a complete normalization/security implementation. Use reviewed migrations to add any additional database guard, rather than claiming a default or CHECK implements the entire service policy.

## 4. Permission and transaction protocol

Proposed maintenance rights: current org administrator or ledger-edit member may mutate; read-only members may read. Existing membership/grant and MCP delegation checks apply. No Party permission-management power is added.

Reuse the foundation's lock hierarchy: acquire the organization authorization lock before the ledger lock, then Party rows in stable ID order. Use the same organization-lock mode as the adopted foundation contract; this proposal does not weaken its current-permission ordering. All Party mutations, creation of Party references, candidate saves and Journal posting affecting Party references take the ledger write lock before reference/Active checks. This deliberately serializes a ledger's relevant writes for the initial design; throughput optimization comes later.

No competing path may lock Party first and then the ledger. Cross-ledger commands are out of scope. The Journal/Close path must use the compatible ledger boundary and preserve its original/target accounting-date checks. Locks are proposed mechanisms, not tested guarantees.

### Create or update

1. Validate bounded transport, authentication and requested scope.
2. Begin transaction; acquire authorization/ledger locks and check current permissions.
3. Resolve Idempotency-Key under actor + client + ledger + operation scope. Fingerprint normalized input, target and expected Version.
4. On replay, recheck current access before returning saved IDs/receipt. Different input with the same key is 409. Same request cannot create a second Party.
5. For update, load the current nondeleted Party and compare If-Match. Normalize/validate fields and Code uniqueness; do not overwrite from a stale form.
6. Apply state and increment Version when changed. Write safe audit and successful receipt in the same transaction.
7. Commit, then return success. Audit failure rolls back state. A uniqueness race maps to a safe Code conflict.

Idempotency follows the existing foundation retention proposal (seven days); expiry ends the replay guarantee. An unresolved create after expiry must not be automatically recreated: absence of a retained receipt is not proof of failure. Require outcome investigation instead of guessing from matching names. Never retry only an intermediate SaveChanges. A connection failure with uncertain commit outcome requires receipt/state reconciliation under the same locks before retry. Do not return a new Party identity or false failure merely because the response was lost.

Definite validation failures need no Journal Error record. They leave the master unchanged and return a normal API error. Party operations do not create accounting effects.

### Deactivate / activate

Under the same locks, current permissions and Version checks, change Active and audit it. No balance query or synthetic balance is needed. Deactivation does not change source/Journal history; activation does not post drafts.

Proposed reference policy:
- New or changed Party references require a nondeleted active Party.
- A retained draft/candidate may retain its existing inactive reference for correction, but this does not authorize a new posting with it.
- On a permitted edit of an effective Journal, an unchanged inactive Party is allowed only on the same stable line ID with the same PartyId as the immediately previous effective version. Mere presence on some older line is insufficient.
- New lines, copied lines, changed PartyId or changed source relationships require active eligibility and all source/common/Account checks.
- Historical reads can resolve inactive identities. The exception does not bypass Close, current access or changed rules.

### Safe delete

Under the same lock order and current Version, check all registered reference owners: retained drafts/candidates, official and historical source revisions, Journal revisions/lines, allocations if introduced, and any existing attachment link that needs Party identity. A reference count is not authorization to expose those records.

If any reference remains, return PARTY_IN_USE without changing state. For an unused Party, set deleted_at, is_active=false, increment Version and commit audit plus receipt. Keep identity, scope and current canonical Code reserved. No FK cascade or physical purge; master audit remains. Ordinary activation cannot restore a tombstone.

Reference creation and deletion share the ledger lock, so either the reference wins and blocks delete, or delete wins and blocks the new reference. FKs are a second protection against physical dangling references, not a replacement for tombstone checks. A new subsystem's reference owner must be included before its writes are enabled.

Party has no direct attachment feature now. Files linked from an integrated source stay outside the DB transaction; this design performs no upload, unlink or file deletion.

## 5. Historical representation

Proposed: effective Journal/source revisions retain PartyId plus accepted Name/Code snapshots. Current selectors display current master labels. Historical views display saved labels with an authorized link to current master, clearly distinguished.

Capture snapshots under the same reference-validation lock as the official transaction. Master rename cannot retrospectively rewrite them. A new successful posting revision gets its own snapshot while the previous revision remains. Tax Number, Address and Contact are not copied to every Journal line.

Source and Journal revisions still commit atomically under the [Journal contract](journal-posting-contract.md). Failure preserves the previous valid source/posting and durable candidate; Party master changes never directly recalculate accounting balances.

## 6. Migration and verification plan

Design only:
1. Add Party table, indexes and composite reference keys through reviewed EF Core migrations.
2. Define the existing ledger composite key and all referencing tables explicitly; do not invent migrations for future modules.
3. Add nullable PartyId and proposed snapshots to the relevant Journal revision model only after implementation is separately approved.
4. Inspect generated SQL and test forward migration on a synthetic empty and representative database. Do not seed real Parties or assign arbitrary Party IDs to historical rows.
5. Plan rollback/data preservation separately; migration rollback is not permission to erase records.

No migration has been generated or applied. No production access, credentials, seeds or schema changes exist from this documentation work.

## 7. Acceptance and failure tests

All cases are Not run. Use synthetic ledgers and users, real PostgreSQL connections and deterministic barriers for race tests; EF InMemory is not evidence of SQL locking behavior.

| Test | Evidence required |
| --- | --- |
| Field limits/normalization | NFC/scalar length, blank optional values, canonical Code and invalid controls behave consistently in API and storage. |
| Tenant isolation | Wrong organization/ledger references fail in service and composite FK paths; no search/cursor/error leakage. |
| Permission change | Revocation ordered before write denies it; a prior committed write stays audited. API and MCP agree. |
| Versions | Two updates with one Version yield at most one mutation. Missing/stale preconditions return 428/412. |
| Idempotency | Concurrent same-key requests and lost responses produce one mutation/receipt; changed input conflicts. Revoked callers cannot replay protected details. |
| Code uniqueness | Same canonical Code conflicts within ledger, including tombstones; another ledger may reuse it. Names/tax numbers are not uniqueness keys. |
| Reference/deletion race | Reference first blocks deletion; deletion first blocks new reference. Include retained candidates and historical revisions. |
| Active/posting race | Deactivation and posting serialize; no unchecked new reference. Unchanged inactive exception matches current effective line identity only. |
| History | Rename changes current master, not old snapshots, balances or Journal IDs. |
| Delete behavior | Referenced Party is blocked; unused tombstone preserves audit/identity/code and cannot ordinary-activate. No cascade. |
| Failure injection | Fail audit, state update and commit response; no partial state, false success or duplicate retry. |
| Journal integration | Conditional Party requiredness, multiple Parties, source Party/A/R preservation, Close and failed-edit preservation all hold. |

The [requirements acceptance matrix](../12-party.md#8-acceptance-matrix) supplies user-visible outcomes. Record tested commit/environment, actual results and untested cases before any implementation-ready claim.

## Rationale and remaining choices

Stable identity and composite ownership prevent name changes or guessed IDs from changing references. A ledger serialization boundary is intentionally simple and reviewable. Tombstones preserve audit and avoid promising irreversible erasure or recovery. Snapshots separate historical facts from current master labels.

Owner choices remain the minimal fields, optional unique/reserved Code and inactive-reference policy from the requirements. Engineering owns exact DTO serialization, SQL, locks, error mapping and test harness. Concrete business rules requiring Party and future source allocation/correction ownership still belong to their workflows. No additional role, accounting policy or module is approved here.

[Party requirements](../12-party.md) · [Documentation contents](../README.md)

