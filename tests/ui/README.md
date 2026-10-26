# UI smoke tests

Playwright smoke tests for the preview app. They exercise the full-page record surface (journal, sales,
purchase, payment, new/save/delete), deep links and Back.

The API must run in Development with the local issuer and a migrated PostgreSQL database:

```powershell
# from Books
$env:ConnectionStrings__Books="Host=127.0.0.1;Port=5432;Username=User;Database=books"
$env:ASPNETCORE_ENVIRONMENT="Development"
dotnet run --project src/Books.Api --urls http://127.0.0.1:5180
```

Install the browser driver once (no browser download is needed when Chrome or Edge is installed):

```powershell
cd tests/ui
npm install
```

Run:

```powershell
node record-surface.js
```

Environment:

| Variable | Meaning |
| --- | --- |
| `BOOKS_BASE` | API/app URL, default `http://127.0.0.1:5180` |
| `BOOKS_TOKEN` | Use an existing dev token instead of asking `/dev/token` |
| `BOOKS_CHROME` | Chromium-based browser executable; otherwise a Playwright cache or Chrome/Edge is used |
| `BOOKS_UI_SHOTS` | Directory for screenshots (optional) |

The test creates a fresh demo organization through `/dev/demo`, so it does not depend on other data. It posts
one small journal and deletes it again.
