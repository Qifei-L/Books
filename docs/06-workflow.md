# Workflow

## Loop for every slice

1. **Pick a slice** from the current milestone: one complete path, for example "post a manual journal and see it in the TB".
2. **Write the acceptance test first** against real PostgreSQL. Domain rules marked **[T]** become property or race tests.
3. **Implement** API endpoint, application service, SQL/migration and MCP tool together.
4. **Open a small PR** to `main`: under about 400 changed lines, with tests, and with any business question written at the top.
5. **CI** runs build, unit tests, Testcontainers integration tests and migration checks. Red CI never merges.
6. **Review**: the owner answers business questions only. Engineering decides API fields, locks and SQL.
7. **Merge and deploy to staging** automatically. The owner tries it through MCP or the minimal UI.
8. **Update docs** in the same PR when a rule or decision changes. Docs describe what exists, not plans.

## Cadence

- A runnable build from week 1: sign in → create ledger → post a journal → see the TB.
- A demo at the end of each milestone, using real (anonymized) data from Manager.io once M9 import exists.
- Decisions are recorded in [Decisions](05-decisions.md) on the day they are made.

## Environments

| Env | Purpose | Data |
| --- | --- | --- |
| local | Development | Docker PostgreSQL, synthetic data |
| staging | Owner testing, AI trials | Anonymized imports |
| production | Real books, after parallel run | Managed PostgreSQL with point-in-time recovery, daily restore test |

Production release needs: all **[T]** invariants green, the migration reconciliation report clean, a backup restored once, and the owner's explicit go.

## Definition of done (slice)

- Acceptance test passes in CI.
- API and MCP give the same result and the same permission errors.
- Audit entries are written.
- Docs updated if behavior changed.
