# Books

Books is a minimal general ledger MVP with an ASP.NET Core Web API, a Blazor Server back-office UI, an Angular frontend, PostgreSQL, and Entity Framework Core.

## Project Structure

```text
/
|-- Books.slnx
|-- backend
|   |-- Books.Domain
|   |-- Books.Application
|   |-- Books.Infrastructure
|   |-- Books.Api
|   `-- Books.Blazor
`-- frontend
    `-- books-ui
```

The API and Blazor UI share the same Domain, Application, and Infrastructure projects. Business rules should live in Application services, not in UI pages.

## Environments

Real `.env` files are ignored by git. Commit only `.env.example` templates.

Do not commit database passwords, Railway tokens, or real secrets.

## Backend Configuration

Template:

```text
backend/Books.Api/.env.example
backend/Books.Blazor/.env.example
```

Supported variables:

```text
DATABASE_URL=
ConnectionStrings__DefaultConnection=
CORS_ALLOWED_ORIGINS=http://localhost:4200,https://laudable-blessing-production-afd7.up.railway.app
ASPNETCORE_ENVIRONMENT=Development
```

Database connection priority:

1. `DATABASE_URL`
2. `ConnectionStrings__DefaultConnection`
3. `ConnectionStrings:DefaultConnection` in `appsettings.Development.json` or `appsettings.json`

Railway PostgreSQL usually provides a URL like:

```text
postgresql://user:password@host:port/database
```

Books.Api and Books.Blazor both use the shared `Books.Infrastructure` database configuration. `DATABASE_URL` and `ConnectionStrings__DefaultConnection` both support `postgresql://` and `postgres://` URLs and convert them to the Npgsql format automatically:

```text
Host=...;Port=...;Database=...;Username=...;Password=...;SSL Mode=Require;Trust Server Certificate=true
```

The backend and Blazor UI use `UseNpgsql`, not SQLite.

`appsettings.json` contains only local development placeholders. Put real Railway or production values in environment variables.

Local development appsettings example:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=books_dev;Username=postgres;Password=postgres"
  },
  "App": {
    "BaseCurrency": "USD",
    "DefaultLedgerName": "Demo Ledger"
  }
}
```

### Backend CORS

CORS is read from:

```text
CORS_ALLOWED_ORIGINS
```

Multiple origins are comma-separated:

```text
CORS_ALLOWED_ORIGINS=http://localhost:4200,https://laudable-blessing-production-afd7.up.railway.app
```

Do not include a trailing slash in origins. Use `https://laudable-blessing-production-afd7.up.railway.app`, not `https://laudable-blessing-production-afd7.up.railway.app/`.

If the variable is not set, the backend defaults to:

```text
http://localhost:4200,https://laudable-blessing-production-afd7.up.railway.app
```

## Frontend Configuration

Template:

```text
frontend/books-ui/.env.example
```

Current template value:

```text
API_BASE_URL=https://books-backend-production-450e.up.railway.app/api/v1
```

Angular does not read this `.env` file at browser runtime. It is a deployment note/template only.

Frontend API URLs are controlled by Angular environment files:

```text
frontend/books-ui/src/environments/environment.ts
frontend/books-ui/src/environments/environment.prod.ts
```

Local development:

```ts
apiBaseUrl: 'http://localhost:5199/api/v1'
```

Production:

```ts
apiBaseUrl: 'https://books-backend-production-450e.up.railway.app/api/v1'
```

Do not put secrets in frontend code or frontend variables.

## Local Development

Backend:

```powershell
cd C:\Users\User\Desktop\Books\backend\Books.Api
dotnet restore
dotnet build
dotnet run
```

The backend runs EF Core migrations and idempotent seed data on startup during the current MVP stage.

Blazor back-office UI:

```powershell
cd C:\Users\User\Desktop\Books\backend\Books.Blazor
dotnet restore
dotnet run
```

Open the URL printed by `dotnet run`, usually:

```text
https://localhost:5001
```

Books.Blazor connects to PostgreSQL directly through the shared Infrastructure project. It does not require Books.Api to be running. The Angular frontend still requires Books.Api.

Frontend:

```powershell
cd C:\Users\User\Desktop\Books\frontend\books-ui
npm install
npm run dev
```

Open:

```text
http://localhost:4200
```

Production build preview:

```powershell
cd C:\Users\User\Desktop\Books\frontend\books-ui
npm run build
npm start
```

`npm start` serves the production build from:

```text
dist/books-ui/browser
```

## Railway Deployment

Railway domains:

```text
Backend:  https://books-backend-production-450e.up.railway.app
Frontend: https://laudable-blessing-production-afd7.up.railway.app
```

### Railway Root Directory

Books.Blazor Service:

```text
/backend
```

Books.Api Service:

```text
/backend
```

This repository is a .NET monorepo. The backend directory contains `Books.slnx` and `global.json`, so Railway can use `/backend` as the root directory and restore the backend solution before publishing a specific host project.

### Railway Docker Builds

Use Dockerfile builds instead of Nixpacks/build commands.

Books.Blazor service:

```text
Root Directory: /backend
Dockerfile Path: Dockerfile.blazor
```

Books.Api service:

```text
Root Directory: /backend
Dockerfile Path: Dockerfile.api
```

Frontend service:

```text
Root Directory: /frontend/books-ui
Dockerfile Path: Dockerfile
```

