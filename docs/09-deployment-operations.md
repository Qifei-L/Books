# Deployment and operations

Status: Proposed operating plan. No environment or deployment has been approved.

## Purpose

Make releases recoverable and problems visible. This document describes required evidence, not work already done.

## Environments and access

Propose separate development, test and production environments. Use synthetic data in development and automated tests.

The application uses a restricted database account. Schema changes use a separate deployment identity. Do not give the application database superuser or schema-change privileges. Do not put credentials in Git, examples, logs or test reports.

C# and PostgreSQL are the selected direction. Hosting provider, region, identity provider, exact versions, domain, budget and operators remain open. Keycloak is a candidate, not an approved service.

## Release checklist

Before a release:

- Record the exact commit, dependency versions and build artifact.
- Review EF Core migrations and their generated PostgreSQL SQL.
- Test upgrades and rollback or forward-repair steps on a disposable database.
- Run authorization, accounting and API/MCP tests appropriate to the scope.
- Confirm backups are usable by performing a restore test.
- Define the release owner, maintenance window, stop conditions and incident contact.
- Obtain approval for the destination, cost and release action.

Apply migrations as a controlled deployment step. Do not let every application instance change the schema at startup.

## Backups and recovery

Back up both business data and the identity service configuration needed to restore access. Protect backups with restricted access and encryption. Agree on retention, maximum acceptable data loss and maximum recovery time before production.

A backup job reporting success is not proof of recovery. Restore into an isolated environment and check organizations, access, audit history and accounting totals where available.

Do not restore an old backup over newer transactions as a routine application rollback. Assess the missing transactions and use an approved recovery or forward-repair plan. Keep an incident record.

## Monitoring and audit

Track service availability, error rate, response time, failed authentication, denied access, database capacity and failed background work. Alerts need an owner and a clear next action.

Use request identifiers to connect API, MCP and backend events. Logs must not contain tokens, passwords, invitation secrets or unrelated customer data. Access to logs and audit records is restricted.

Keep business audit events separate from diagnostic logs. A failed operation must not appear as a successful business change. Normal application operations cannot rewrite audit history.

## Incident response

1. Contain the problem without destroying evidence.
2. Identify affected organizations and operations.
3. Stop unsafe writes if accounting integrity or access control is at risk.
4. Restore service using the reviewed plan.
5. Reconcile data and record the impact.
6. Explain the cause, fix and remaining risk.

Security suspension of a compromised last administrator must still be possible. Follow the proposed locked-recovery process in the [technical contract](design/phase-1-technical-contract.md). Never appoint a new administrator automatically or bypass identity checks to make recovery easier. The recovery procedure needs separate approval before production.

## After release

Verify the deployed commit and configuration. Test a normal authorized request and a denied cross-organization request. Check migrations, audit writes and agreed accounting totals. Watch for regressions within a release-specific observation window.

## Business and operating decisions

Before production, agree on budget, hosting/data-location constraints, operators, acceptable data loss and recovery time, retention needs and incident escalation. These choices do not block writing the documents, but they block an actual release.

## Technical Design Tasks

These tasks belong to the later technical-design stage. Current review focuses on planning and business workflows; detailed expansion is paused.

Engineering recommends the provider and configuration, backup implementation, monitoring, restore tests and release procedure within those constraints. The owner is not expected to design these mechanisms. Explain any cost or workflow trade-off before requesting a business decision. No environment, credential or deployment is authorized by this plan.

[Documentation index](README.md)
