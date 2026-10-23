# Books documentation

Status: Review drafts. The documents do not authorize development or deployment.

## Start here

Read [Project scope](01-project-scope.md), the [business workflows](11-workflows.md), then the [decision list](10-decisions.md). Confirmed decisions do not need another answer.

Planning → Requirements & Workflow → Technical Design → Development & Testing.

The current review focuses on planning and business flows, including Frontend user actions linked to API business operations. Frontend implementation remains deferred. Existing technical documents remain drafts; further technical detail is paused. They are not permission to implement.

## Business requirements

1. [Project scope and delivery stages](01-project-scope.md)
2. [Organizations, members and access](02-organizations-permissions.md)
3. [Companies and ledgers](03-companies-ledgers.md)
4. [Chart of accounts](04-chart-of-accounts.md)
5. [Journals and posting](05-journals-posting.md)
6. [Balances, activity and reports](06-reports.md)
7. [API and MCP principles](07-api-conventions.md)
8. [Testing and acceptance](08-testing-acceptance.md)
9. [Deployment and operations](09-deployment-operations.md)
10. [Decisions and open questions](10-decisions.md)
11. [Business workflows](11-workflows.md): diagrams, not Frontend designs.

## Technical proposals

- [Organization and access design](design/organizations-permissions.md): the model, authorization and lifecycle rules.
- [Detailed phase-one contract](design/phase-1-technical-contract.md): the proposed C# implementation, database constraints, HTTP/MCP contracts and development tasks.

The C# direction is confirmed. Exact versions, identity provider and other technical details remain proposals. Phase one creates the organization and access foundation, not a working accounting engine.

## Historical material

[Earlier account requirements](requirements.md) is a superseded summary. It records the original discussion without turning its suggestions into approved rules.

## Reading the status labels

- **Confirmed:** the owner accepted this decision.
- **Proposed:** a recommendation for review.
- **Open questions:** a business, cost or user-workflow decision is still needed; use the short business review list.
- **Technical Design Tasks:** engineering owns the details and validation. A proposed mechanism is not a request for the owner to choose its implementation.
- **Not run:** no test evidence exists yet.

Publishing a draft does not approve its contents. The next steps are review, a separate instruction to code, tests, and a separate release decision.
