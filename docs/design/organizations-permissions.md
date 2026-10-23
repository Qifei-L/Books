# Organizations and permissions

Current review: Planning and business workflows first. This technical document is retained as a draft. Further technical expansion is paused; no implementation is authorized.

Version: 0.3 | Date: 2026-10-23 | Status: design proposal, not implemented

## Read this first

This document explains who can access each organization and ledger. The [technical contract](phase-1-technical-contract.md) gives the proposed database, API, authentication and MCP details.

**Confirmed** means the user has decided the business rule or technology direction. **Proposed** means the implementation detail still needs review. Neither label gives permission to start coding or deploy.

## Confirmed rules

- Any registered user with a verified identity can create an organization. The creator becomes its first administrator. Anonymous users cannot create one.
- One account can join several organizations. Permissions are separate in each organization.
- An organization manages a team and can contain several companies. Each company has exactly one separate ledger in phase 1.
- Administrators manage all ledgers, members and permissions in their own organization.
- Ordinary members can access only assigned ledgers, with `read` or `edit` access. They cannot give access to anyone else.
- The last administrator must hand over responsibility before leaving, being removed or becoming an ordinary member.
- An administrator invites a user. The user must actively accept. An invitation lasts seven days, can be revoked, and must be replaced after expiry.
- When an inviter loses administrator access, their pending invitations become invalid.
- Rejoining members and administrators changing to ordinary members need new, explicit ledger grants. Old permissions do not come back automatically.
- HTTP and MCP use the same backend authorization and business rules.

For example, a user can be an administrator in Organization A and a read-only member in Organization B. Their administrator role in A gives them no extra access in B.

The backend direction is **C# + ASP.NET Core + EF Core + PostgreSQL, without ABP in phase 1**. Exact versions, the identity provider, deployment and accounting engine remain open. This work changes documentation only. It creates no credentials, real invitations, permissions or deployment.

## Proposed data model

| Record | Meaning and rule |
| --- | --- |
| User | A stable internal identity mapped from a verified login. A user ID supplied by a client is not proof of identity. |
| Organization | The team workspace. An organization open for normal use must have an effective administrator. |
| Company | Belongs to one organization. A normal update cannot move it to another organization. |
| Ledger | Belongs to that company and organization. Phase 1 requires exactly one ledger per company, not merely at most one. |
| Membership | The user's role and active/removed status in one organization. The user/organization pair is unique. Its stable ID preserves history. |
| LedgerGrant | A member's read/edit access to a ledger. The member/ledger pair is unique and both must belong to the same organization. |
| Invitation | A proposed membership for a specific verified identity, with inviter, role, ledger scope, expiry and state. It is not an active membership. |
| AuditEvent | Who changed what, in which organization, when, and under which request. It never stores passwords, access tokens or secret invitation credentials. |

Use service checks and database constraints together. Composite foreign keys include `org_id` so a grant cannot connect an Organization A member to an Organization B ledger. An alternative database would need equivalent atomic constraints and tests, not just Frontend filtering.

Create a company and its ledger in one transaction. If either fails, neither becomes usable. Keep removed memberships for historical references, but invalidate their grants. Rejoining does not revive those grants.

## Check access on every operation

The shared request flow is:

1. Verify authentication and identify the user and client.
2. Identify the requested operation and resource.
3. Read current memberships and resource ownership from trusted server state.
4. Check the current role, ledger grants and any required client delegation.
5. Apply business rules.
6. Commit the change and its audit record together.
7. Return only authorized data.

Administrators still follow accounting validation and history-protection rules. `edit` allows ledger business changes, not member or permission management. Switching the selected organization changes context, not permissions. Organization-level operations do not need a ledger; creating an organization does not need an existing organization.

Do not reveal another organization's resource names, existence or accounting data in errors. A login session proves identity, not permanent permission. Read current permissions for each protected operation, or use a mechanism with equivalent strongly consistent revocation. After removal, downgrade or grant revocation commits, later requests use the new state.

For writes, the final access check and commit must share one ordered transaction boundary. If revocation commits first, the later write fails. If the write commits first, it is a completed pre-revocation action and remains in the audit trail.

Phase 1 has no exports, long-running jobs or continuous streams. Before adding them, define reauthorization before output and side effects, cancellation, and tested interruption boundaries. Already returned data cannot be recalled.

## Create an organization

Create the organization and its first administrator membership atomically. Failure must not leave a usable organization without an administrator. An idempotent retry returns the same creation result rather than creating another organization.

No extra admission approval, quota or commercial condition is added by this design. Company and ledger creation remains an administrator operation.

## Invite and accept

Proposed states are `pending`, `accepted`, `revoked`, `expired` and `invalidated`. Expiry follows the current time; it does not depend on a scheduled cleanup job running promptly.

The [technical contract](phase-1-technical-contract.md) proposes an authenticated invitation inbox for phase 1. An invitation ID is not a secret that grants access. The recipient must log in, match the invitation's verified identity and explicitly accept it. Reading an invitation, knowing an email address, opening a link or making an AI tool call does not join the organization.

Only a current administrator can invite. Every proposed ledger must belong to the same organization. Allow at most one valid pending invitation per organization and target identity:

