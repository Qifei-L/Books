# Business workflows

Status: Review draft. Confirmed rules are labelled below. Proposed workflows and technical mechanisms still need review. These diagrams do not authorize coding, migrations or deployment.

Current sequence: Planning -> Requirements & Workflow -> Technical Design -> Development & Testing. We are reviewing Requirements & Workflow. Existing technical documents remain drafts; detailed technical design is paused.

The diagrams show business steps, not final Frontend layouts or endpoint contracts. Frontend interaction planning and links to API business operations are in scope. Frontend implementation is deferred. API and MCP must use the same backend access rules.

## 1. Sign in, choose an organization, or create one

Confirmed: A registered user with a verified identity may explicitly create an organization. After successful creation, that user is its first administrator. Signing in is not organization creation.

### Enter Books

Proposed interaction flow: Check sign-in and identity verification separately, then show the organizations and invitations available to this user.

```mermaid
flowchart TD
    A["Open Books"] --> B{"Valid signed-in session?"}
    B -->|No| C{"Already have an account?"}
    C -->|Yes| D["Sign in to the existing account"]
    C -->|No| E["Sign up for a new account"]
    D --> B
    E --> B
    B -->|Yes| F{"Identity verified?"}
    F -->|No| G["Complete identity verification"]
    G --> F
    F -->|Yes| H["Show accessible organizations and invitations"]
    H --> M["Review a pending invitation using section 3"]
    H --> N["Choose Create organization using the separate flow below"]
    H --> I["Choose an existing organization"]
    I --> J{"Current membership allows access?"}
    J -->|No| K["Explain unavailable access and refresh the list"]
    J -->|Yes| L["Enter that organization with current permissions"]
```

- An expired or invalid session sends the person to sign in, not to create another account or organization.
- A pending invitation is not membership. The person must explicitly accept a valid invitation using the flow in section 3.
- If the list is empty, offer invitation review or the explicit Create organization action. Do not create an organization automatically.
- A person who already belongs to an organization can select it. They may also explicitly create a different organization under the confirmed creation policy.

### Explicitly create an organization

```mermaid
flowchart TD
    A["Frontend: user chooses Create organization"] --> B["Enter organization details and confirm creation"]
    B --> C["API business operation: create organization"]
    C --> D{"Current sign-in and verified identity valid?"}
    D -->|No| E["Return to sign-in or verification; do not create"]
    D -->|Yes| F["Create organization and first administrator together"]
    F --> G{"Creation succeeds?"}
    G -->|No| H["Show failure or check uncertain outcome; no blind duplicate request"]
    G -->|Yes| I["Return the new organization"]
    I --> J["Frontend: show or enter the new organization as administrator"]
```

Proposed mechanism: Organization and first-administrator creation succeed together, with no partial result or duplicate organization on retry. A timeout is not proof that creation failed. Company creation is a separate explicit action in section 2; it is not a side effect of sign-in.

### How Books recognizes existing records

Books recognizes the signed-in account, then finds that account's current organization memberships. Each existing organization has its own identifier. A matching organization or company name does not identify the account or prove membership.

