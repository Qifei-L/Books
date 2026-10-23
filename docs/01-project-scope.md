# Project scope

Status: Review draft. Documentation only. No implementation is approved.

## Confirmed

- Books is a bookkeeping backend.
- Start with organizations, members and access control.
- One organization can manage several companies. Each company has one separate ledger in the first release.
- Use C#, ASP.NET Core, EF Core and PostgreSQL. Do not add ABP in the first phase.
- API and MCP use the same backend rules. The Books Web interface is out of scope for now.
- Write and review the documents before starting development.

## Proposed delivery plan

| Stage | Deliverable | Exit check |
| --- | --- | --- |
| 0. Design review | Business rules, technical contracts and open decisions | The owner accepts the scope. Important unanswered questions are recorded. |
| 1. Organization foundation | Users, organizations, companies, ledger records, invitations, access and audit | Isolation, invitation and administrator handover tests pass. A ledger record is not yet a working accounting ledger. |
| 2. Account management | Account list, maintenance and lifecycle | Account rules are accepted and tested. No fake balances are shown. |
| 3. Posting | Draft journals, posting, opening balances and corrections | Posting is balanced, atomic and traceable. |
| 4. Queries and reports | Balances, ledger activity and agreed reports | Reports reconcile to posted entries. |
| 5. Release review | Test evidence, operating plan and recovery evidence | The owner explicitly approves the release and its destination. |

Stages 2–5 are proposals, not approved implementation commitments. The current detailed technical contract covers Stage 1 only.

## What is not included now

No inventory, payroll, tax filing, bank feed, automatic reconciliation, group consolidation or full ERP. No production migration, purchase, new credential or deployment is authorized by these documents.

## How we work

1. Agree on business outcomes.
2. Review the technical design and its limits.
3. Obtain a separate instruction to start coding.
4. Implement small changes on a review branch.
5. Run the agreed tests and review the code.
6. Obtain separate approval for production release.
7. Verify the released version and its critical behavior.

A published draft is not an approved requirement. An approved requirement is not a passing test. A passing test is not release permission.

## Open questions

See the short [decision list](10-decisions.md). Do not ask again about decisions already marked Confirmed.

[Documentation index](README.md)
