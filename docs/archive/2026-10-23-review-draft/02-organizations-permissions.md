# Organizations and permissions

Status: Requirements draft. The business rules marked Confirmed were agreed on 2026-10-23. Proposed details still need review. This document does not authorize implementation or deployment.

## Purpose

Explain who can join an organization, which ledgers they can use, and what happens when their access changes.

## Confirmed

### Organizations, companies, and ledgers

An organization is a team's management space. It contains members, permissions, and companies.

For the first release:

- One organization can contain several companies.
- Each company has one independent ledger.
- Accounts, journal entries, and balances belong to a specific ledger.

The organization itself does not keep accounting records or automatically combine its ledgers. Cross-ledger queries or combined reports need separate rules and access checks.

Example: An administrator of Organization A has no access to Organization B unless B separately grants access.

See [Companies and ledgers](03-companies-ledgers.md).

### Creating an organization

Any registered user with a verified identity can create an organization. The creator becomes its first administrator. Creation must not leave a usable organization without an administrator. The technical method for creating both together is still proposed.

### Membership in several organizations

A user is a login account. A membership is that user's relationship with one organization.

One account can join several organizations without registering again. Each organization has its own role and ledger permissions for that account.

Example: The same person can be an administrator in A and a read-only member of one ledger in B. A's permissions do not apply in B.

### The two roles

The first release has two organization roles: administrator and ordinary member.

An administrator can manage all ledgers, members, and permissions in their organization. Administrators do not need separate access grants for each ledger in that organization. Only administrators can invite members, manage members, grant access, or remove access.

An ordinary member can access only the ledgers assigned to them. Each assignment is either:

- Read-only: View accounting information in that ledger. Do not add or change accounting data.
- Editable: Enter or change data in that ledger, subject to its business rules.

Editable access does not allow a member to invite people, change roles, share ledger access, or grant permissions to someone else.

Example: A member can have read-only access to Ledger 1 and editable access to Ledger 2. They cannot edit Ledger 1 or grant another person access to Ledger 2.

Administrator access does not override accounting checks, history protection, or audit requirements. The detailed accounting rules will be defined in the relevant modules.

### Joining by invitation

1. An organization administrator invites a specific person.
2. That person signs in, verifies their identity, and actively accepts the invitation.
3. The system creates their membership and applies explicit permissions.

An invitation does not automatically add someone. Ordinary membership does not grant access to every ledger.

An invitation is valid for 7 days. It can be revoked before acceptance. An expired invitation requires a new invitation. An expired or revoked invitation cannot be accepted. A person whose identity does not match the invitation cannot accept it.

If the inviter loses administrator access, their unaccepted invitations become invalid. A current administrator must send new invitations.

### Leaving and returning

Removing someone from one organization does not remove their account or their access to other organizations.

A removed member needs a new invitation and new permissions to return. Old invitations and old ledger permissions must not restore access automatically.

When an administrator becomes an ordinary member, their ledger permissions must be assigned explicitly. They do not keep access to every ledger by default.

### Keeping an administrator

The last administrator must make another member an administrator before leaving. Removal or demotion must not leave the organization with no administrator either. This rule must also hold when requests happen at the same time.

This protection applies to normal membership and role changes. It must not block security suspension of a compromised account, even if that account is the last usable administrator. A locked organization and recovery process need a separate proposed workflow and release gate.

There is no separate owner role in this requirement. The responsible person is an organization administrator.

## Required access checks

The backend must check access for every query, change, search, and export. Hiding a button is not enough.

For an operation on a ledger, check:

1. The caller is still a member of the organization.
2. The target ledger belongs to that organization.
3. The caller can access that ledger and perform the requested action.

For an organization-level operation, check the relevant organization role. Knowing another organization's ID or ledger ID does not grant access. Changing an ID in a request must not bypass these checks.

When switching organizations, use the target organization's membership and permissions. Do not carry over the previous organization's role. API and MCP calls use the same checks.

The business boundary is established. How requests carry this context, and how sessions and caches enforce current permissions, remains technical design work.

## Proposed

These details describe how to make the rules reliable. They are not approval of a technical implementation.

### Duplicate requests and invitations

- Create the organization and its first administrator together. If either step fails, neither becomes usable. A retry should not create a second organization.
- Reuse an active invitation when the target and permissions are unchanged. If permissions change, revoke the old invitation and issue a replacement.
- Accept an invitation only once. Repeated acceptance must not create another membership.
- If the person is already a member, accepting an invitation must not upgrade their role or replace their current permissions. An administrator should make that change through member management.
- If acceptance and revocation happen together, return one clear outcome. Revoking an already accepted invitation does not remove the member. Use member management for removal.

### Current permissions and concurrent changes

After removal or revocation succeeds, existing sessions, retries, exports, and MCP calls should use current permissions. Old sessions or cached permissions should not keep access alive.

A write and a permission revocation need a clear order. If revocation completes first, a later write cannot use the old permission. If the write completes first, its history should show that it happened before revocation. Neither operation should leave a partial change.

### History and audit

Keep historical journal entries and their original operator information when someone leaves. Do not delete history or assign the person's earlier actions to someone else.

Record who changed access, when they changed it, the affected organization or ledger, and the permissions before and after the change. A failed change must not appear as a successful one.

## Technical Design Tasks

These tasks belong to the later technical-design stage. Current review focuses on planning and business workflows; detailed expansion is paused.

Engineering owns these tasks. The business owner does not need to choose API fields, database constraints or retry mechanisms.

- Review the proposed API operations in [contract section 8](design/phase-1-technical-contract.md#8-endpoints-and-minimum-payloads). Frontend implementation is deferred. Planning user actions and their corresponding API business operations is in scope; exact API contracts belong to the later technical review.
- Validate identity verification, token/session handling and current-permission checks against [section 4](design/phase-1-technical-contract.md#4-identity-and-authentication). The provider is not selected and interoperability is not verified.
- Review the existing schema, constraints and transaction proposals in [sections 5 and 6](design/phase-1-technical-contract.md#5-data-model-and-database-constraints). The C#/EF Core/PostgreSQL direction is confirmed.
- Validate duplicate-request and invitation race handling against [section 6](design/phase-1-technical-contract.md#6-transaction-and-lock-order) and [section 7](design/phase-1-technical-contract.md#7-common-http-contract). These mechanisms are already proposed, not missing business decisions.

The confirmed roles, invitation rules, 7-day lifetime, explicit acceptance, revocation, reauthorization and administrator handover do not need another answer. Engineering may refine mechanisms without changing these rules. Any proposed change to user workflow or cost must be raised separately with the owner.

These are design-review tasks, not authorization to implement or run tests.

See [Testing and acceptance](08-testing-acceptance.md) for cases, [Organization and permission design](design/organizations-permissions.md) for technical proposals, and [Decisions](10-decisions.md) for the decision record. No tests have been run for this draft.

[Business workflow diagrams](11-workflows.md)
[Back to documentation](README.md)
