# Books

An AI-native bookkeeping backend: multi-organization ledgers, documents that post to one journal core, and an MCP entry point that follows the same rules as the API.

| Read | What it is |
| --- | --- |
| [Product](docs/01-product.md) | Goal, scope, milestones and success criteria |
| [Domain rules](docs/02-domain-rules.md) | The rules the system must enforce, one line each |
| [Architecture](docs/03-architecture.md) | Modules, data model, posting flow, reports |
| [Cambodia tax](docs/04-cambodia-tax.md) | Tax requirements that shape the design |
| [Decisions](docs/05-decisions.md) | Decided, proposed defaults and open questions |
| [Workflow](docs/06-workflow.md) | How we build, review and release |

Stack: C#, ASP.NET Core, EF Core + Npgsql, PostgreSQL. No ABP.

The earlier review draft is kept unchanged in [docs/archive](docs/archive/2026-10-23-review-draft/README.md). Where it conflicts with these documents, these documents win.

## Development

Requirements: .NET 10 SDK, Docker (for local PostgreSQL and Testcontainers).

```bash
docker compose up -d                                  # local PostgreSQL
dotnet tool restore
dotnet ef database update --project src/Books.Infrastructure   # needs ConnectionStrings__Books
dotnet test --solution Books.slnx                     # integration tests start their own PostgreSQL
```

Without Docker, set `BOOKS_TEST_POSTGRES` to an admin connection string; the tests create a fresh database on that server.
