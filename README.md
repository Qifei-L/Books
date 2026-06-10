# Books

Books is a minimal general ledger MVP with an ASP.NET Core Web API backend, Angular frontend, PostgreSQL database, and Entity Framework Core.

## Project Structure

```text
C:\Users\User\Desktop\Books
|-- backend
|   `-- Books.Api
`-- frontend
    `-- books-ui
```

## Backend

Set a PostgreSQL connection string before running against Railway or another remote database.

For local development, copy:

```text
C:\Users\User\Desktop\Books\backend\Books.Api\.env.example
```

to:

```text
C:\Users\User\Desktop\Books\backend\Books.Api\.env
```

Then fill in either `DATABASE_URL` or `ConnectionStrings__DefaultConnection`. The real `.env` file is ignored by git.

Connection string priority:

1. `DATABASE_URL`
2. `ConnectionStrings__DefaultConnection`
3. `ConnectionStrings:DefaultConnection` in `appsettings.json`

Railway commonly provides:

```powershell
$env:DATABASE_URL = "postgresql://username:password@host:port/database"
```

The backend converts that URL into the Npgsql format automatically:

```text
Host=host;Port=port;Database=database;Username=username;Password=password;SSL Mode=Require;Trust Server Certificate=true
```

You can also set an Npgsql connection string directly:

```powershell
$env:ConnectionStrings__DefaultConnection = "Host=localhost;Port=5432;Database=books;Username=postgres;Password=postgres"
```

Do not commit real Railway credentials.

```powershell
cd C:\Users\User\Desktop\Books\backend\Books.Api
dotnet restore
dotnet tool restore
dotnet ef database update
dotnet run
```

If `dotnet ef` is not installed globally, run the local tool instead:

```powershell
dotnet tool run dotnet-ef database update
```

During the current MVP stage, the API also runs EF Core migrations and seed data on startup.

Default HTTPS URL from `Properties\launchSettings.json`:

```text
https://localhost:7078
```

Swagger UI:

```text
https://localhost:7078/swagger
```

The API enables CORS policy `AllowAngularDevClient` for:

```text
http://localhost:4200
http://127.0.0.1:4200
```

## Frontend

```powershell
cd C:\Users\User\Desktop\Books\frontend\books-ui
npm install
ng serve
```

Open:

```text
http://localhost:4200
```

The Angular API base URL is configured in:

```text
src\environments\environment.ts
```

Current value:

```ts
apiBaseUrl: 'http://localhost:5199/api/v1'
```

The backend also exposes HTTPS at `https://localhost:7078`. The frontend uses HTTP by default so the browser can call the local API without a self-signed certificate prompt.

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
- `GET /api/v1/ledgers/{ledgerId}/reports/trial-balance`
- `GET /api/v1/ledgers/{ledgerId}/reports/general-ledger?accountId={accountId}`
