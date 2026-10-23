# Decisions and open questions

Updated: 2026-10-23  
Status: Review record. No coding or deployment is approved.

## Confirmed

### Project and delivery

- Build Books as a bookkeeping backend.
- Start with organizations and access control.
- Keep the Books Web interface out of the current scope.
- Complete and review the documents before development.
- Write all documents in plain, direct English.

### Organizations and ledgers

- An organization manages a team.
- One organization can manage several companies.
- Each company has one independent ledger in the first release.
- An organization does not post entries or automatically consolidate its companies.

### Roles and permissions

- Organization administrators manage all ledgers, members and grants in their own organization.
- An administrator role in one organization gives no rights in another.
- Ordinary members access only assigned ledgers. Each assignment is read-only or editable.
- Only administrators invite and manage members or grant and revoke access.
- Ordinary members cannot delegate access, including when they can edit a ledger.
- The last administrator must hand over before leaving. Appoint another active member as administrator first.
- Administrator rights do not bypass accounting validation, history protection or audit.

### Creation and membership

- Registered users with verified identity may create an organization and become its first administrator.
- One account may join several organizations. Roles and permissions are independent in each.
- An administrator invites a user. The user must accept before joining.
- Invitations last seven days, may be revoked, and must be reissued after expiry.
- When an inviter loses administrator rights, their pending invitations become invalid.
- Rejoining members and administrators downgraded to members need explicit new grants. Do not restore old rights automatically.

### API and MCP

- Clients share one backend. MCP is another entry point, not a privileged bypass.
- The backend verifies identity and the relevant organization and ledger scope. Client-supplied IDs do not prove permission.
- Requests, results and errors follow consistent rules. Money, dates and null values need clear meanings.
- Search, filters, sorting and pagination follow consistent rules.
- Retries must not create duplicates. Concurrent changes must not silently overwrite each other. Upgrades must not silently break existing callers.

### Technology direction

Use C#, ASP.NET Core, EF Core and PostgreSQL. API and MCP come first. Do not add ABP in phase one. A modular monolith is the proposed application structure, not an instruction to implement it.

This replaces the earlier Go proposal. It does not select exact package versions, an identity provider, hosting or production settings.

## Proposed, not approved

The accounting chapters now contain a complete starting proposal. They are not a statement of approved accounting policy.

The detailed technical contract proposes .NET/EF Core/Npgsql version families, Keycloak evaluation, authenticated invitation inboxes, transaction and lock rules, API fields, MCP delegation and development stages. Exact implementation details remain under review.

ASP.NET Core Identity is not, by itself, a complete OAuth/OIDC authorization server. Moving to C# does not authorize replacing the external identity-provider plan with homemade authentication.

No tests have been run. No production service, database, account, credential, invitation or permission has been created by this documentation work.

## Short business review list

The recommended defaults are proposals. Answer by topic; do not repeat the confirmed decisions above.

| Topic | What needs a decision |
| --- | --- |
| 1. Ledger setup | Base currency for each company, whether foreign-currency transactions are needed in the first accounting release, fiscal year and business time zone. |
| 2. Accounts | Flat accounts or a parent-child structure; custom code rules; whether duplicate names are acceptable; how account changes and deactivation should work. |
| 3. Posting and corrections | Opening balances, who can post, period closing/reopening and the correction process for posted entries. |
| 4. First reports | Which reports are required, date and balance presentation, and whether export is needed initially. |

Read the corresponding chapter for the proposed behavior and a concrete example. Once a choice is accepted, update that chapter and this record together.

## Technical and release review

These do not require immediate answers to finish the drafts:

- Review the detailed phase-one contract, identity provider and its operating cost.
- Agree on test scope, final acceptance owner and pass/fail evidence.
- Before production, choose hosting, budget, operators, recovery targets and release procedure.
- Give a separate instruction before coding, and a separate authorization before deployment.

Security suspension and recovery of the last usable administrator need a reviewed recovery process before production. The system must not prevent urgent suspension or silently promote someone else.

## Change record

All decisions above were recorded on 2026-10-23.

1. Created a requirements-only review branch.
2. Prioritized organizations and built the document outline.
3. Confirmed organization roles, company/ledger structure and API/MCP principles.
4. Confirmed organization creation, membership, invitation and reauthorization policies.
5. Added technical proposals and acceptance scenarios.
6. Replaced the Go proposal with the C# direction.
7. Completed the remaining review drafts and rewrote the full set in plain English.

Step 7 changes presentation and fills gaps. It does not approve new accounting policies or start development.

[Documentation index](README.md)
