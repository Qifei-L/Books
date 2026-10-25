# UI review notes (2026-10-25)

Where the preview app stands after the shell, Sales, Purchases, Cash and bank and the shared list work, what
was decided on the way, and what is deliberately still missing. Branch `ui/sales-pages`, pull request #98
against `main`. The app change is confined to `src/Books.Api/wwwroot/app`: `app.js`, `index.html`, `app.css`,
`forms.js`, `setup.js`, `list.js`.

## Shape

| Level | What it is |
| --- | --- |
| Shell | A fixed bar (organization, company, period, user), a grouped sidebar, one scrolling page with a title and a one-line purpose, and the page action at the right of that title |
| Modules | A module opens into its own pages and stays open while one of them is shown. Sales: Invoices, Credit notes, Reports, Settings. Purchases: Invoices, Debit notes, Reports. Cash and bank: Transactions, Statements, Settings |
| Pages | One list, one job. `PAGES` holds the title, the purpose and the render function; a page renders the first time it is opened and a save re-reads only the open page |
| Lists | One renderer (`renderDocList`) for the five document lists; `DOC_KINDS` says the path or loader, the party, the amount columns, the extra columns, the row cells and which dialog opens |

## List mechanics

The toolbar reads in the order the filters are used: period first (a preset, whole fiscal year by default,
so a document dated later in the year is never hidden), then the type and bank account on the bank list, then
the party (a combobox — a select stops working at a few dozen parties and cannot show a tax ID), then search,
which filters as it is typed. View controls (extra columns, density) sit at the right.

The row's identity is the party, with the date and (unless it has a column of its own) the reference under it;
for a transfer it is the description, because a transfer has no party. The system number stays out of the
list. Columns and rows per page are chosen in the toolbar — 25, 50, 100 or all — and the totals row always
sums every filtered document, not the visible page. Visible columns, their order, density and rows per page
are kept in localStorage (`books.ui.v1`): a reading preference, not data.

The table borrows from QuickBooks: the number, date and reference are one two-line cell; one amount column
headed with its currency; a single status badge; days late inline on the due date; a row menu on hover
(Open, Print PDF, Edit, Delete); sticky header and totals row. On the bank list the amount column has no
single currency in its header, because a payment is in its bank's currency and a transfer in its sending
account's; the totals row groups them by currency. From Manager.io: sortable headers, a totals row that ends
with the count, and a minimum of chrome.

## Decisions worth knowing

- **Bank transactions are one list.** Payments, receipts and transfers answer one question — what moved
  through cash and bank — so they share a page rather than three. The type is a column and a filter; a
  transfer's from and to accounts are its bank cell; the category is an account, an invoice settled, the
  party's balance, or "Transfer". New opens a menu of the three documents. The page has no Posted/Draft
  tabs: the status is in the row, the line above the table says how many drafts are not in the books, and
  the totals count only what is posted. This is the list component's `tabs: false` variant.
- **A bank amount keeps its own currency.** A payment takes the currency of the bank account it touches,
  a transfer the sending account's; `money()` formats each row and the totals group by currency instead of
  adding different currencies together.
- **Statements is the reconciliation page.** One bank account at a time: as-of date, the statement's
  closing balance, book and cleared balances, pending postings that can be marked cleared, and the imported
  statements. A statement's unmatched lines are drafts to Suspense and are posted from the same page. Bank
  statements still arrive through MCP; there is no file parser or import wizard (owner decision).
- **Cash and bank settings hold the bank accounts.** The old Records > Bank accounts page is gone. New bank
  account can either mark an existing Assets account as a bank or create that account in the chart in the
  same form, so setting up a bank no longer needs a detour through the API.
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
  series are Sales > Settings. Global Settings keeps the ledger and the organization. Cash and bank >
  Settings keeps the bank accounts.

## Not done, on purpose

- The list payload carries every document with its lines on purpose (an AI reconciles against them); it is
  large — 5.9 MB for 5000 documents — so responses are Brotli/gzip compressed at the optimal level
  (`Program.cs`), which brings the transfer to about 630 KB.
- Filtering is client-side. Fine for a company's invoices; it has to move to the API when a list grows and
  paging arrives, together with a `(ledger_id, date)` index. The bank list merges three endpoints and then
  filters in the browser; it will want one paged transactions endpoint before a company has thousands.
- No hash routing, so a filter or a page cannot be linked or restored after a refresh.
- Status stops at Draft / Posted / Error. Open / Paid / Partially paid need `allocated_total` and
  `open_balance` on `SalesInvoiceDto`, computed from `allocations`.
