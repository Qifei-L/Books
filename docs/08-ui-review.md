# UI review notes (2026-10-25)

Where the preview app stands after the shell, Sales, Purchases, Cash and bank, Inventory, Payroll, Fixed
assets, Journals, Business parties, the chart and tax settings, and the shared list work, what was decided on
the way, and what is deliberately still missing. Branch `ui/sales-pages`, pull request #98 against `main`. The
app change is confined to `src/Books.Api/wwwroot/app`: `app.js`, `index.html`, `app.css`, `forms.js`,
`setup.js`, `inventory.js`, `stock.js`, `payroll.js`, `list.js`.

## Shape

| Level | What it is |
| --- | --- |
| Shell | A fixed bar (organization, company, period, user), a grouped sidebar, one scrolling page with a title and a one-line purpose, and the page action at the right of that title |
| Modules | A module opens into its own pages and stays open while one of them is shown. Sales: Invoices, Credit notes, Reports, Settings. Purchases: Invoices, Debit notes, Reports. Cash and bank: Transactions, Statements. Inventory: Stock on hand, Adjustments, Transfers, Assemblies. Fixed assets: Assets, Depreciation runs. Payroll: Pay runs, Employees |
| Pages | One list, one job. `PAGES` holds the title, the purpose and the render function; a page renders the first time it is opened and a save re-reads only the open page |
| Lists | Two renderers: `renderDocList` for the eleven document lists, and the simpler `renderSimpleList` for master data. `DOC_KINDS` says the path or loader, the party, the amount columns, the extra columns, the row cells and which dialog opens; `SIMPLE_KINDS` says the columns and the form a row opens |
| Records | Business parties. A business party is the one record behind both a customer and a supplier |
| Books and tax | Chart of accounts holds the accounts and their bank/cash details; Tax return holds the month's return and the tax codes behind it; Payroll and Journals are document lists |

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
(Open, Print PDF, Edit, Delete); sticky header and totals row. The bank list is the exception: it works on
one bank account at a time (the account is a required choice, not an "all accounts" filter), and money in
and money out are two columns rather than one amount. A receipt is In, a payment is Out, a transfer is Out on
its sending account and In on its receiving one; a cross-currency transfer arrives as its to-amount in the
receiving account's currency. From Manager.io: sortable headers, a totals row that ends with the count, and
a minimum of chrome.

Master data is a simpler list: no period control, no tabs and no totals row. Business parties have a role
filter, a status filter, search and sortable columns; the footer counts the records and says a row opens its
form. The business party is the one record behind both a customer and a supplier, which is why the page is
not called "Customers and suppliers".

Inventory documents are three more lists: Stock adjustments show their items and Value, Stock transfers show
the two locations and their Items and Quantity (no currency), and Assemblies show the made item, its
Quantity, Components and Value. None has Posted/Draft tabs: a draft adjustment carries its status in the row
and the draft warning, a transfer and an assembly are posted when saved. The rows open a detail, and the row
menu still offers Delete; adjustments also have Edit and the Stock adjustment / Stock count menu.

The last document lists are Journals, Pay runs and Depreciation runs. Journals show their revision, amount
and status; a source journal opens its document and offers no Edit, a manual one does. Pay runs show the
employee count, currency and payroll totals, and open an inspector below the table with the employee lines,
search and paging. Depreciation runs are one row per month with a Total; the asset register stays on Assets
and the run opens its detail.

Payroll also has an Employees page. There is no separate employee master: the page groups the employee lines
of every imported pay run by code, shows the latest period, gross, net and currency, and opens that person's
pay history. Search filters by code or name; there is no New employee because the payroll system owns the
people.

## Decisions worth knowing

- **A sales invoice opens a trail, not a dead end.** The invoice detail has Related links to the customer
  statement, its journal, the receipts and payments whose line names it, the credit notes that correct it,
  and the tax return for its month. A related document opens in the same dialog with Back; closing the dialog
  clears the trail. The "received" rows come from payment/receipt lines that carry `invoice_id`; there is no
  allocation table, so a receipt left on the party balance does not appear against an invoice.
- **Bank transactions are one list for one account.** Payments, receipts and transfers answer one question —
  what moved through this bank account — so they share a page rather than three. The bank account is a
  required choice, never "all accounts": amounts, In/Out totals and the reconciliation all stay in that
  account's currency. The type is a column and a filter; the bank cell names the other side of a transfer;
  the category is an account, an invoice settled, the party's balance, or Transfer in/out. Money in and money
  out are separate columns, not Dr/Cr: a receipt is In, a payment Out, and a transfer Out on the sending
  account and In on the receiving one. New opens a menu of the three documents. The page has no Posted/Draft
  tabs: the status is in the row, the line above the table says how many drafts are not in the books, and
  the totals count only what is posted. This is the list component's `tabs: false` variant.