- The same request returns the existing invitation without extending expiry.
- To change the role or grants, revoke the old invitation and issue a new one. The old invitation stays invalid.
- An expired invitation needs a new invitation.

Acceptance is one atomic operation. Check the current state, exact expiry, recipient identity, inviter's current administrator access, proposed role and ledger scope. Then create or restore the membership, apply only the new grants, and consume the invitation.

If the inviter lost administrator access, the invitation remains invalid even if they later become an administrator again. A current administrator must issue a new invitation.

If the target is already an active member, acceptance must not upgrade their role or replace their grants. Return the appropriate safe result or `ALREADY_MEMBER`; changes use member-management commands. A removed member needs an invitation issued after removal. Removal also invalidates that target's old pending invitations.

Concurrent acceptance and revocation must produce one valid outcome. Revocation or expiry first means acceptance fails. Acceptance first means the invitation is consumed; revoking it is not a way to remove the new member.

No notification email is included in the phase-1 proposal, and the API must not claim delivery. If a future design adds secret invitation links, credentials must be unguessable, single-use and time-limited, with only a digest stored and no secret in logs, audit or errors. That is a future option, not the current inbox mechanism.

## Change, remove or hand over access

Only administrators manage members. Ledger editors cannot call these commands.

To hand over administration:

1. Promote another active member of the same organization.
2. Confirm the promotion succeeded.
3. The original administrator may then leave or become an ordinary member.

Count administrators and apply the change in the same serialized transaction. Two administrators concurrently removing or downgrading each other must not both pass based on an earlier count of two.

When promoting a member, clear grants that might otherwise reappear on a later downgrade. A downgrade requires an explicit new grant list; an empty list means no ledger access. Remove membership access, grants and related pending invitations atomically with the audit record. Preserve memberships in other organizations and the original actor on historical records.

Security emergency exception: never keep a compromised account active merely to retain the last administrator. The detailed contract proposes `locked_recovery` when disabling the last usable administrator: block normal business access, do not automatically promote anyone, and require an audited recovery process. This recovery design and identity-provider disable synchronization must be reviewed and verified before launch.

## API and MCP boundary

| Operation | Required context |
| --- | --- |
| Create organization | Registered, verified user; no existing organization or ledger required. |
| List my organizations | Current identity; only currently authorized memberships, and only delegated organizations for an agent client. |
| Invite, revoke, manage members or grants | Current administrator; same-organization targets; expected version checks. |
| Accept invitation | Verified recipient actively accepting; current invitation and membership state. |
| Read or edit ledger resources | Current organization, ledger ownership and required access. |
| Leave organization | The member acting for themselves; last-administrator transaction protection. |

MCP is another adapter to the same application services. It must carry a verifiable human identity and approved client identity. It cannot use a shared administrator account or treat an AI's natural-language claim as authority.

The detailed contract requires a current delegation bound to user, client and organization. Effective access is the intersection of token scopes, current user access and the delegation's resource limits. Changing from an MCP URL to an HTTP URL cannot bypass this rule. API and MCP audiences are distinct; neither an ID token nor a token for the wrong resource is acceptable.

The identity provider is not yet selected. Managed application sessions and external identity services have different maintenance costs, but neither replaces Books' own membership and ledger checks. Keycloak is a candidate, not a confirmed provider. This document creates no persistent access.

## Retries, versions, queries and errors

A proposed idempotency scope includes the user, client, operation, organization or global scope, and request key. Store a request fingerprint and committed result. The same key with different content is a conflict. Replaying a result still requires current authorization; a cache must not disclose old sensitive content after revocation. The detailed contract defines retention and safe receipt exceptions.

Mutable objects have versions. A write supplies its expected version; stale writes fail rather than overwrite newer changes. Unique constraints help prevent duplicates but do not replace idempotency.

Use stable error codes for authentication, access, invalid input, expired/revoked invitations, version conflicts and last-administrator protection. Do not expose secrets or cross-organization details.

Filter lists by authorization before sorting or pagination. Use a unique secondary sort key. Counts, search suggestions and exports must obey the same access scope. Define cursor and concurrent-change behavior explicitly; do not promise a perfect snapshot unless it is implemented.

## Audit and verification

Commit each important organization, membership, grant or invitation change with its audit event. A failed change cannot have a successful audit event, and a successful change cannot lack one. Record actor, target, time, safe before/after values, request ID and outcome. Rejected access may have a separate security log, not a successful business event.

Only authorized viewers can read audit events. Ordinary editors cannot change audit history. Retention periods and compliance rules are not decided here. Notification failures must never be reported as successful delivery or create duplicate memberships.

The [acceptance matrix](../08-testing-acceptance.md) covers cross-organization references, read-only writes, MCP privilege escalation, old sessions after revocation, invitation failures and concurrent administrator handover. Test service rules, database constraints and HTTP/MCP integration, not just the Frontend.

**No implementation or acceptance tests have run.** Business decisions are confirmed; implementation details remain proposals. Review the [detailed technical contract](phase-1-technical-contract.md), then obtain separate approval to code. Do not run migrations or deploy as part of this documentation task.

[Back to documentation](../README.md)
