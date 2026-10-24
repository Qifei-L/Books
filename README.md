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

### Run it locally and try it

```bash
docker compose up -d
export ConnectionStrings__Books="Host=localhost;Port=5432;Username=books;Password=books;Database=books"
dotnet tool restore
dotnet ef database update --project src/Books.Infrastructure --startup-project src/Books.Infrastructure --connection "$ConnectionStrings__Books"
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Books.Api --urls http://localhost:5180
```

- **Preview UI:** open <http://localhost:5180/app/>, sign in with the local test issuer (any subject), then **Create demo organization**. The demo has a year of journals, purchases, sales, payments, an imported bank statement and an October tax return. The UI is read-only; work goes through the API and MCP.
- **API:** `POST /dev/token` with `{"subject": "me"}` returns a bearer token (Development only). Endpoints live under `/api/v1`, for example `/api/v1/organizations`. Writes need an `Idempotency-Key` header (a UUID) and, for changes, `If-Match: "v<version>"`.
- **MCP:** `POST /dev/token` with `{"subject": "me", "mcp": true}` gives a token for `http://localhost:5180/mcp` (Streamable HTTP). Point an MCP client such as Claude Desktop, Claude Code or Cursor at it with that bearer token; `tools/list` shows every tool. AI clients save drafts; posting, filing and publishing need a person through the API.
- **No Docker:** any PostgreSQL 17 works; set the connection string to it. To start over, drop and recreate the database and run the migrations again.
- Tokens from the local issuer last a short time and stop working when the API restarts; sign in again.