- Grouping by month and a selection with an action bar exist; what is missing is the account of what a
  batch action is allowed to do, and the Open/Paid status that needs `open_balance`. Bank transactions
  hides Group by month: its type, bank and party filters already do the narrowing.
- Bank statements are imported through MCP, not a file parser or import wizard (owner decision). The
  Statements page can clear postings and post the drafts an import created, but it cannot read a bank file.

## Where the code lives

`wwwroot/app/list.js` holds everything the document lists share: the kind definitions (`DOC_KINDS`), the
period presets, filter state, column choice and order, sorting, paging, grouping, selection and the row menu.
A kind can also supply a `load` (one endpoint or several), `facets` (the type and bank account filters),
`extraColumns` and `cells` (a category, a bank account, a type), `pathOf` and `nounOf` (a merged list deletes
and labels the right document), and `tabs: false` for a list that shows posted and draft together.

`app.js` keeps the page registry, the dialogs and the reports, plus the Statements page
(`renderBankStatements`). `setup.js` keeps the records and settings pages and the transfer and bank account
forms. `forms.js` keeps the document forms, including the payment/receipt category picker. A new module that
wants the same list adds one `DOC_KINDS` entry and one page entry; nothing else.

## Running the tests on this machine

`dotnet test` cannot run here (the test platform talks to the runner over a named pipe, which the sandbox
refuses). Run the test executable directly instead:

```
BOOKS_TEST_POSTGRES="Host=127.0.0.1;Port=5432;Username=<user>;Database=postgres"
Logging__EventLog__LogLevel__Default=None
TMP=<a writable folder>   TEMP=<the same folder>
tests/Books.Tests/bin/Debug/net10.0/Books.Tests.exe
```

`BOOKS_TEST_POSTGRES` avoids Docker; the EventLog switch stops every warning from becoming a 500; TMP/TEMP
must be writable or the PDF and Excel tests fail with `InternalServerError` from QuestPDF. With all three:
295 passed, 0 failed.

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
- The bank list: the type and bank account facets combine, the party filter still applies only to payments
  and receipts, search spans all three document kinds, and the totals row groups by currency and counts
  posted documents only. A transfer's row identity is its description; its bank cell carries both accounts.
- `showTab` and `PAGES.actions`: header actions are remembered per page so returning to a rendered page
  restores them without rendering again.
- `openRowMenu`: closes on the next click elsewhere; Delete sends `If-Match` through `write`, and a merged
  list takes the document's own path for the request.
- Statements: clearing is a `POST` of the journal ids and changes no posting; the statement's own drafts are
  posted from the same page. A cleared posting cannot be changed until it is uncleared (P9).
- Cash and bank Settings: creating a bank account can create its Assets account first; if the second write
  fails, the unused account stays in the chart and the form says why.
- Sticky header and totals depend on `.page` being the only scrolling container; do not add `overflow` to
  `.page-body` or to `.table-scroll` — a scroll container there captures `position: sticky` and the header
  scrolls away.
- A narrow window drops the wide columns (description, other tax, currency, KHR, reference) at 1300/1120/960 px
  instead of scrolling the table sideways. The bank list keeps type, party, bank, amount and status, and
  drops category at 1120.

## Next slices, as agreed with the owner (2026-10-25)

The list component in `list.js` now covers the four sales and purchase lists and the bank transactions
list. The owner's picks for what to convert next, and what each one needs from the component:

| Module | Pages to build | What `list.js` needs first |
| --- | --- | --- |
| Inventory | Stock adjustments, Stock transfers, Assemblies (the three documents); Items, categories and locations stay master data | A page without Posted/Draft tabs; cells for quantity and value instead of net/VAT/gross |
| Payroll | Pay runs, plus an employees list the owner asked for | A page without tabs; a row count column |
| Fixed assets | Depreciation runs; the asset register | No tabs; a period column |
| Journals | Journals with drafts and posted, editable | No tabs; a revision column |
| Master data | Accounts, groups, usages, parties, tax codes, bank accounts, members | A "simple list" variant: no tabs, no totals, inline edit |

Order agreed: Inventory, then the rest. Cash and bank is done: its list needed a custom loader, a category
column, row cells that are not amount keys, and a variant without the Posted/Draft tabs, and those now exist.
What remains for the rows below is mostly the quantity/value cells and the "simple list" variant.