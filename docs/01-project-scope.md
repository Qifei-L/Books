# Project scope

Status: Review draft. Documentation only. No implementation is approved.

## Confirmed

- Books is a bookkeeping backend.
- Start with organizations, members and access control.
- One organization can manage several companies. Each company has one separate ledger in the first release.
- Use C#, ASP.NET Core, EF Core and PostgreSQL. Do not add ABP in the first phase.
- API and MCP use the same backend rules. Frontend implementation is deferred, but planning user actions and API business operations together is in scope.
- Write and review the documents before starting development.

## Current sequence

Planning → Requirements & Workflow → Technical Design → Development & Testing.

We are reviewing planning and business workflows now. First agree on the purpose, scope, users and business steps. Then review technical design. Only begin development and testing after a separate instruction.

Existing technical documents are retained as drafts. Detailed technical expansion is paused while the business plan and workflows are reviewed. Confirmed business rules and the C# direction remain unchanged.

## Proposed delivery plan

| Stage | Deliverable | Exit check |
| --- | --- | --- |
| 0a. Planning | Purpose, users, priorities and scope | The owner accepts the business scope. |
| 0b. Requirements & Workflow | Business rules, workflow diagrams and business choices | The owner reviews the flows and unresolved business choices. |
| 0c. Technical Design | Engineering proposals based on the agreed flows | Engineering reviews the design; business trade-offs are resolved. No coding is authorized by this review. |
| 1. Organization foundation | Users, organizations, companies, ledger records, invitations, access and audit | Isolation, invitation and administrator handover tests pass. A ledger record is not yet a working accounting ledger. |
| 2. Account management | Account list, maintenance and lifecycle | Account rules are accepted and tested. No fake balances are shown. |
| 3. Posting | Draft journals, posting, opening balances and corrections | Posting is balanced, atomic and traceable. |
| 4. Queries and reports | Balances, ledger activity and agreed reports | Reports reconcile to posted entries. |
| 5. Release review | Test evidence, operating plan and recovery evidence | The owner explicitly approves the release and its destination. |

Stages 2–5 are proposals, not approved implementation commitments. The current detailed technical contract covers Stage 1 only.

## What is not included now

No inventory, payroll, tax filing, bank feed, automatic reconciliation, group consolidation or full ERP. No production migration, purchase, new credential or deployment is authorized by these documents.

## How we work

1. Agree on business outcomes, scope, requirements and workflows.
2. After that review, evaluate the technical design and its limits.
3. Obtain a separate instruction to start coding.
4. Implement small changes on a review branch.
5. Run the agreed tests and review the code.
6. Obtain separate approval for production release.
7. Verify the released version and its critical behavior.

A published draft is not an approved requirement. An approved requirement is not a passing test. A passing test is not release permission.

## Open questions

See the short [decision list](10-decisions.md). Do not ask again about decisions already marked Confirmed.

[Documentation index](README.md)
