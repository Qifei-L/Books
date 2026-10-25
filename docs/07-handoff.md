# Handoff (2026-10-25)

What is built, what is not finished, and the questions still open. Read [05-decisions](05-decisions.md) for every owner
decision; this page lists only what is left.

## Where things stand

Merged to main through #95. Inventory alignment with Manager.io / QuickBooks / Xero (items 5–8, 12–14, 16, 17, 19 of the
checklist) is on main:

| Area | PR | API | App (wwwroot/app) | MCP |
| --- | --- | --- | --- | --- |
| Item accounts, lock once used | #88 | yes | yes | `list_items` |
| Item code, sales and purchase details | #89 | yes | yes | lines default from the item |
| Stock adjustments like a journal, revaluation | #90 | yes | yes | `save_stock_adjustment_draft`, `list_stock_adjustments` |
| Item movements, negative stock with warning | #91 | yes | yes | `get_item_movements` |
| Units of measure | #92 | yes | yes (unit on sales and purchase lines) | `unit` on draft lines |
| Locations and stock transfers | #93, #95 | yes | yes (Inventory tab) | **no** |
| Assemblies from a bill of materials | #94, #95 | yes | yes (Inventory tab, "Make") | **no** |

## Not finished

### 1. MCP tools for locations, transfers and assemblies

The HTTP API exists (`src/Books.Api/Http/LocationEndpoints.cs`, `AssemblyEndpoints.cs`); the MCP side does not. Add, following
`src/Books.Api/Mcp/BooksReadTools.cs` and the draft tools in `BooksSalesTools.cs`:

- `list_locations`, `get_stock_by_location` (`/reports/inventory/locations`)
- `list_stock_transfers`, `save_stock_transfer`
- `list_assemblies`, `save_assembly`
- `location_id` on the line records of `save_sales_drafts`, `save_purchase_drafts` and `save_stock_adjustment_draft`
  (the HTTP line requests already take `location_id`; the MCP records pass `null`)
- A way for AI to set an item's bill of materials (`components` on the item). There is no MCP item write tool at all yet
  (`save_item` would cover accounts, prices, units and components).

Update the tool-list test (it checks the names in alphabetical order).

### 2. Location and class on lines in the app

Sales and purchase forms now show Class and Location pickers; journal lines show both, plus party and tax. The stock
adjustment, assembly, payment, fixed-asset and pay-run forms still have no Class/Location picker. Lines without a class
are unclassified; lines without a location use the default location.

### 3. Bill of materials in the app (question open)

The item form has no BOM editor; components are set through the API (or AI, once item 1 is done). Owner to decide
whether the app needs one (see #95).

### 4. Smaller items from PR questions

Defaults were applied; owner may still change them:

- #87: module grouping in the sidebar, and the first screen (Sales now; a dashboard later?).
- #88: a "change inventory account" action that also moves the balance (now: locked once used, move by manual journal).
- #89: a separate KHR price per item (now: prices in base currency only; KHR lines give their own price). Tax codes are
  filled in from the item by the app and AI, not the server.
- #90: quantity and amount entered together are taken as entered (changes the remaining average).
- #91: cost while stock is negative is the last average, else purchase price, else 0, and is not corrected later.
  Warnings are returned on save only.
- #92: stock adjustments and counts are in the base unit only; units on adjustment lines?

## Decided for later

- **Branches / locations at company level.** A location is now both the stock place and an accounting dimension on
  journal, sales and purchase lines. Branch-level books (a separate P&L/BS per branch) are still later. Stock recorded
  before any location existed belongs to the default location (warehouse).

## Working on this repo

- Tests need PostgreSQL (never EF InMemory for anything with transactions):
  `BOOKS_TEST_POSTGRES="Host=localhost;Port=54329;Username=postgres;Database=postgres" dotnet test --solution Books.slnx`
- Migrations: `dotnet tool run dotnet-ef migrations add <Name> -p src/Books.Infrastructure -s src/Books.Infrastructure`
- Development uses the local test issuer only; no real credentials or keys in code. Do not deploy or create cloud
  resources.
- Small PRs; business questions not covered by the docs go at the top of the PR description for the owner.

## Journal dimensions (2026-10-25)

Branch `feat/journal-dimensions` adds the first slice of class/location dimensions and manual journal control:

| Area | Done | Still missing |
| --- | --- | --- |
| Journal form | Party, class, location, tax code and tax direction; gross tax split | Withholding on manual journals (use payment/payroll) |
| Sales / purchase forms | Class and location pickers on each line | Payment, stock adjustment, assembly, fixed-asset and pay-run lines |
| Classes | Ledger-scoped master data, API and Classes page | Shared classes across an organization (M14) |
| GL Detail | Class, location and party names per line; class/location filters | — |
| TB / BS / P&L | Class/location filters in the API, app and Excel | — |
| MCP | list_classes, list_locations, dimensions on save_journal; filters on the four reports | class/location on sales, purchase and stock-adjustment draft line records |
| Aging | Manual AR/AP lines count; aging still equals the control balance | Manual AR/AP permission is not restricted to administrators yet |

Rules: class and location are reporting dimensions only; they never change the account or the subledger. A manual journal may post AR/AP with a party, but it still cannot control inventory, fixed assets or payroll.

