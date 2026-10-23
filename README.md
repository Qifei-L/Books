# Books

Books is a bookkeeping backend in the requirements and system-design review stage.

## Read the design

**[Documentation contents](docs/README.md)** is the main entrance.

Start with [System Design Principles](docs/00-system-design-principles.md), then read the module requirements and technical proposals. The contents page shows what each document covers and whether it is confirmed, proposed or not yet tested.

## Current direction

- Organizations manage teams and companies; each company initially has one independent ledger.
- C#, ASP.NET Core, EF Core and PostgreSQL. No ABP in phase one.
- API and MCP share backend rules. Frontend implementation is deferred.
- Current focus: the Journal core, combined validation and safe source-to-accounting updates.
- The organization foundation design and task list remain available.

[Confirmed decisions and open questions](docs/10-decisions.md) distinguish accepted rules from proposals. No coding, merge or deployment is approved. Acceptance scenarios have not been run.
