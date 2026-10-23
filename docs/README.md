# Books documentation

**Start with [System Design Principles](00-system-design-principles.md).** It explains the system in plain English before the detailed rules and contracts.

Current stage: requirements and system/technical design review. No implementation or deployment is approved. All acceptance scenarios are **Not run**.

## 1. Start here

- [Software development learning guide](development-guide.md): PRD → system design → technical design → tasks → acceptance → operations, using Journal Save as one example. **Guide; not a claim of completed development.**

- [Project scope and delivery stages](01-project-scope.md): the product goal, boundaries and staged plan. **Review draft.**
- [System Design Principles](00-system-design-principles.md): a short overview of the core boundaries and rules. **Confirmed principles plus labelled proposals.**
- [Decisions and open questions](10-decisions.md): what is settled and what still needs a decision. **Living review record.**

Current focus: Journal requirements, common/subsystem/Account validation and the atomic source-to-accounting contract. The organization foundation task list is retained. Frontend implementation and new subsystem development remain out of scope.

## 2. System design

- [System Design Principles](00-system-design-principles.md): organization/ledger isolation, the shared Journal core, validation, posting outcomes, Close, attachments and report truth.
- [Journal core contract](05-journals-posting.md#journal-core-contract): module responsibilities and the business effect of Save, edit and delete. **Confirmed rules and proposed source links.**
- [Posting data flow](design/journal-posting-contract.md#4-durable-candidate-and-atomic-official-effects): candidate input → checks → atomic official source and Journal result. **Technical proposal.**
- [Business workflows](11-workflows.md): User / Frontend / API responsibilities in compact diagrams. **Mixed confirmed rules and proposed interactions; not Frontend designs.**

## 3. Business requirements

- [Organizations, members and access](02-organizations-permissions.md): who belongs to a team and who may do what. **Core policies confirmed; some interactions proposed.**
- [Companies and ledgers](03-companies-ledgers.md): one ledger per company and accounting setup boundaries. **Relationship confirmed; setup options remain open.**
- [Chart of accounts](04-chart-of-accounts.md): account tree, fields, System Purpose/Account Usage and maintenance rules. **Accepted baseline plus proposed operations.**
- [Journals and posting](05-journals-posting.md): Qty, combined requirements, Save/Draft, Close and posting outcomes. **Confirmed requirements with labelled remaining choices.**
- [Foreign currency and revaluation](15-foreign-currency.md): human-initiated valuation, base-only adjustments and settlement. **Workflow confirmed; implementation and remaining accounting policies proposed/open.**
- [Reports](06-reports.md): TB, GL Detail, Balance Sheet and P&L. **Report set/core layout confirmed; detailed mechanics proposed.**
- [API and MCP principles](07-api-conventions.md): shared access checks and consistent request/result behavior. **Principles confirmed; exact contracts proposed.**

### Supporting modules

- [Party](12-party.md): complete Party scope, fields, operations, history rules and acceptance. **Line relationship confirmed; master-data policies proposed.**

## 4. Technical design

- [Party technical design](design/party-technical-design.md): proposed typed API, schema, transaction/race protection and acceptance matrix. **Design only; no code or tests.**
- [Journal posting contract](design/journal-posting-contract.md): proposed C# request/result shapes, rule composition, versions, transactions and recovery. **Not implemented or tested.**
- [Organization and access design](design/organizations-permissions.md): identity, membership, invitation and grant responsibilities. **Design proposal.**
- [Foundation technical contract](design/phase-1-technical-contract.md): proposed API/data models, constraints, transactions, MCP and development tasks. **Foundation only; not accounting implementation.**

The direction is C#, ASP.NET Core, EF Core and PostgreSQL, without ABP in phase one. Exact versions, identity provider and interoperability remain unverified proposals. Each module keeps its own requirements and technical detail; this index does not combine everything into one large document.

## 5. Testing, acceptance and operations

- [Testing and acceptance](08-testing-acceptance.md): expected scenarios, rule basis and evidence to collect. **All Not run.**
- [Deployment and operations](09-deployment-operations.md): recovery, attachments, operational responsibilities and release safeguards. **Proposal; no environment or deployment created.**

## 6. Decisions and open questions

- [Confirmed decisions](10-decisions.md#confirmed): accepted business and technology choices. Do not ask for them again.
- [Short business review list](10-decisions.md#short-business-review-list): genuine owner decisions, not engineering implementation questions.
- [Technical design tasks](10-decisions.md#technical-design-tasks): engineering-owned mechanisms and validation work.

## Status labels

- **Confirmed:** the owner accepted this rule.
- **Proposed:** a recommendation, not automatic approval.
- **Open:** a business, cost or workflow choice is unresolved.
- **Not run:** no execution evidence exists.

Planning → Requirements & Workflow → System/Technical Design → Development & Testing. A separate instruction is required to start coding; deployment needs separate authorization. Publishing these documents is not implementation approval.

## Historical material

[Earlier account requirements](requirements.md) is a superseded summary retained for context, not the current contract.

