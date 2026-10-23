# Decisions and open questions

Updated: 2026-10-23  
Status: Review record. No coding or deployment is approved.

## Confirmed

### Project and delivery

- Build Books as a bookkeeping backend.
- Start with organizations and access control.
- Defer Frontend implementation. Plan Frontend user actions together with API business operations.
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

### COA labels and P&L grouping

- Rename Expense to **Cost & Expense**.
- Use two top-level P&L groups: **Income (add)** and **Cost & Expense (subtract)**.
- This is reporting classification, not a change to debit/credit treatment or an instruction to discard signed or contra balances.

### Account tree planning baseline

The five-type account tree in [Chart of Accounts](04-chart-of-accounts.md#account-tree) is accepted with these changes: no Other Income group, FX gains directly under Income, and generic asset names such as Bank Account and Property, Plant & Equipment. System is a function flag, not another type. Current Year Profit is a calculated non-posting report item.

This approval covers the tree, not automatic account generation, new field requirements or maintenance policies. Future/conditional account activation, detailed year-end calculations and the separate FX line/account design remain open.

### Account fields

- Add Default Usage as subsystem + purpose. It is distinct from Type and System Purpose.
- Include optional Description and Sort Order.
- Code is user-entered and editable. A stable Account ID preserves journal identity when Code changes.
- Code must be unique within the same ledger; different ledgers may use the same Code. Editable Code must still satisfy this rule.
- Specific subsystem lists and multiple Default Usages per account remain proposals; accepting the field does not activate those modules.

### Initial System Account purposes

- Identify System Accounts by fixed function mapped to account identity, not by name or code.
- Retained Earnings holds prior-year accumulated results; the detailed closing policy remains open.
- Opening Balance Clearing is temporary during opening-balance setup and must be zero when setup is complete.
- Rounding Differences is enabled only if automatic rounding adjustments are needed.
- These purposes are accepted, not an instruction to create all accounts initially or to implement them.

### Suspense scope

Include incomplete data, unusable records and import or processing errors in Suspense review. Distinguish valid entries posted to a Suspense Account from invalid or failed records kept unposted for review. This does not authorize posting invalid entries or select a Frontend layout.

### Technology direction

Use C#, ASP.NET Core, EF Core and PostgreSQL. API and MCP come first. Do not add ABP in phase one. A modular monolith is the proposed application structure, not an instruction to implement it.

This replaces the earlier Go proposal. It does not select exact package versions, an identity provider, hosting or production settings.

## Proposed, not approved

The accounting chapters now contain a complete starting proposal. Except for the specifically confirmed labels, P&L grouping, account tree and initial System Account purposes above, they are not a statement of approved accounting policy.

Default Accounts as optional starter templates remain proposed. Current Year Profit's non-posting place in the tree is accepted; calculation and closing details remain open. The posted-account versus unposted-review distinction is accepted. A shared entry point and Posted / Not posted / Pending or Unknown presentation remain workflow proposals. Uncertain commit outcomes must be reconciled before retrying; errors never justify unbalanced posting. FX and AR/AP/Inventory placeholders are accepted in the planning tree, but their module activation and behavior are not approved. A tax-control mapping remains a further proposal. No account or mapping has been created.

The detailed technical contract proposes .NET/EF Core/Npgsql version families, Keycloak evaluation, authenticated invitation inboxes, transaction and lock rules, API fields, MCP delegation and development stages. Exact implementation details remain under review.

ASP.NET Core Identity is not, by itself, a complete OAuth/OIDC authorization server. Moving to C# does not authorize replacing the external identity-provider plan with homemade authentication.

No tests have been run. No production service, database, account, credential, invitation or permission has been created by this documentation work.

## Short business review list

The recommended defaults are proposals. Answer by topic; do not repeat the confirmed decisions above.

| Topic | What needs a decision |
| --- | --- |
| 1. Ledger setup | Base currency for each company, whether foreign-currency transactions are needed in the first accounting release, fiscal year and business time zone. |
| 2. Accounts | The tree is accepted. Code format and comparison rules; whether duplicate names are acceptable; how account changes and deactivation should work; needed system functions and optional starter templates. |
| 3. Posting and corrections | Opening balances, who can post, period closing/reopening and the correction process for posted entries. |
| 4. First reports | Which reports are required, date and balance presentation, and whether export is needed initially. |

Read the corresponding chapter for the proposed behavior and a concrete example. Once a choice is accepted, update that chapter and this record together.

## Current review order

Planning → Requirements & Workflow → Technical Design → Development & Testing. Focus now on planning and business flows. Keep existing technical documents as drafts and pause further technical detail. This does not revoke confirmed business rules or the C# direction.

## Technical Design Tasks

For the later technical-design review, engineering owns schema and constraints, transactions, API/MCP contracts, identity/session checks, retry handling, concurrency and technical test design. The [detailed phase-one contract](design/phase-1-technical-contract.md) already proposes these mechanisms. Review and validate them; do not ask the owner to supply the design.

The identity provider, exact dependency combination and interoperability remain unverified technical proposals. Engineering should compare options and recommend one. Ask the owner only about material costs, login experience, recovery workflow or other business trade-offs.

## Business and release review

Before production, agree on budget, hosting/data-location constraints, operators, acceptable recovery targets and the acceptance decision-maker. Engineering proposes the hosting configuration, test levels and release gates against those needs.

A separate instruction is still required before coding, and separate authorization before deployment. Moving a question to engineering does not approve a provider, service purchase, credential setup or implementation.

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
8. Separated engineering-owned design tasks from owner business decisions. Existing technical proposals remain subject to review and validation.
9. Standardized the term Frontend and added business workflow diagrams. Frontend implementation remains deferred; business interaction planning is in scope. Proposed accounting flows are not approved by their inclusion.

10. Confirmed Cost & Expense naming, two P&L groups and three conditional System Account purposes. Default templates and further System Account candidates remain proposed.
11. Included incomplete, unusable and errored records in Suspense scope. Added proposed safe review and resolution handling without approving invalid posting.

12. Accepted the revised account tree with no Other Income group and generic asset names. Conditional activation and operational rules remain separate decisions.

13. Added Default Usage as subsystem + purpose, optional Description, Sort Order and user-editable Code. Confirmed Code uniqueness within each ledger; the same Code may exist in different ledgers. The earlier fixed-Code-after-use proposal is superseded.

14. Added Edit, Deactivate and Delete Account workflow drafts. Requesting these diagrams does not approve their proposed operation restrictions or authorize real account changes.

Steps 7–9 clarify the drafts and review process. They do not approve new accounting policies or start development.

[Documentation index](README.md)
