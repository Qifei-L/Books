# Phase 1: organization access and API contract

Version: 0.4 | Date: 2026-10-23 | Status: proposed technical details, not implemented

Based on branch `docs/requirements-review`, source commit `f4f2a2dc93ef351402dbee6e04fadd0b24b88c83`. See [Draft PR #1](https://github.com/Qifei-L/Books/pull/1) and the [permissions overview](organizations-permissions.md).

## 1. What is decided, and what is not

**Confirmed:** C# + ASP.NET Core + EF Core + PostgreSQL. API/MCP first, with a shared backend. No ABP in phase 1. The business rules below are also confirmed.

**Proposed:** a modular monolith, the specific versions, identity provider, fields, paths, limits and transaction design in this document. HTTP and MCP share application services, authorization, transactions and data access. Do not introduce microservices, Redis, a message queue, a separate permission engine or a custom OAuth server for this phase.

**Open questions:** exact dependency versions; identity provider and its operator; accounting settings and engine; production provider, region, costs, domain, backup and release plan.

**Not approved:** coding, migrations, account or credential creation, production access or deployment. Reviewing this design does not authorize those actions. Obtain a separate instruction to start coding and separate approval for production arrangements.

Keycloak is a candidate to test, not a selected provider. A mature provider can handle registration, verification, login, recovery and OAuth. It also adds a service, database, upgrades, backups, email setup and security maintenance. That may be too much for a small project. Evaluate a suitable existing identity platform first; do not create services or paid accounts without approval.

After coding is approved, start with a small isolated authentication compatibility test. Keycloak and the target MCP client's resource parameter, audience, discovery and registration behavior have not been tested. If they fail, report the gap and revise the design. Never fall back to a shared administrator token.

## 2. Phase-1 scope and confirmed business rules

Proposed phase-one implementation scope, after separate coding approval: identity mapping, organizations, members, company/ledger foundation records, invitations, ledger grants, audit, HTTP/MCP contracts and automated tests. Do not build accounts, entries, vouchers, balances, reports or the accounting engine. The Books Web UI is paused. Using an identity provider's login page is not building a Books Web UI.

Confirmed rules:

- A registered, verified user can create an organization and becomes its first administrator.
- One user can join several organizations. Each organization has separate permissions.
- One organization can contain several companies. Each company has one independent ledger. The organization does not post entries or automatically consolidate accounts.
- Administrators manage all ledgers, members and permissions in their organization. Ordinary members receive `read` or `edit` per ledger and cannot grant access to others.
- The last administrator must hand over before leaving. Normal membership changes must leave at least one administrator.
- Joining requires the user's explicit acceptance. Invitations last seven days, can be revoked and must be reissued after expiry. An inviter losing administrator access invalidates their pending invitations.
- Rejoining and changing from administrator to ordinary member require fresh, explicit grants. Old access does not return automatically.
- All entry points, including MCP, use the same identity, ownership, permission and business checks.

Base currency, financial year, accounting time zone and engine are not decided. Phase-1 ledgers contain identity and display fields only. Return `accounting_setup_status=not_configured`. These are access-control foundation records, not ledgers ready for posting. Do not silently choose USD, a calendar year or UTC as accounting defaults. UTC system timestamps do not decide the accounting time zone.

## 3. Proposed stack and module boundaries

| Part | Proposal | Conditions |
| --- | --- | --- |
| Host | C# and ASP.NET Core; .NET 10 LTS candidate | The earlier source review recorded patch 10.0.13. This is not a locked or newly verified version. Recheck support and security patches before implementation; pin SDK/runtime, not `latest`. |
| Data access | EF Core 10 and Npgsql.EntityFrameworkCore.PostgreSQL 10 candidates | EF Core 10 requires .NET 10. Check exact package compatibility and transitive dependencies, then lock versions. Matching patch numbers do not prove compatibility. |
| Database | PostgreSQL; version 18 candidate | Retain transactions, composite foreign keys and row locks. Check support, patches and image digest before implementation. No prerelease versions. |
| Migrations | EF Core Migrations plus reviewed PostgreSQL SQL or migration bundle | Review generated SQL. Include deferred constraints/triggers as explicit SQL in versioned migrations where needed. Run migrations as a separate release step, never automatically at application startup. No migration is generated or run now. |
| Authentication | ASP.NET Core JWT bearer/OIDC with a standard identity provider | Keycloak remains a candidate. Validate access tokens, not ID tokens. ASP.NET Core Identity manages accounts; it is not a complete OAuth/OIDC authorization server. |
| MCP | Official `modelcontextprotocol/csharp-sdk`; `ModelContextProtocol.AspNetCore` host | Streamable HTTP in the API process. Keep audience, delegation and current-access checks. No superuser account. Verify and pin SDK/protocol compatibility. |
| Contracts and tests | OpenAPI 3.1, JSON Schema; xUnit, ASP.NET Core integration tests, real PostgreSQL | Run unit/integration tests with `dotnet test` after implementation is approved. EF InMemory and SQLite cannot substitute for PostgreSQL foreign-key, locking or concurrency tests. Test package versions remain open. |

References retained from the source review: [.NET support](https://dotnet.microsoft.com/en-us/platform/support/policy), [EF Core 10](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/whatsnew), [Npgsql EF Core 10](https://www.npgsql.org/efcore/release-notes/10.0.html), [Npgsql provider](https://www.npgsql.org/efcore/), [migration deployment](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying), [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk), [ASP.NET Core Identity](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity?view=aspnetcore-10.0). No dependencies have been installed or compatibility tests run.

### EF Core must preserve the security model

Use one `DbContext` per request/application command. Do not share it across concurrent operations. Every read and write keeps explicit organization conditions, service authorization and composite foreign keys. Global query filters are only an extra guard. `IgnoreQueryFilters` cannot bypass authorization.

Use the PostgreSQL locks and same-transaction checks in section 6. Parameterized SQL for `FOR UPDATE`/`FOR SHARE` and reviewed deferred constraints must share the EF connection and transaction. A single `SaveChanges` transaction is not the whole business transaction.

Use an explicitly incremented positive `bigint version` as an EF concurrency token. Keep the API's decimal-string version. Map `DbUpdateConcurrencyException` to version conflict, not an overwrite. Do not substitute SQL Server `rowversion`. Bulk or raw SQL updates need explicit organization and version conditions. See [EF concurrency](https://learn.microsoft.com/en-us/ef/core/saving/concurrency).

Connection retries must retry the complete transaction under the idempotency protocol. Do not retry just an intermediate `SaveChanges` or duplicate an audit event. Business state, audit and the successful idempotency result commit together. Read current authorization state.

Use one .NET solution with API/MCP adapters, Application, Domain and Infrastructure boundaries. Business modules are Identity, Organizations, Companies, Memberships, Invitations, Authorization and Audit. Keep them lightweight; do not make each entity a service or add ABP. Separate routes/DTOs, application services and persistence. Services enforce authorization, not just middleware or endpoint attributes. MCP cannot directly use `DbContext` or repositories. Every write command receives a server-created Principal, never a request-body `actor_user_id`.

## 4. Identity and authentication

### Responsibilities and client identity

The identity provider handles registration, email verification, login, password recovery, MFA, OAuth consent, token issuance and revocation. Books owns stable user IDs, memberships and current authorization.

Register direct test clients and MCP clients separately. Identify them from trusted token `client_id`/`azp`, not a self-declared header. Direct API calls also need scopes:

| Operation | Required scope |
| --- | --- |
| Read own identity status | `books:identity` |
| Create organization | `books:org-create` |
| Organization reads, writes, administration | `books:read`, `books:write`, `books:org-admin`, respectively |
| Create/revoke delegations | `books:delegations-manage`, and an approved direct client |

Scopes never replace business roles. Use `(issuer, subject)` as the identity key, not email. Phase 1 supports one approved issuer. Multiple-login linking and email migration need separate design. Never merge or take over historical accounts because email strings match.

Create the Books user after the first valid login. Unverified users can read only their own status, not create organizations, accept invitations or access organization data. Take verified identity details from a trusted issuer's validated token/userinfo. A request's `email_verified=true` proves nothing.

The direct test client uses Authorization Code + PKCE S256 and the provider's login page. No password or implicit grant. Register exact redirect URIs, validate `state`, and validate `nonce` for OIDC login. See [RFC 9700](https://www.rfc-editor.org/rfc/rfc9700.html). Books has no password-collecting `/login` or `/register` API.

### Token validation and revocation

Use `Authorization: Bearer` access tokens. Check allowed signing algorithm, issuer, audience, `exp`, `nbf`, subject and client identity on every request. Obtain keys from the configured issuer's metadata/JWKS, not arbitrary token-supplied key URLs. Cache public keys; rate-limit refresh on an unknown `kid`. Fail closed. Never put bearer tokens in query strings, logs or audit.

An ID token targets the login client; it cannot access the Books API. Access tokens must target the configured Books resource. Deployment URIs remain undecided; do not invent domains. Proposed access-token lifetime: five minutes. The provider handles refresh and logout.

Short token life does not replace immediate business revocation. Every request checks current membership, grants and delegation. Books also needs a local disabled-user check. If revocation happens only at the provider and is not synchronized locally, an issued JWT may work until expiry. Record this limitation in acceptance evidence. Do not claim all logout paths revoke access instantly.

### Last-administrator security disable and recovery

Account disable is not a public phase-1 management endpoint. A future local disable transaction must lock the user and all their organizations in a fixed order. Normal disable requires handover first. Emergency security disable must not be blocked merely because the account is the last administrator.

Proposed exception: if security disable removes the last usable administrator, set the organization to `locked_recovery`. Deny normal business reads and writes. Recovery must be audited and separately reviewed. Do not silently leave an active organization without a usable administrator or automatically promote a member. This proposal adds neither an approved role nor permission for automatic recovery.

External provider disable/deletion is outside Books database constraints. Without verified disable-event synchronization and a manual recovery process, the database can guarantee an active administrator record, not that its owner can still log in. Disable synchronization, recovery and recovery identity checks are launch blockers until approved and tested. Document lost/delayed events and provider-outage behavior.

No browser cookie session is needed now. Design BFF/cookies/CSRF if a Web UI is added. Do not mix unapproved authentication methods into the bearer API. Require production TLS and explicit CORS/Origin allowlists, not wildcard credential origins.

Keycloak exposes discovery, token, key, userinfo, introspection and revocation endpoints: [official OIDC documentation](https://www.keycloak.org/securing-apps/oidc-layers). Their existence does not prove this project's MCP compatibility.

## 5. Data model and database constraints

This is a specification, not executable migration code.

- IDs are server-generated UUIDs.
- Every entity referenced by a tenant composite foreign key has `UNIQUE(org_id,id)`, including invitations and delegations.
- Timestamps use `timestamptz`.
- Mutable aggregates use positive `bigint version`, initially `1`. APIs return decimal strings such as `"1"`.
- Trim names; allow 1–200 Unicode characters.
- Updates cannot change ownership fields such as `org_id`, `company_id` or `user_id`.
- Physical deletion of organizations, companies, ledgers and users is outside the phase-1 API.

| Table | Main columns | Required constraints |
| --- | --- | --- |
| users | id, status(active/disabled), display_name, created_at, version | No passwords. Disabled users fail at every entry point. |
| identities | id, user_id, issuer, subject, verified_email, email_verified_at | `UNIQUE(issuer,subject)`; user FK. Email is not the identity key. |
| organizations | id, name, state(active/locked_recovery), created_by, created_at, version | Active organizations need an active, locally enabled administrator. `locked_recovery` blocks normal business access; recovery remains proposed. The organization row is also the authorization transaction lock. |
| memberships | id, org_id, user_id, role(admin/member), status(active/removed), generation, version, joined_at, removed_at | `UNIQUE(org_id,user_id)`, `UNIQUE(org_id,id)`; positive generation increments on each rejoin. |
| companies | id, org_id, name, created_at, version | `UNIQUE(org_id,id)`; organization FK. |
| ledgers | id, org_id, company_id, name, created_at, version | `UNIQUE(company_id)`, `UNIQUE(org_id,company_id)`, `UNIQUE(org_id,id)`; composite FK `(org_id,company_id)` to companies. |
| ledger_grants | org_id, membership_id, ledger_id, membership_generation, access(read/edit), granted_by, created_at | PK `(membership_id,ledger_id)`; composite FKs `(org_id,membership_id)` and `(org_id,ledger_id)`; grant matches the current active member/generation. |
| invitations | id, org_id, target_email_key, inviter_membership_id, proposed_role, status, expires_at, accepted_by, accepted_at, created_at, version | Inviter composite FK to same-organization membership. At most one `pending` row per organization/target. Expiry is creation + 168 hours. States: pending, accepted, revoked, expired, invalidated. |
| invitation_grants | org_id, invitation_id, ledger_id, access | Composite FKs `(org_id,invitation_id)` and `(org_id,ledger_id)`; PK `(invitation_id,ledger_id)`; administrator invitations have no ledger grants. |
| audit_events | id, org_id(nullable for user actions), actor_user_id, actor_client_id, delegation_id, action, target_type/id, safe_change, request_id, occurred_at | Append-only. Runtime cannot update/delete. Commit with the corresponding change. |
| idempotency_records | actor_user_id, client_id, scope_key, operation, key, request_hash, outcome_resource_ids, status, created_at, expires_at | Unique scope combination. No bearer/invitation secrets in results. Replay is not guaranteed after expiry. |
| delegations | id, user_id, client_id, org_id, resource_mode, scopes, status, expires_at, version | One active delegation per user/client/organization. Role changes do not expand it. No shared user identity. |
| delegation_ledgers | delegation_id, org_id, ledger_id, access_cap | Same-organization composite FKs. Caps limit access; they do not grant business permission. |

### Exactly one ledger per company

A unique `ledger.company_id` only gives *at most* one. Add a reverse FK from company `(org_id,id)` to ledger's unique `(org_id,company_id)`. Make both directions `DEFERRABLE INITIALLY DEFERRED`. Insert company and ledger together; commit requires the complete pair. A company-only commit fails. No cascading delete in either direction. Any future deletion needs a history-protection design.

An ordinary CHECK cannot enforce cross-row administrator counts or look up other tables. Use composite FKs, uniqueness and transaction locks. Add a deferred constraint trigger as the final last-administrator guard. The trigger must lock the organization before checking current state, not count an old snapshot. See [PostgreSQL constraints](https://www.postgresql.org/docs/18/ddl-constraints.html).

### Invitation identity and effective grants

Invitation targets use verified email from the approved provider. Define one `email_key` policy: trim, normalize/lowercase the domain, and normalize the local part only according to the provider's verified uniqueness rules. Never invent plus/dot stripping. The authentication test must establish the same policy for creation and acceptance. Reject ambiguous matches rather than guess. Email changes do not transfer memberships. Re-read the trusted current verified email at acceptance.

A grant works only when membership is active, role is `member`, and grant generation matches membership generation. Delete current grants on promotion. Delete old grants on downgrade/removal. Rejoining increments generation and inserts only the newly approved grants. Audit preserves grant history; old live rows do not restore it.

Minimum indexes:

- `memberships(user_id,status,org_id)`
- Organization resources: `(org_id,created_at,id)`
- `ledger_grants(org_id,ledger_id)`
- `invitations(target_email_key,status,expires_at)`
- `invitations(org_id,inviter_membership_id,status)`
- `audit_events(org_id,occurred_at,id)`

Do not use `now()` in a partial unique index. When creating an invitation, lock the organization, mark elapsed pending invitations for that target `expired`, then rely on the `status=pending` partial unique index.

## 6. Transaction and lock order

Proposed phase-1 strategy: PostgreSQL `READ COMMITTED` plus organization row locks. Correctness comes before throughput.

For an organization write, acquire `SELECT organization FOR UPDATE` before reading current actor membership, target and access. Writes within one organization serialize. Every organization service checks `state=active`. `locked_recovery` returns `ORG_RECOVERY_REQUIRED` for normal business operations; only a separately reviewed recovery path may remain available.

Security-sensitive reads use organization `FOR SHARE`. Authorize and fully materialize a bounded response inside the transaction. Multi-organization lists return only visible metadata and do not perform bulk writes. Sort any cross-organization locks by UUID. Measure before reducing lock granularity. See [PostgreSQL locks](https://www.postgresql.org/docs/18/explicit-locking.html).

All entry points use this order:

1. Current user's status lock (`FOR SHARE`; disable needs an exclusive lock).
2. Organization.
3. Delegation.
4. Membership, invitation or ledger.
5. Idempotency record.

Sort multiple rows of the same kind by ID. Delegation revocation locks its organization before the delegation. MCP checks and locks the delegation after the organization too. Never use opposite lock orders.

Commit business changes, audit and successful idempotency results together. A crash or audit failure rolls them all back. Do external identity calls outside database transactions. Do not wait for email or remote services while holding locks.

| Command | Transaction rule |
| --- | --- |
| Create organization | Serialize the user's matching idempotency key. Insert organization and first administrator together. An organization without an administrator cannot commit. |
| Create company | Under the organization lock, verify current administrator access and insert company plus its only ledger. |
| Remove/downgrade | Count administrators and change state under the organization lock. A normal change leaving zero returns `409 LAST_ADMIN`, including self-change. Invalidate the member's pending invitations; removal also invalidates old pending invitations addressed to the target. Update grants, delegations and audit consistently. |
| Reduce permissions | Conservatively revoke all that user's active delegations in that organization. The user must explicitly create new ones. Later promotion must not revive old AI access. |
| Accept invitation | Lock organization and invitation. Check state, exact expiry, current email, inviter still active administrator, ledger scope and existing membership. Atomically create/restore membership, write fresh grants, consume invitation and audit. |
| Accept/revoke race | Acceptance first: later revoke returns `409 INVITE_ALREADY_ACCEPTED`, not member removal. Revoke/expire/invalidate first: acceptance returns `410 INVITE_UNAVAILABLE` only to an authorized viewer. |

A write that acquires the lock and commits before revocation is a pre-revocation action. If revocation commits first, a later lock holder must reject the write using new permissions. An earlier out-of-transaction check cannot authorize an unprotected commit.

Already materialized or sent reads cannot be recalled. Phase 1 has no exports, long jobs or continuous streams. Define chunk reauthorization and cancellation before adding them; do not claim interruption capabilities that do not exist.

Proposed limits: `lock_timeout=3 seconds`, command `statement_timeout=10 seconds`. Return a retryable failure on timeout, never success. Deadlock/serialization failures may automatically retry the complete transaction at most twice, with the same idempotency key. Do not retry external side effects with uncertain outcomes.

## 7. Common HTTP contract

Prefix: `/api/v1`. Requests are JSON. Reject unknown fields, duplicate JSON keys and invalid UUIDs. Proposed body limit: 256 KiB. Proposed grants limit: 100. No arbitrary permission fields or unrestricted JSON extension fields.

Omitted fields mean unchanged. Explicit `null` is accepted only for fields declared nullable. `[]` means clear the list. `name` is not nullable; phase-1 PATCH accepts only `name`.

Single-object success: `{"data":{...},"request_id":"..."}`.

List success: `{"data":[...],"page":{"next_cursor":null},"request_id":"..."}`. No default total count. An empty list is not an error.

Versions are strings such as `"3"`. Return strong ETags such as `"v3"`. PATCH, PUT and state-changing actions require `If-Match`: missing gives `428 PRECONDITION_REQUIRED`; mismatch gives `412 VERSION_CONFLICT`. Do not duplicate this as an HTTP JSON `expected_version`. MCP's `expected_version` maps to the same condition.

Timestamps use RFC3339/ISO 8601 UTC with `Z`. Seven days means 168 hours. `now >= expires_at` is expired. No accounting dates or amounts appear in phase 1. Future amounts need separately reviewed decimal-string, currency and precision rules, not floating-point defaults.

Errors use `application/problem+json`: fixed documented `type`, `title`, `status`, `code`, safe `detail`, `instance/request_id`, and optional `field_errors`. Never return SQL, stacks, tokens or another organization's name. Clients use `code`, not the wording of `detail`.

| HTTP | Codes | Meaning |
| --- | --- | --- |
| 400 | INVALID_ARGUMENT / INVALID_CURSOR | Invalid input or mismatched cursor. |
| 401 | UNAUTHENTICATED | Missing, expired or invalid credentials; appropriate WWW-Authenticate. |
| 403 | EMAIL_NOT_VERIFIED / FORBIDDEN / INSUFFICIENT_SCOPE / ORG_RECOVERY_REQUIRED | Authenticated but missing a safely disclosable qualification or permission. |
| 404 | NOT_FOUND | Absent or invisible organization, ledger or invitation. Do not distinguish cross-tenant existence. |
| 409 | LAST_ADMIN / ALREADY_MEMBER / INVITE_CONFLICT / IDEMPOTENCY_KEY_REUSED / INVITE_ALREADY_ACCEPTED | Business conflict; do not overwrite. |
| 410 | INVITE_UNAVAILABLE | Reveal expiry/revocation/invalidation only after verifying the viewer is the recipient or organization administrator. |
| 412 / 428 | VERSION_CONFLICT / PRECONDITION_REQUIRED | Prevent lost updates. |
| 429 | RATE_LIMITED | Retry-After. Proposed per-user/client limits and IP limits for anonymous authentication entry points. |
| 503 | TEMPORARILY_UNAVAILABLE | Database/validation dependency unavailable or lock timeout. Never disguise this as an empty list. |

Invisible target: 404 first. Visible target with a forbidden operation: 403. Add error codes without changing existing meanings. Breaking fields/semantics require `/v2` and a new MCP tool version. Old clients may ignore new optional response fields. Define old-client behavior before extending enums.

### Idempotency

Every business write requires `Idempotency-Key` as a UUID, including accept, revoke, leave and permission changes.

Scope: `user_id + client_id + organization-or-global + operation + key`. Fingerprint the normalized body, path target and `If-Match` together. Same key/different fingerprint: 409. Same key/same request: replay the same logical result. Concurrent identical keys wait for the first transaction, then read its result. A wait timeout gives 503; retry with the original key.

Proposed retention: seven days; return `Idempotency-Expires-At`. After expiry, do not assume deduplication. Creation clients should query for the result first. For an uncertain outcome, keep the original key.

Cache only committed successes. Validation failure does not consume the key; failed transactions do not record success. Before replay, check current identity, delegation and target access. A cache cannot bypass revocation.

For the original user's own leave/self-downgrade, the same actor may replay only a safe receipt: ID, completion status and time. Do not return old organization content. Other invisible targets return 404. If the resource later changes, replay the original receipt/resource IDs, not a claim of current details; use a separate GET for current state.

### Pagination and filters

`limit` defaults to 50 and accepts 1–100. Sort by `created_at ASC,id ASC`; audit uses `occurred_at,id`. `next_cursor` is opaque, signed and bound to actor, organization, filters, ordering, last key and first-page cutoff. Proposed cursor lifetime: 15 minutes. A cursor for A cannot query B.

Capture the cutoff on page one. Exclude later-created records on subsequent keyset pages. Recheck current access on every page. Deletion, revocation and edits can change results; this is not a complete cross-page snapshot and does not promise zero omissions/duplicates.

Supported filters: case-insensitive `name_prefix` for organizations/companies/ledgers; `role` and `status` for administrator membership lists; `status` for invitations. Combine filters with AND. Unknown filters/sorts return 400. No arbitrary SQL sort fields. Authorize before pagination. Ordinary members can inspect their own membership only; complete member/invitation lists require administrator access.

## 8. Endpoints and minimum payloads

All paths below follow `/api/v1`. `{org}` means `org_id`; `{member}` means `membership_id`. Validate every path target's ownership first. Entity responses include `id`, relevant `org_id`, `version` and `created_at`. Do not expose provider subject IDs in user lists. Minimize displayed member emails. No authentication secrets in audit.

| Method and path | Input | Access and success |
| --- | --- | --- |
| GET /me | None | Valid login; own id, display_name, verified, status. No tokens. |
| GET /me/organizations | Pagination | Current active memberships; organization summary and own role. Agent clients additionally require a valid delegation per returned organization. |
| POST /organizations | name | Verified user; 201 organization + first membership. No existing organization required. |
| GET /organizations/{org} | None | Active member; summary and own role. |
| PATCH /organizations/{org} | name | Administrator; 200 current organization; If-Match. |
| GET /organizations/{org}/companies | name_prefix, pagination | Administrator: all. Member: companies whose ledger has a current grant. |
| POST /organizations/{org}/companies | name, ledger_name | Administrator; 201 company + only ledger. Both names required. |
| GET /organizations/{org}/companies/{company} | None | Administrator or member with access to its ledger. |
| PATCH /organizations/{org}/companies/{company} | name | Administrator; 200. Ledger edit does not grant company-identity management. |
| GET /organizations/{org}/ledgers | name_prefix, pagination | Filter by current role/grants; include effective_access and accounting_setup_status=not_configured. |
| GET /organizations/{org}/ledgers/{ledger} | None | Administrator or current grant; summary and effective_access. |
| PATCH /organizations/{org}/ledgers/{ledger} | name | Administrator; foundation configuration only, no accounting settings. |
| GET /organizations/{org}/memberships | role, status, pagination | Administrator; list. |
| GET /organizations/{org}/memberships/me | None | Active member; own role and current grants. |
| PUT /organizations/{org}/memberships/{member}/access | role, grants:[{ledger_id,access}] | Administrator; replace the full access set. For admin, grants must be []. For member, grants is required; [] explicitly means no ledgers. |
| POST /organizations/{org}/memberships/{member}/remove | {} | Administrator; safe receipt. Cannot remove last administrator. |
| POST /organizations/{org}/memberships/me/leave | {} | Self; safe receipt. Last administrator gets 409. |
| GET /organizations/{org}/invitations | status, pagination | Administrator; list without secrets. |
| POST /organizations/{org}/invitations | email, role, grants | Administrator; 201 summary. Active-member target: 409. Identical valid pending invitation: 200 existing invitation, no expiry extension. Different content: 409; revoke then reissue. |
| POST /organizations/{org}/invitations/{invite}/revoke | {} | Administrator; 200 receipt. Already accepted: 409. |
| GET /me/invitations | Pagination | Verified user; pending invitations matching their current verified email, with organization display name and proposed scope. |
| POST /me/invitations/{invite}/accept | {} | Matching verified recipient actively accepts; 200 membership/grants. Same recipient repeating a consumed acceptance gets a safe result, never a role upgrade. |
| GET /organizations/{org}/audit-events | Pagination; fixed allowed filters | Administrator; read-only audit. |
| GET /me/delegations | Pagination | Self through a direct login client; delegation summaries. |
| POST /me/delegations | client_id, org_id, resource_mode, scopes, ledger_caps, expires_at | Self through a direct login client; 201. Cannot exceed current user access. This is not a member granting access to another person. |
| POST /me/delegations/{id}/revoke | {} | Self through an approved direct client; If-Match; 200 receipt. |

Invitation inbox items must expose `invitation.version` and its corresponding ETag. Accept uses that invitation version in `If-Match`, not the membership version.

There is no separate POST ledger, second ledger per company, bulk import, organization delete, company transfer or arbitrary SQL endpoint. Every change returns its new version or a safe receipt.

### Invitation delivery

Phase 1 proposes an authenticated invitation inbox. The invitation ID is not a bearer credential. Knowing it is insufficient: the recipient must match the verified identity and explicitly POST accept. GET is never acceptance, and no background process accepts on the user's behalf.

This replaces the older optional secret-link idea, not the confirmed invitation policy. Notification email is out of phase-1 scope. Do not claim delivery. Future email needs an outbox, retries and delivery state; do not send it directly within a business transaction.

Synthetic company request: `{"name":"Example Company","ledger_name":"Example Ledger"}`.

Illustrative 201 response: `{"data":{"company":{"id":"<uuid>","org_id":"<uuid>","name":"Example Company","version":"1"},"ledger":{"id":"<uuid>","company_id":"<uuid>","accounting_setup_status":"not_configured","version":"1"}},"request_id":"<request-id>"}`. This shortened example illustrates nesting, not the full schema. Angle-bracket placeholders are not valid UUIDs to submit; the complete response also follows the common entity fields above.

Administrator handover uses two separately audited commands: promote another active member with `grants=[]`, confirm success, then leave or downgrade yourself. No owner role or permission-bypassing handover endpoint is needed.

## 9. MCP: limited human delegation

The design targets protocol version `2025-11-25`. Verify target-client support before implementation; this is not a claim that it is always the latest version. See [MCP authorization](https://modelcontextprotocol.io/specification/2025-11-25/basic/authorization).

### Authentication and identity binding

- `/mcp` is a protected resource. Publish RFC 9728 protected-resource metadata. Its 401 response includes valid `WWW-Authenticate` discovery information with `resource_metadata`. Discover the authorization server through OIDC/OAuth metadata.
- Use Authorization Code + PKCE. Phase 1 permits approved, preregistered clients only, not unrestricted dynamic registration.
- The client requests the Books MCP resource/audience; the backend validates the exact audience. Test provider behavior for the `resource` parameter. Setting a static `aud` does not prove full compatibility. See [RFC 8707](https://www.rfc-editor.org/rfc/rfc8707.html) and [RFC 9728](https://www.rfc-editor.org/rfc/rfc9728.html).
- Every request identifies the real `user_id` and trusted `client_id`. Tool arguments cannot set `actor_user_id`.
- Client classification, scopes and delegation checks belong to shared Principal/application authorization, not a URL test for `/mcp`.
- An MCP client that gets a valid API-audience token and calls HTTP still needs the same delegation intersection. Switching entry points cannot bypass it.
- HTTP accepts only the API audience. MCP accepts only the MCP audience. Reject the wrong audience before business processing. A correct audience does not make an agent client a direct human client.
- No `client_credentials` acting as a human, shared administrator key or token passthrough to services with other audiences.

For each organization request, load the current active delegation bound to **user + client + organization**. Effective access is:

`token scopes ∩ current user organization/ledger access ∩ delegation scope`

All three must allow the action. Delegation records do not issue tokens and are not a custom OAuth server.

### Establishing and ending delegation

The person explicitly creates a delegation through an approved direct test client. Agent/MCP clients cannot create or expand delegations, including through HTTP. The target client must be approved. Require explicit `expires_at`; proposed maximum lifetime is 30 days. Renewal needs a new explicit approval, not automatic extension.

The person can revoke through the direct client without administrator cooperation. The next request fails immediately; do not wait for token expiry. Downgrade, removal or a related grant reduction conservatively revokes every delegation for that user/organization. Rejoining or promotion does not restore them. A disabled account is denied everywhere.

Proposed scopes: `books:read`, `books:write`, `books:org-admin`. Only administrators can delegate `books:org-admin`, and only for the named organization. Members can delegate read/edit within specified ledger caps, never organization administration.

Require one explicit `resource_mode`:

| Mode | Required limits |
| --- | --- |
| selected_ledgers | Only books:read/books:write; ledger_caps must be nonempty. Each cap is read/edit. No default all-ledger scope. |
| organization_admin | Current user must be administrator; explicit books:org-admin; ledger_caps=[]. This covers the whole named organization, including future ledgers. It is not ledger-limited. |

Reject `org-admin` plus nonempty ledger caps with 400. Caps cannot honestly restrict a power that can invite administrators or grant other ledgers. The person must explicitly accept whole-organization administration. This design is not a current grant of real AI access. Ordinary members can never create an `organization_admin` delegation.

Phase-1 MCP does not expose registration, delegation creation, invitation acceptance or organization creation. Those identity/access-establishing actions remain in the direct API test client.

| Operation | selected_ledgers | organization_admin |
| --- | --- | --- |
| Organization summary | Minimal organization name and own membership information only. | Named organization summary. |
| Company/ledger read | Only capped ledgers and their companies, intersected with current user access. | Whole named organization; still requires books:read. |
| Ledger business write, later phase | edit cap + current user edit + books:write. | Named organization; still requires books:write and business checks. |
| Company/ledger creation or foundation configuration | Denied; not ledger business edit. | Explicit org-admin; named organization only. |
| Members, invitations, grants, audit | Denied, even for data related to a selected ledger. | Explicit org-admin; whole named organization. |
| Create/expand delegation | Denied through agent clients. | Denied through agent clients. |

### Adapter and tool contract

In the shared process, the MCP adapter passes the verified Principal, arguments, idempotency key and expected version to common application services. No loopback HTTP or special high-privilege token. HTTP and MCP reuse the same backend capabilities, not copies of their rules. If split into processes later, redesign audience-bound delegation/token exchange; do not reuse wrong-audience tokens.

Proposed tool names:

- books_list_organizations
- books_get_organization
- books_list_companies
- books_create_company
- books_list_ledgers
- books_get_ledger
- books_list_memberships
- books_set_member_access
- books_remove_member
- books_list_invitations
- books_create_invitation
- books_revoke_invitation
- books_list_audit_events

Write tools require `idempotency_key` and `expected_version`; creation does not need the latter. No generic arbitrary-request tool. Organization lists show only organizations with a valid delegation, not the person's other memberships.

Every tool needs strict input/output schemas. Successful `structuredContent` reuses HTTP DTOs. Business failures use `isError=true` and stable `code`/`request_id`, never false success. Invalid/expired tokens produce transport 401. Insufficient scope uses the protocol's 403 challenge. Business-resource denial uses structured errors. Tool descriptions and annotations do not confer permission. Do not keep long-lived background permission snapshots.

Compatibility testing must prove: metadata discovery; requested resource; login with PKCE; correct audience and client identity; successful tool call; wrong-audience rejection; immediate failure after delegation revocation; refresh cannot expand scopes; unknown-client rejection. Until this evidence exists, do not claim MCP connectivity works.

## 10. Audit, security and operations

Successful-change audit records include human actor, client, delegation if any, organization, action, resource IDs, allowlisted before/after values, UTC time and request ID. Keep member emails only where restricted business data needs them; prefer internal IDs in audit. Never log tokens, passwords, one-time codes or full invitation credentials. Failed access goes into security logs, not successful business audit. Limit abnormal request sizes and rates.

Initial tenant isolation relies on explicit `org_id` conditions, service checks, composite FKs and tests. The runtime database account has no DDL or superuser rights. Use a separate migration account. Runtime cannot update/delete audit tables; no administrator audit-edit API exists.

Do not claim RLS is enabled. A later RLS design must test pooled-connection context isolation and bypass rights. An unimplemented guard is not delivered protection.

Separate development, test and production is proposed; no environments are created now. Before launch, specify backup retention, recovery-time objectives, database/identity-provider restore drills, TLS/domain, secret storage/rotation, alerts and log retention. Use approved secret storage only, never Git. No credential templates are included. Migrations are a separate release step. A backup does not guarantee application rollback. Destructive changes are outside phase 1.

## 11. Implementation milestones, after coding approval

| Stage | Work | Required evidence |
| --- | --- | --- |
| M0 Authentication and contracts | Lock compatible .NET/EF/Npgsql/MCP SDK versions; isolated provider/client test; OpenAPI and MCP schemas; no real business data. | Full authentication path and negative cases pass. Revise failed assumptions before proceeding. |
| M1 Data and access foundation | EF models, reviewed versioned migrations, exact one-to-one company/ledger, service authorization, organization locks, audit, idempotency. | Empty-database migration; direct SQL cannot create cross-organization references or an orphan company; concurrent last-admin protection. |
| M2 Organizations and companies | Limited me/organization/company/ledger ASP.NET Core endpoints; filtered pagination. | Atomic creation, organization isolation, authorized companies only, no accounting defaults. |
| M3 Invitations and members | Inbox, acceptance, revocation, removal, full role/grant replacement. | Seven-day boundaries, mismatched identity, inviter downgrade, rejoin, duplicates and races. |
| M4 MCP delegation | Official C# SDK adapter, limited tools, scopes, direct-client delegation create/revoke, errors. | Same HTTP/MCP authorization matrix; no privileged credential; token and business revocation tests. |
| M5 Acceptance and release proposal | Security tests, load baseline, dependency scan, restore-drill plan, runbook. | Report passed/failed/not-run results. User reviews release separately; no automatic deployment. |

### Minimum automated tests

1. Replace every path/reference/cursor across organizations A/B. Invisible targets return 404 without leaks. Composite FKs reject cross-organization grants.
2. Same user is administrator in A and read-only in B. Read-only cannot write; edit cannot manage members, grants or organization/company configuration.
3. A verified user with no organization can create one; an unverified user cannot. Failure injection leaves no orphan creator/organization records.
4. Either company or ledger insert failing rolls back both. Direct company-only commit and a second ledger fail.
5. Use separate real database connections and barriers for two administrators leaving/removing/downgrading concurrently. At least one remains after normal changes.
6. Pause a request after access evaluation, revoke in another transaction, then resume. Assert outcomes from lock/commit order, not sleep-based timing guesses.
7. Test invitation immediately before, exactly at and after expiry; accept/revoke races; inviter losing and regaining administrator access must not revive old invitations.
8. Existing-member acceptance cannot upgrade. Removed members cannot use old invitations. New invitation grants are the only rejoin access. Downgrade with grants=[] gives no ledger access.
9. Concurrent same-key/same-body writes create one change and audit event. Different body gives 409. Cover lost connection after commit, safe retry after revocation and expired-key boundaries.
10. Two clients writing the same version: only one succeeds. 428/412 have stable codes. Audit failure rolls back business state.
11. Authorization precedes pagination. Test equal timestamps, secondary keys and changed filter/organization/actor cursors. Revocation blocks the next page.
12. JWT wrong issuer/audience/algorithm, expired/future nbf, ID-token substitution, unknown-kid refresh limits and no automatic email-based identity merge.
13. MCP cross-organization/ledger/scope requests fail. Agent clients cannot bypass delegation through HTTP or API-audience tokens, or create/expand delegation. Members never get org-admin. Reject org-admin with nonempty caps. Selected-ledger delegation cannot read full member/audit lists, invite administrators or grant other ledgers. Organization-admin delegation is explicitly organization-wide, never cross-organization. Forged client_id fails. Self-revocation and removal take effect immediately.
14. Emergency disable of the last usable administrator is not blocked by the count guard. Proposed recovery lock prevents normal access; no automatic promotion. Document missing/delayed provider-disable events and provider outages. No launch without an approved recovery design.
15. Errors/logs contain no authentication secrets, SQL or cross-tenant names. Runtime cannot update/delete audit.

Acceptance evidence must include final commit, dependency locks, migration results, OpenAPI/schema validation, unit tests, real-PostgreSQL concurrency tests, HTTP/MCP integration results and the untested list.

**None of these implementation tests has run. This is a design, not a passing test report.**

## 12. Review and next step

Review the exact C# dependency combination and identity-service operating cost; the foundation-only scope; authenticated inbox without email; conservative organization locks; HTTP fields/errors; and the explicit human delegation boundary.

After review, update the decision record and obtain a clear instruction to code. Accounting setup, accounts and posting remain separate later designs. This document does not approve them. No accounts, credentials, real invitations, migrations or deployments were created, and no real system permissions were changed.

[Back to documentation](../README.md)