The backend Dockerfiles publish the selected .NET host project and run it from the ASP.NET runtime image. The apps still read Railway's `PORT` variable in `Program.cs`; the Dockerfiles expose `8080` as a local fallback.

The frontend Dockerfile builds Angular and serves the production files with nginx.

Previous non-Docker Books.Blazor build command:

```bash
dotnet publish Books.Blazor/Books.Blazor.csproj -c Release -o Books.Blazor/out --no-self-contained
```

Previous non-Docker Books.Blazor start command:

```bash
dotnet Books.Blazor/out/Books.Blazor.dll
```

Previous non-Docker Books.Api build command:

```bash
dotnet publish Books.Api/Books.Api.csproj -c Release -o Books.Api/out --no-self-contained
```

Previous non-Docker Books.Api start command:

```bash
dotnet Books.Api/out/Books.Api.dll
```

Frontend Service:

```text
/frontend/books-ui
```

PostgreSQL:

```text
No public domain required
```

Books.Blazor:

```text
Public domain required
```

Books.Api:

```text
Public domain required
```

Frontend:

```text
Public domain required
```

### Books.Blazor Railway Variables

Set these in the Books.Blazor service `Variables` tab:

```text
DATABASE_URL=${{Postgres.DATABASE_URL}}
ASPNETCORE_ENVIRONMENT=Production
```

Alternative database variable:

```text
ConnectionStrings__DefaultConnection=${{Postgres.DATABASE_URL}}
```

Optional:

```text
ASPNETCORE_URLS=http://+:${PORT}
```

Books.Blazor also reads Railway's `PORT` variable directly in `Program.cs`, so `ASPNETCORE_URLS` is optional.

### Books.Api Railway Variables

Set these in the Books.Api service `Variables` tab:

```text
DATABASE_URL=${{Postgres.DATABASE_URL}}
CORS_ALLOWED_ORIGINS=https://laudable-blessing-production-afd7.up.railway.app
ASPNETCORE_ENVIRONMENT=Production
```

Alternative database variable:

```text
ConnectionStrings__DefaultConnection=${{Postgres.DATABASE_URL}}
```

Optional:

```text
ASPNETCORE_URLS=http://+:${PORT}
```

Books.Api also reads Railway's `PORT` variable directly in `Program.cs`, so `ASPNETCORE_URLS` is optional.

If your Railway PostgreSQL service is not named `Postgres`, replace the service name. Example:

```text
DATABASE_URL=${{PostgreSQL.DATABASE_URL}}
```

Railway supports adding variables with `New Variable` or pasting `.env` style values in the `RAW Editor`.

`backend/Books.Api/.env.example` is only a template. Railway uses the variables configured in the service.

### Frontend Railway Variables

No required variables right now.

The production API URL is currently configured at build time in:

```text
frontend/books-ui/src/environments/environment.prod.ts
```

Frontend service does not need `DATABASE_URL`.

Angular frontend environment values are build-time values. Do not assume the browser can read Railway runtime variables after the app has been built.

### Frontend Production Start

When using Docker, Railway should use `frontend/books-ui/Dockerfile`; no frontend start command is required.

If running without Docker, the frontend production start command must not use `ng serve`.

Current scripts:

```json
{
  "dev": "ng serve",
  "build": "ng build --configuration production",
  "start": "node scripts/serve.mjs"
}
```

The project also includes a Dockerfile that builds Angular and serves:

```text
/app/dist/books-ui/browser
```

with nginx.

## Seed Data

On database creation the backend seeds:

- Ledger: `Demo Ledger`
- Accounts:
  - `1000 Cash - Asset`
  - `1100 Accounts Receivable - Asset`
  - `2000 Accounts Payable - Liability`
  - `3000 Owner Equity - Equity`
  - `4000 Sales Revenue - Revenue`
  - `5000 General Expense - Expense`
- Posted journal entry:
  - `JV-000001`
  - `Owner capital injection`
  - Dr Cash 1000
  - Cr Owner Equity 1000

Seed data is idempotent. Re-running the API will not duplicate `Demo Ledger`, accounts, or `JV-000001`.

## API Summary

- `GET /api/v1/ledgers`
- `GET /api/v1/ledgers/{id}`
- `POST /api/v1/ledgers`
- `PUT /api/v1/ledgers/{id}`
- `DELETE /api/v1/ledgers/{id}`
- `GET /api/v1/ledgers/{ledgerId}/accounts`
- `GET /api/v1/accounts/{id}`
- `POST /api/v1/ledgers/{ledgerId}/accounts`
- `PUT /api/v1/accounts/{id}`
- `DELETE /api/v1/accounts/{id}`
- `GET /api/v1/ledgers/{ledgerId}/journal-entries`
- `GET /api/v1/journal-entries/{id}`
- `POST /api/v1/ledgers/{ledgerId}/journal-entries`
- `PUT /api/v1/journal-entries/{id}`
- `POST /api/v1/journal-entries/{id}/post`
- `DELETE /api/v1/journal-entries/{id}`
- `GET /api/v1/ledgers/{ledgerId}/reports`
- `GET /api/v1/ledgers/{ledgerId}/reports/trial-balance`
- `GET /api/v1/ledgers/{ledgerId}/reports/general-ledger?accountId={accountId}`
- `GET /api/v1/ledgers/{ledgerId}/reports/profit-loss`
- `GET /api/v1/ledgers/{ledgerId}/reports/balance-sheet`