- **A bank amount keeps its own currency.** The account selection fixes the currency for the page, so each
  amount is formatted in it; a cross-currency transfer's incoming side uses the receiving account's currency
  and the to-amount of the transfer, not the sending amount.
- **Statements is the reconciliation page.** One bank account at a time: as-of date, the statement's
  closing balance, book and cleared balances, pending postings that can be marked cleared, and the imported
  statements. A statement's unmatched lines are drafts to Suspense and are posted from the same page. Bank
  statements still arrive through MCP; there is no file parser or import wizard (owner decision).
- **Bank accounts are chart accounts.** The separate Cash and bank > Settings page and the old Records >
  Bank accounts page are gone. The chart of accounts shows the bank details in a column and its account form
  creates or edits the account and its bank details together, so a bank or cash account is set up where every
  other account is. Only an Assets account can carry bank details.
- **Tax codes live with the tax return.** The separate Records > Tax codes page is gone: the month's return
  and the codes that feed it are one page, because a rate or account change is made next to the figures it
  changes. Administrators still get New tax code and the code form from that page.
- **Inventory is four pages.** Stock on hand keeps the report, the items and the locations (with stock by
  location); the three documents have their own pages. Stock adjustments are the only ones with drafts, so
  they show the draft warning; transfers and assemblies are posted when saved. Quantity and Value use
  ordinary cells instead of the document lists' net/VAT/gross: a transfer's amount column is Quantity, and
  an assembly carries both a Quantity and a Value.
- **Journals are the posting list.** One row per journal with its revision, amount and status; no tabs.
  A journal created by a document opens that document and does not offer Edit; a manual journal does.
- **Pay runs are imported, not entered here.** The list shows the employee count, currency and the payroll
  totals; the inspector below it opens from a row with the employee lines, search and paging. There is no New
  pay run button because the payroll system owns that entry. The Employees page is built from those imported
  lines, one row per code, with the last period and pay and the full history; it is not a separate editable
  master.
- **Depreciation runs have their own page.** The asset register stays on Assets; Depreciation runs lists one
  posted journal per month, with a Total and a detail of the assets it depreciated.
- **Customers and suppliers are business parties.** One record serves both roles, because a company can be
  both; the page, the new button and the form call it a business party, while the role checkboxes still say
  Customer and Supplier. The list is the first user of the simple master-data variant: search, role and
  status filters, sortable columns, a row count, and a row opens the form. There is no totals row because a
  party is not a document.
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
  series are Sales > Settings. Global Settings keeps the ledger and the organization. Bank details are part
  of the Chart of accounts, and tax codes are part of the Tax return.

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
- The drilldown starts at a sales invoice or credit note. The purchase invoice, a receipt opened from the
  bank list, and a stock movement from an invoice line do not yet open the same trail; the detail is a dialog,
  not a URL, so a trail cannot be linked or survive a refresh. The invoice trail loads payments, receipts and
  credit notes whole and filters them in the browser; it needs invoice-scoped endpoints when volume grows.
- Grouping by month and a selection with an action bar exist; what is missing is the account of what a
  batch action is allowed to do, and the Open/Paid status that needs `open_balance`. Bank transactions
  hides Group by month: its type, bank and party filters already do the narrowing.
- Bank statements are imported through MCP, not a file parser or import wizard (owner decision). The
  Statements page can clear postings and post the drafts an import created, but it cannot read a bank file.
- Business parties edit through the row's form, not by typing into table cells.
- The Chart of accounts form creates and edits accounts and their bank details, but groups and usages still
  have no editor, and an unused account can only be deleted through the API. Tax codes keep their own form
  inside the Tax return page.
- Inventory items, categories and locations are still on the Stock on hand page, not separate master-data
  pages; an item's category has no editor yet. A bill of materials is still set through the API or AI (the
  handoff's open question); assemblies can be made but not edited, and deleting one removes its journal.
- Payroll employees are read from the imported pay run lines; there is no employee master record to create
  or edit in Books, and a person with no pay run does not appear. The Fixed asset register is still its own
  plain table (the asset form edits an asset); only the depreciation runs moved to the list component.
- Groups, usages and members still have no form/list page; business parties, tax codes, items and locations
  are edited where they are shown.

## Where the code lives

`wwwroot/app/list.js` holds everything the lists share. `DOC_KINDS` and `renderDocList` cover the document
lists: the period presets, filter state, column choice and order, sorting, paging, grouping, selection and the
row menu. A kind can also supply a `load` (one endpoint or several), `facets` (the type and bank account
filters), `extraColumns` and `cells` (a category, a bank account, a quantity, a component list), `pathOf` and
`nounOf` (a merged list deletes and labels the right document), `amountHeader` and `formatTotal` (a quantity
instead of money), `noEdit` and `editWhen` (a document that is posted on save, or a source journal),
`after` for an inspector under the table, and `tabs: false` for a list that shows posted and draft together.
`SIMPLE_KINDS` and `renderSimpleList` are the master-data variant: fixed columns, search, a role or status
filter, sortable headers, a row count and a form from the row.