The existing [identity proposal](design/phase-1-technical-contract.md#4-identity-and-authentication) maps the identity provider's stable issuer and subject to a Books user ID. Email or display-name matches must not merge accounts. This is a reference to the existing draft, not a new provider choice or verified integration.

See [Organizations and permissions](02-organizations-permissions.md) and [acceptance checks](08-testing-acceptance.md).

## 2. Create a company and its ledger

Confirmed: One organization can contain several companies. Each company has one independent ledger in the first release. Administrators manage all ledgers in their own organization, not in other organizations.

Proposed workflow: Only an administrator creates a company. Create the company and its one ledger together. Accounting settings are a later accounting-stage proposal, not a new phase-one API requirement.

Proposed steps: Check that the caller is a current administrator of this organization, create the company and ledger together, and return one clear result. A failure or retry must not leave a usable partial company or duplicate ledger. Complete agreed ledger settings before later accounting work.

Ordinary ledger edit access does not allow permission management. The proposed company-creation restriction is separate from that confirmed rule. An organization does not itself keep accounting records or automatically consolidate its companies.

See [Companies and ledgers](03-companies-ledgers.md).

## 3. Invite and join

Confirmed: Only an administrator can invite a specific person. That person must sign in, verify their identity and actively accept before becoming a member. Invitations expire after seven days and can be revoked before acceptance.

```mermaid
flowchart TD
    A["Administrator invites a specific person"] --> B["Invitation awaits acceptance for up to seven days"]
    B --> C["Invited person signs in and requests acceptance"]
    C --> D{"Verified identity matches the invitation?"}
    D -->|No| E["Reject acceptance and grant no membership"]
    D -->|Yes| F{"Invitation is still valid?"}
    F -->|No| G["Reject acceptance; current administrator must issue a new invitation"]
    F -->|Yes| H["Person actively accepts the invitation"]
    H --> I["Create membership and apply explicit permissions"]
```

- Expired or revoked invitations cannot be accepted.
- If the inviter loses administrator rights, their pending invitations become invalid. Restoring that role does not revive those invitations; a current administrator must invite again.
- Ordinary membership alone gives no ledger access. A user can belong to several organizations, with independent roles and permissions in each.
- A removed member needs a new invitation and explicit new grants. Old invitations and grants must not restore access.

Proposed mechanisms: Accept once, make concurrent acceptance and revocation produce one clear result, and prevent an invitation from silently upgrading an existing member. Revoking an already accepted invitation would not remove the member; member management would handle removal.

### Frontend and API interaction example

Proposed interaction planning, not an approved Frontend or endpoint design:

```mermaid
flowchart TD
    A["Frontend: administrator chooses person and permissions"] --> B["API business operation: invite organization member"]
    B --> C{"Current administrator and valid invitation scope?"}
    C -->|No| D["API result: invitation rejected"]
    D --> E["Frontend: explain the issue; no invitation created"]
    C -->|Yes| F["API result: invitation pending acceptance"]
    F --> G["Frontend: show pending status, not active membership"]
```

The invited person must still complete the separate acceptance workflow above.

See [Organizations and permissions](02-organizations-permissions.md).

## 4. Grant or remove ledger access

Confirmed: Only an administrator manages permissions. An ordinary member can hold read-only access to one ledger and editable access to another. Neither kind of access allows delegation.

```mermaid
flowchart TD
    A["Request a ledger grant change for an ordinary member"] --> B{"Caller is a current administrator of this organization?"}
    B -->|No| C["Reject the permission change"]
    B -->|Yes| D{"Target is an active member and ledger belongs to this organization?"}
    D -->|No| C
    D -->|Yes| E{"Requested change?"}
    E -->|Grant or replace| F["Assign explicit read-only or editable access"]
    E -->|Remove| G["Remove the ledger grant"]
    F --> H["Use current membership and ledger permissions for each operation"]
    G --> H
```

Administrators already manage every ledger in their own organization and do not need individual ledger grants. Read-only access never permits accounting writes. Editable access remains subject to accounting rules and never permits invitations, role changes or sharing access.

The backend must check membership, organization, ledger and permitted action for every query, write, search and export. API and MCP cannot bypass these checks. Session invalidation, cache behavior, ordering of concurrent writes and revocations, and audit storage are proposed technical mechanisms.

See [Organizations and permissions](02-organizations-permissions.md).

## 5. Leave, remove a member or demote an administrator

Confirmed: Normal membership or role changes must not leave the organization without an administrator. The last administrator must first appoint another active member as administrator.

```mermaid
flowchart TD
    A["Request leaving, member removal or administrator demotion"] --> B{"Caller is authorized for this membership change?"}
    B -->|No| C["Reject the change"]
    B -->|Yes| D{"Would the change remove the last administrator?"}
    D -->|Yes| E["Block the change until another active member is appointed administrator"]
    E --> F["Current administrator appoints another active member as administrator"]
    F --> D
    D -->|No| G["Apply the membership or role change"]
    G --> H{"Person remains as an ordinary member?"}
    H -->|Yes| I["Require explicit new ledger grants; do not restore old grants"]
    H -->|No| J["End this organization membership and access"]
    J --> K["Returning later requires a new invitation and new permissions"]
```

Only administrators manage other members or their roles. A member leaving one organization keeps their account and any independent access to other organizations. Losing administrator rights also invalidates that person's pending invitations.

The last-administrator rule must hold during concurrent changes. The enforcement mechanism remains proposed.

Security exception: Do not block urgent suspension of a compromised account, even if it is the last usable administrator. A locked-organization recovery process needs separate review before production. Do not silently promote another member. That exception is not the normal leaving workflow above.

See [Organizations and permissions](02-organizations-permissions.md) and [Decisions](10-decisions.md).

## 6. Prepare and post a journal

Proposed accounting workflow: Manual journals can be saved as drafts and posted only after validation. Editors and administrators would prepare and post them; a separate approval role is not assumed. The permission boundary is confirmed, but the posting roles, states and accounting controls still need approval.

```mermaid
flowchart TD
    A["Select ledger and request a journal draft"] --> B{"Current editable access or own-organization administrator?"}
    B -->|No| C["Reject the write"]
    B -->|Yes| D["Save or edit draft; no balance effect"]
    D --> E["Request posting"]
    E --> F{"Current access and all posting checks pass?"}
    F -->|No| G["Explain the blocker and leave no partial posting"]
    G --> D
    F -->|Yes| H["Post all lines and status together; assign posting reference"]
    H --> I["Include posted lines in ledger balances and reports"]
    I --> J["Keep posted journal unchanged; corrections use linked journals"]
```

Proposed posting checks include an active company, complete ledger settings, a current draft version, eligible same-ledger accounts, at least two non-zero lines, valid debit or credit amounts, exact balance at agreed precision, and a valid accounting date in an open period. A cancelled or already posted draft cannot post again.

A retry must not create a second posting. An uncertain response requires checking the saved outcome before starting a new request. A reversal would be a separate validated journal; it affects balances only when posted and does not erase the original.

This proposal does not add accounting endpoints to phase one or settle currency, precision, numbering, period closure or approval policy.

See [Journals and posting](05-journals-posting.md).

## 7. Read balances and reports

Confirmed: Reading and exporting require access to the specific ledger. Read-only members can read their assigned ledgers. Administrators have access only within their own organization. There is no automatic consolidated organization report.

Proposed accounting workflow: Reports use posted journal lines and one consistent reporting snapshot. Report selection, calculations and export formats still need approval.

```mermaid
flowchart TD
    A["Choose one ledger, report dates and filters"] --> B{"Current permission allows reading this ledger?"}
    B -->|No| C["Reject the request without exposing ledger data"]
    B -->|Yes| D["Read a consistent set of posted journal lines"]
    D --> E["Calculate opening balance, movements and closing balance"]
    E --> F["Reconcile report totals and transaction detail"]
    F --> G["Show ledger, currency, dates, filters and generation time"]
    G --> H{"Export requested and approved format available?"}
    H -->|No| I["Return the report"]
    H -->|Yes| J["Recheck access and export the same scope and totals"]
```

Drafts and cancelled drafts would not affect balances. Original entries and posted reversals would both count on their own accounting dates. Opening-balance journals would be included once. Pagination would not change totals; an export download would require current access.

Trial balance and account detail are proposed reconciliation references. Balance sheet and income statement layouts remain drafts until account classification, current-year profit and year-end rules are agreed. No statutory or tax compliance is claimed.

See [Balances and reports](06-reports.md).

## Review boundary

These are process illustrations, not implemented behavior or executed tests. Technical proposals remain subject to the design review. Accounting proposals remain subject to business approval. Frontend implementation is deferred. Interaction planning does not approve final Frontend layouts or technical contracts.

[Documentation index](README.md) · [Decisions](10-decisions.md)
