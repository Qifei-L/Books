# UI review notes (2026-10-25)

Where the preview app stands after the shell, Sales, Purchases and list work, what was decided on the way,
and what is deliberately still missing. Branch `ui/sales-pages`, pull request #98 against `main`.
Only `src/Books.Api/wwwroot/app` changed: `app.js`, `index.html`, `app.css`, `forms.js`, `setup.js`.

## Shape

| Level | What it is |
| --- | --- |
| Shell | A fixed bar (organization, company, period, user), a grouped sidebar, one scrolling page with a title and a one-line purpose, and the page action at the right of that title |
| Modules | A module opens into its own pages and stays open while one of them is shown. Sales: Invoices, Credit notes, Reports, Settings. Purchases: Invoices, Debit notes, Reports |
| Pages | One list, one job. `PAGES` holds the title, the purpose and the render function; a page renders the first time it is opened and a save re-reads only the open page |
| Lists | One renderer (`renderDocList`) for all four document lists; `DOC_KINDS` says the path, the party, the amount columns and which dialog opens |

## List mechanics

The toolbar reads in the order the filters are used: period first (a preset, whole fiscal year by default,
so a document dated later in the year is never hidden), then the party (a combobox — a select stops working
at a few dozen parties and cannot show a tax ID), then search, which filters as it is typed. View controls
(extra columns, density) sit at the right.

The row's identity is the party, with the date and (unless it has a column of its own) the reference under it;
the system number stays out of the list. Columns and rows per page are chosen in the toolbar — 25, 50, 100 or
all — and the totals row always sums every filtered document, not the visible page. Visible columns, their
order, density and rows per page are kept in localStorage (`books.ui.v1`): a reading preference, not data.

The table borrows from QuickBooks: the number, date and reference are one two-line cell; one amount column
headed with its currency; a single status badge; days late inline on the due date; a row menu on hover
(Open, Print PDF, Edit, Delete); sticky header and totals row. From Manager.io: sortable headers, a totals
row that ends with the count, and a minimum of chrome.

## Decisions worth knowing

- **One time control.** The period preset drives the list; the report pages keep their own as-of date,
  because an aging figure and a list range answer different questions.
- **Drafts never count.** No totals row, no overdue figure, no "showing x of y" that includes them: a draft
  is not in the ledger, so it has no total. The draft tab lists them and says so. They are the same documents, unposted; the tab says they are not in the
  ledger and block filing. A draft outside the period is counted with a one-click widen rather than vanishing.
- **Amounts are formatted in one place.** The API sends amounts as strings; `money()` parses before
  formatting, and uses the currency's minor units (USD 2, KHR 0).
- **Ids are per list.** Every list once reused `docs-search` and friends, so `getElementById` resolved to the
  first, hidden page and search filtered nothing.
- **Module settings live with the module.** `Settings > Invoicing` was deleted: the seller block and number
  series are Sales > Settings. Global Settings keeps the ledger and the organization.

## Not done, on purpose

- The list payload carries every document with its lines on purpose (an AI reconciles against them); it is
  large — 5.9 MB for 5000 documents — so responses are Brotli/gzip compressed at the optimal level
  (`Program.cs`), which brings the transfer to about 630 KB.
- Filtering is client-side. Fine for a company's invoices; it has to move to the API when a list grows and
  paging arrives, together with a `(ledger_id, date)` index.
- No hash routing, so a filter or a page cannot be linked or restored after a refresh.
- Status stops at Draft / Posted / Error. Open / Paid / Partially paid need `allocated_total` and
  `open_balance` on `SalesInvoiceDto`, computed from `allocations`.
- Grouping by month and a selection with an action bar exist; what is missing is the account of what a
  batch action is allowed to do, and the Open/Paid status that needs `open_balance`.

## Running it

.NET 10 SDK and PostgreSQL 17. Without Docker, an ordinary PostgreSQL server works; point
`BOOKS_TEST_POSTGRES` at an admin connection string for tests.

```
ConnectionStrings__Books="Host=127.0.0.1;Port=5432;Username=<user>;Database=books"
Logging__EventLog__LogLevel__Default=None
ASPNETCORE_ENVIRONMENT=Development
dotnet run --project src/Books.Api --urls http://127.0.0.1:5180
```

`Logging__EventLog__LogLevel__Default=None` matters outside an elevated session: the default EventLog
provider throws "Access is denied", and that exception replaces the response, so any request that logs a
warning returns 500. It once made `/dev/demo` die halfway and `/items` look broken.

Then open `/app/`, sign in with the local test issuer (subject `me`), and press **New demo** for a year of
sample data.

## What to look at in review

- `renderDocList` state: `docFilters`, `docTab`, `docSort`, `docCols` are per kind; `chooseLedger` clears
  `state.rendered`, `refreshLedger` re-reads only the open page. Watch for a page showing another page's data.
- `showTab` and `PAGES.actions`: header actions are remembered per page so returning to a rendered page
  restores them without rendering again.
- `openRowMenu`: closes on the next click elsewhere; Delete sends `If-Match` through `write`.
- Sticky header and totals depend on `.page` being the only scrolling container; do not add `overflow` to
  `.page-body` or to `.table-scroll` — a scroll container there captures `position: sticky` and the header
  scrolls away.
- A narrow window drops the wide columns (description, other tax, currency, KHR, reference) at 1300/1120/960 px
  instead of scrolling the table sideways.
