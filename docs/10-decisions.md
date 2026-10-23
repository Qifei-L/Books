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

### Suspense, Error and Pending

Call the second category **Error**, replacing the earlier label Suspense Review. Include incomplete data, unusable records and definite import or processing failures in Error review. Unknown outcomes remain **Pending**, not Error. Distinguish valid entries posted to a Suspense Account from invalid or failed records kept unposted for review. This does not authorize posting invalid entries or select a Frontend layout.

### Journal line Item

Add Item (product or service) to journal lines, separate from Account. Item is optional, including for pure adjustment journals. Selecting Item may suggest a default Account; the actual Account used is saved on the line. This does not approve the full header/line model, inventory, quantity, costing or FX behavior.

### Journal transaction currency

Each journal uses one transaction currency, with the same currency on all lines. No independent line-currency override is allowed. Base-currency conversion amounts are valuations, not mixed transaction currencies.

Header Currency and line transaction/base amounts are proposed field placement. One journal shares one exchange rate. Manual entry is allowed; source is backend information and manually entered rates are labelled Manual. Supported currencies, rate direction/precision, rounding and account-currency constraints remain separate design decisions. Cross-currency transfers require a separate future workflow. No tax or FX implementation is approved.

### Post validation and Error

Validate date, accounts, currency, amounts and debit/credit balance before posting. All must pass. Definite validation failures retain the draft and specific errors in Error review, not a forced Suspense Account posting. They have no balance/report effect or partial entries. Correct and revalidate before resubmitting.

Unknown outcomes such as a lost response require reconciliation before retrying. Current ledger permissions apply to drafts and error details. Detailed posting eligibility and calculation policies remain subject to their separate status.

### Posted journal changes and Close

- Posted status does not itself prohibit editing or deletion.
- Authorized users may change amounts, accounts or dates, or delete posted journals, while the applicable scope is open.
- People control Close or leaving the scope open; it is not automatic. Close blocks edits and deletion. Authorized people control reopening; it is never automatic.
- The cutoff is evaluated against accounting date/time, not record creation time. Both the old and new accounting dates must be editable when moving a journal date.
- Date-versus-instant and timezone interpretation are later design details; do not silently use UTC midnight or force monthly closing.
- Successful edits replace the prior accounting effect after validation. A failed edit retains the valid posted version and sends the attempted change to Error review without changing balances.
- Successful deletion removes the accounting effect and retains an audit record of actor, time and changes.
- Organization administrators perform Close within their organization. Close uses an inclusive accounting cutoff; new posting, edits and deletion at or before it are blocked. Later accounting dates/times remain open. Reopening permissions remain open. Reversal may be optional; it is not the only allowed correction method.

### Journal numbering

The system generates a unique Journal No. Reference is entered by the user and may repeat. Number format, scope and allocation timing remain design details. No attachment policy or new Document/container model is approved by this decision.

### Attachment storage portability

Files and their journal links must be preserved when changing the attachment storage container. This requirement is confirmed. It does not introduce a new journal Document data model.

Document Provider, stable Attachment IDs, location mappings, copy/checksum/read verification, version tracking, backups and rollback are proposed means of meeting the requirement. No storage provider, credentials, sharing expansion, migration or deletion is approved. Attachments are optional: saving and posting do not require an upload. Attachment counts and retention policy remain open.

### Human-configured taxes

People choose the tax settings. The system validates and processes those settings rather than selecting taxes on its own. The number of taxes per line, calculation rules and accounting versus reporting-only treatment are not yet settled; no particular tax implementation is approved.

### Technology direction

Use C#, ASP.NET Core, EF Core and PostgreSQL. API and MCP come first. Do not add ABP in phase one. A modular monolith is the proposed application structure, not an instruction to implement it.

This replaces the earlier Go proposal. It does not select exact package versions, an identity provider, hosting or production settings.

## Proposed, not approved

The accounting chapters now contain a complete starting proposal. Except for the specifically confirmed labels, P&L grouping, account tree and initial System Account purposes above, they are not a statement of approved accounting policy.

Default Accounts as optional starter templates remain proposed. Current Year Profit's non-posting place in the tree is accepted; calculation and closing details remain open. The posted-account versus unposted-review distinction is accepted. A shared or separate page layout remains proposed. Suspense, Error and Pending remain distinct categories, not interchangeable accounting balances. Uncertain commit outcomes must be reconciled before retrying; errors never justify unbalanced posting. FX and AR/AP/Inventory placeholders are accepted in the planning tree, but their module activation and behavior are not approved. A tax-control mapping remains a further proposal. No account or mapping has been created.

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
11. Included incomplete, unusable and errored records in the review category, later renamed Error. Added proposed safe review and resolution handling without approving invalid posting.

12. Accepted the revised account tree with no Other Income group and generic asset names. Conditional activation and operational rules remain separate decisions.

13. Added Default Usage as subsystem + purpose, optional Description, Sort Order and user-editable Code. Confirmed Code uniqueness within each ledger; the same Code may exist in different ledgers. The earlier fixed-Code-after-use proposal is superseded.

14. Added Edit, Deactivate and Delete Account workflow drafts. Requesting these diagrams does not approve their proposed operation restrictions or authorize real account changes.

15. Renamed Suspense Review to Error; retained posted Suspense Account and separate Pending outcomes. This changes labels, not accounting effects or permissions.

16. Confirmed human-configured taxes, administrator Close, one shared journal exchange rate with backend source and Manual labelling, and optional attachments. Close now uses an inclusive accounting cutoff; reopening rights and detailed tax behavior remain open.

Steps 7–9 clarify the drafts and review process. They do not approve new accounting policies or start development.

[Documentation index](README.md)
