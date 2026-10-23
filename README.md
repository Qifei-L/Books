# Books

Books is a bookkeeping backend in the documentation and review stage.

## Current direction

- Start with organizations and access control.
- One organization can manage several companies. Each company has one ledger in the first release.
- Use C#, ASP.NET Core, EF Core and PostgreSQL. No ABP in the first phase.
- API and MCP first. No Books Frontend yet.
- No implementation or deployment has been approved.

## Read the documents

1. [Documentation index](docs/README.md)
2. [Confirmed decisions and open questions](docs/10-decisions.md)
3. [Organizations and access](docs/02-organizations-permissions.md)
4. [Business workflow diagrams](docs/11-workflows.md)

Current sequence: Planning → Requirements & Workflow → Technical Design → Development & Testing. We are reviewing the first two steps. The [technical proposal](docs/design/phase-1-technical-contract.md) is retained as a draft; further technical detail and development are paused.

The documents use plain English. Confirmed rules, proposed behavior and open questions are kept separate. Tests described in the documents have not been run.