`app.js` keeps the page registry, the dialogs and the reports, plus the Statements page
(`renderBankStatements`), the Chart of accounts and its account form (`renderChart`, `accountForm`), the
Tax return with its tax code section (`renderTax`), and the detail trail (`drillTo`, `trailBack`) that the
sales invoice uses. `inventory.js` keeps the Stock on hand page, the stock
adjustment form and the item and movement dialogs; `stock.js` keeps locations, stock by location, the
transfer and assembly forms and their detail dialogs; `payroll.js` keeps the pay run inspector and the
Employees page (built from the pay run lines). `setup.js` keeps the settings pages, the tax code form, the
fixed asset register and the depreciation run form/detail. `forms.js` keeps the document forms, including the
payment/receipt category picker and the business party form. A new document list adds one `DOC_KINDS` entry
and one page entry; a new master-data list adds one `SIMPLE_KINDS` entry and one page entry.

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
- The bank list: the bank account is a required facet, so the page never adds accounts or currencies
  together; the type filter narrows the rows. A transfer appears once per account: Out on its sending side
  and In on its receiving side, with the other account named in the bank cell. Search spans all three
  document kinds, the totals row has separate In and Out totals, and it counts posted documents only.
- Business parties: search looks in the name, tax ID, contact, email, phone and address; the role and status
  filters combine; the header sorts; the row count is not a total. A save or delete clears the cached list
  and re-reads the open page, like the document lists.
- Inventory: the three documents are ordinary `DOC_KINDS` entries with `tabs: false`. Adjustments show
  Items and Value and keep the draft warning; transfers show Items and a Quantity total with no currency;
  assemblies show Quantity, Components and Value. A row opens its detail; adjustments also offer Edit,
  while transfers and assemblies are posted on save and only delete. Items and locations stay on Stock on
  hand with their forms and stock by location.
- Sales invoice: the row opens the invoice and its Related section. Customer statement, Journal, a settlement
  or a credit note opens in the same dialog with a Back bar; the tax-return button closes the dialog and
  opens that month. The credit-note detail offers Original invoice the same way. The trail is in memory only
  and clears when the dialog closes.
- Journals: the list has no tabs and a Revision column; `editWhen` hides Edit on source journals, which open
  the document instead. Pay runs show the currency and totals, with the inspector under the table; the
  inspector keeps its own employee search and paging. Depreciation runs are a period row with a Total and a
  detail; the register stays on Assets. Employees loads the pay run lines once, groups by code, and filters
  the rendered rows in place so typing does not re-fetch or lose focus.
- `showTab` and `PAGES.actions`: header actions are remembered per page so returning to a rendered page
  restores them without rendering again.
- `openRowMenu`: closes on the next click elsewhere; Delete sends `If-Match` through `write`, and a merged
  list takes the document's own path for the request.
- Statements: clearing is a `POST` of the journal ids and changes no posting; the statement's own drafts are
  posted from the same page. A cleared posting cannot be changed until it is uncleared (P9).
- Chart of accounts: a row opens the account form; the form creates the account and its bank details, and
  the read-only GL button still opens the account's detail. The bank fields appear only for Assets accounts,
  and an existing bank account's type and currency are fixed.
- Tax return: the month control reloads the return and the tax code section together; a code row opens the
  tax code form for administrators. A missing return still leaves the tax codes usable.
- Sticky header and totals depend on `.page` being the only scrolling container; do not add `overflow` to
  `.page-body` or to `.table-scroll` — a scroll container there captures `position: sticky` and the header
  scrolls away.
- A narrow window drops the wide columns (description, other tax, currency, KHR, reference) at 1300/1120/960 px
  instead of scrolling the table sideways. The bank list keeps type, party, bank, In/Out and status; category
  drops at 1300 and the numeric columns narrow at 960. The chart drops system purpose at 1120 and narrows bank
  details at 960.

## Next slices, as agreed with the owner (2026-10-25)

The list component in `list.js` now covers every document list: the sales and purchase lists, bank
transactions, the three inventory documents, journals, pay runs and depreciation runs — plus the business
parties simple list and the Payroll Employees page. What is left is master data, not documents:

| Module | Pages to build | What `list.js` needs first |
| --- | --- | --- |
| Master data | Groups, usages and members (accounts and bank details are in the Chart of accounts; business parties and tax codes are done) | The simple list now exists; each remaining page needs its own form and, if wanted, typing in cells |

Order agreed: Cash and bank, Business parties, Inventory, the chart/bank and tax merges, the remaining
document lists and the Payroll employees list are done. What remains is groups, usages and members.