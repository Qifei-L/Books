// The document list: everything the list pages share — kinds, period presets, filters, columns, sorting,
// paging, grouping, selection and the row menu. A page supplies a kind; DOC_KINDS says where its data
// comes from and what its rows mean.
//
// Three hooks turn a kind into something that is not a sales or purchase invoice:
//   load          fetch the list from one endpoint or several (bank payments come with their banks)
//   cells         a row's value where the DTO key is not the display text (a bank account id, a category)
//   extraColumns  a column that is not one of the document's amounts (bank account, category, fee)
// A kind with `tabs: false` lists posted and draft documents together, with the status in the row and
// totals that count only what is in the ledger.

// Document lists. The layout follows Manager.io for the toolbar and QuickBooks for the table: a two-line
// document cell, one amount column, a status badge, a row menu on hover, sticky header and totals, and a
// density switch. Sales, purchases and the bank documents share the renderer.
const BANK_HIDDEN = ["due", "other_tax_total", "fx", "khr", "lines_count", "party_tax_id", "party_terms", "journal"];

const DOC_KINDS = {
  invoice: {
    path: "sales-invoices", container: "sales-invoices", formKey: "invoice", side: "sale",
    noun: "sales invoice", plural: "sales invoices", prefix: "SI", newLabel: "New sales invoice",
    partyKey: "customer_id", partyFilter: (p) => p.is_customer, partyLabel: "Customer",
    third: "due", extra: "overdue",
    amounts: [{ key: "net_total", label: "Net" }, { key: "tax_total", label: "VAT" }, { key: "gross_total", label: "Gross" }],
  },
  credit_note: {
    path: "credit-notes", container: "sales-credit-notes", formKey: "credit_note", side: "sale",
    noun: "credit note", plural: "credit notes", prefix: "CN", newLabel: "New credit note",
    partyKey: "customer_id", partyFilter: (p) => p.is_customer, partyLabel: "Customer",
    third: "ref", refLabel: "Credits", refPrefix: "SI", refPath: "sales-invoices", extra: "credits",
    amounts: [{ key: "net_total", label: "Net" }, { key: "tax_total", label: "VAT" }, { key: "gross_total", label: "Gross" }],
  },
  purchase_invoice: {
    path: "purchase-invoices", container: "purchases-invoices", formKey: "purchase_invoice", side: "purchase",
    noun: "purchase invoice", plural: "purchase invoices", prefix: "PI", newLabel: "New purchase invoice",
    partyKey: "supplier_id", partyFilter: (p) => p.is_supplier, partyLabel: "Supplier",
    third: "due", extra: "overdue",
    amounts: [{ key: "net_total", label: "Net" }, { key: "tax_total", label: "VAT" }, { key: "withholding_total", label: "WHT" }, { key: "payable_total", label: "Payable" }],
  },
  debit_note: {
    path: "debit-notes", container: "purchases-debit-notes", formKey: "debit_note", side: "purchase",
    noun: "debit note", plural: "debit notes", prefix: "DN", newLabel: "New debit note",
    partyKey: "supplier_id", partyFilter: (p) => p.is_supplier, partyLabel: "Supplier",
    third: "ref", refLabel: "Debits", refPrefix: "PI", refPath: "purchase-invoices", extra: "credits",
    amounts: [{ key: "net_total", label: "Net" }, { key: "tax_total", label: "VAT" }, { key: "withholding_total", label: "WHT" }, { key: "payable_total", label: "Payable" }],
  },
  // Cash and bank. Payments, receipts and transfers are one list for one bank account at a time. Money
  // coming in and money going out are separate columns, never Dr/Cr: a receipt is In, a payment is Out,
  // and a transfer is Out on its sending account and In on its receiving one. It has no Posted/Draft tabs:
  // the status is in the row, and the page says how many drafts are not in the books.
  transaction: {
    container: "bank-transactions", noun: "transaction", plural: "transactions", prefix: "",
    numberKey: "doc_no", newLabel: "New transaction",
    newMenu: [
      { label: "Payment", run: () => paymentForm("payment") },
      { label: "Receipt", run: () => paymentForm("receipt") },
      { label: "Transfer", run: () => transferForm() },
    ],
    partyKey: "party_id", partyFilter: (p) => p.active, partyLabel: "Party",
    hide: BANK_HIDDEN, extra: "none", tabs: false, hideGroup: true, tableClass: "bank", searchHint: "type, reference, party, amount",
    amounts: [{ key: "in", label: "In", always: true, w: "104px" }, { key: "out", label: "Out" }],
    amountWidth: "104px", statusWidth: "110px",
    columnOrder: ["type", "party", "date", "reference", "bank", "category", "in", "amount", "status", "description"],
    columnDefaults: ["description"],
    facets: [
      { key: "doc_kind", label: "Type", all: "All types", options: () => [
        { value: "payment", label: "Payment" }, { value: "receipt", label: "Receipt" }, { value: "transfer", label: "Transfer" },
      ] },
      { key: "bank_account_id", label: "Bank account", required: true, options: (c) => c.banks.map((b) => ({ value: b.account_id, label: `${b.code} ${b.name} · ${b.currency}` })) },
    ],
    extraColumns: {
      type: { label: "Type", always: true, sortable: true, w: "84px", value: (d) => d.type_label.toLowerCase() },
      bank: { label: "Bank account", always: true, sortable: true, w: "164px", value: (d) => bankLabel(d.bank_account_id).toLowerCase() },
      category: { label: "Category", always: true, sortable: true, w: "174px", value: (d) => transactionCategoryText(d).toLowerCase() },
    },
    cells: {
      type: (d) => `<span class="type-tag ${d.doc_kind}">${esc(d.type_label)}</span>`,
      party: (d, c) => transactionIdentity(d, c),
      bank: (d) => transactionBankCell(d),
      category: (d) => transactionCategoryCell(d),
      in: (d, c) => (Number(d.in) ? money(d.in, c.currencyOf(d)) : ""),
      amount: (d, c) => (Number(d.out) ? money(d.out, c.currencyOf(d)) : ""),
    },
    currencyOf: (d) => d.currency || state.ledger.base_currency,
    pathOf: (d) => d.doc_path,
    nounOf: (d) => (d.doc_kind === "transfer" ? "transfer" : d.doc_kind),
    summary: (list) => {
      const currency = list[0]?.currency ?? state.ledger.base_currency;
      const inTotal = list.reduce((s, d) => s + Number(d.in ?? 0), 0);
      const outTotal = list.reduce((s, d) => s + Number(d.out ?? 0), 0);
      return `${money(inTotal, currency)} ${currency} in · ${money(outTotal, currency)} ${currency} out`;
    },
    noData: (c) => (c.banks.length ? "" : "No bank or cash accounts yet. Add one in Books → Chart of accounts."),
    load: async () => {
      const [payments, receipts, transfers, banks, parties] = await Promise.all([
        api(`${ledgerUrl()}/payments`), api(`${ledgerUrl()}/receipts`), api(`${ledgerUrl()}/transfers`),
        api(`${ledgerUrl()}/bank-accounts`), api(`${ledgerUrl()}/parties`),
      ]);
      const currencyOf = (id) => banks.find((b) => b.account_id === id)?.currency ?? state.ledger.base_currency;
      const partyName = (id) => parties.find((p) => p.id === id)?.name ?? "";
      const paymentsAndReceipts = (docs, kind, path, label) => docs.map((d) => ({
        ...d, doc_kind: kind, doc_path: path, doc_no: d.payment_no, type_label: label,
        amount: d.total, currency: d.currency || currencyOf(d.bank_account_id),
        in: kind === "receipt" ? d.total : "0",
        out: kind === "payment" ? d.total : "0",
        direction: kind === "receipt" ? "in" : "out",
        party_label: partyName(d.party_id) || d.description || "",
      }));
      // A transfer is one document but two bank-account movements. The sending account sees Out, the
      // receiving account sees In; a cross-currency transfer arrives as to_amount in the receiving currency.
      const transferRows = transfers.flatMap((d) => {
        const sent = d.amount;
        const received = d.to_amount ?? d.amount;
        const base = { ...d, doc_kind: "transfer", doc_path: "transfers", doc_no: d.transfer_no, type_label: "Transfer", type: "transfer",
          party_id: null, party_label: d.description || "Bank transfer" };
        return [
          { ...base, bank_account_id: d.from_account_id, counterparty_account_id: d.to_account_id, direction: "out",
            amount: sent, currency: currencyOf(d.from_account_id), in: "0", out: sent },
          { ...base, bank_account_id: d.to_account_id, counterparty_account_id: d.from_account_id, direction: "in",
            amount: received, currency: currencyOf(d.to_account_id), in: received, out: "0" },
        ];
      });
      return {
        docs: [
          ...paymentsAndReceipts(payments, "payment", "payments", "Payment"),
          ...paymentsAndReceipts(receipts, "receipt", "receipts", "Receipt"),
          ...transferRows,
        ],
        banks, parties,
      };
    },
    searchText: (d) => [
      d.type_label, d.doc_kind === "transfer" ? "transfer in transfer out" : d.direction,
      d.description, bankLabel(d.bank_account_id), bankLabel(d.counterparty_account_id),
      d.doc_kind === "transfer" ? `${bankLabel(d.from_account_id)} ${bankLabel(d.to_account_id)} Transfer in Transfer out` : transactionCategoryText(d),
      ...(d.lines ?? []).map((l) => paymentLineText(l)),
    ].join(" "),
    open: (d, c) => (d.doc_kind === "transfer" ? showTransfer(d) : showPayment(d, c.nameOf)),
    edit: (d) => (d.doc_kind === "transfer" ? transferForm(d) : editPayment(d.doc_kind, d.id)),
  },
  // Stock adjustments and counts: no Posted/Draft tabs, status in the row. The Value column is money; the
  // Items column is the quantity side, which is not summed across units.
  stock_adjustment: {
    path: "stock-adjustments", container: "inventory-adjustments", noun: "stock adjustment", plural: "stock adjustments", prefix: "SA",
    numberKey: "adjustment_no", newLabel: "New adjustment",
    newMenu: [
      { label: "Stock adjustment", run: () => adjustmentForm() },
      { label: "Stock count", run: () => adjustmentForm(null, true) },
    ],
    filterParty: false, partyLabel: "Adjustment",
    hide: BANK_HIDDEN, extra: "none", tabs: false, hideGroup: true, searchHint: "SA, reference, item, description, value",
    amounts: [{ key: "total_value", label: "Value" }],
    columnOrder: ["party", "date", "reference", "lines", "amount", "status", "description"],
    columnDefaults: ["description"],
    extraColumns: {
      lines: { label: "Items", always: true, sortable: true, w: "250px", value: (d, c) => (d.lines ?? []).map((l) => c.itemName(l.item_id)).join(" ").toLowerCase() },
    },
    cells: {
      party: (d, c) => stockAdjustmentIdentity(d, c),
      lines: (d, c) => stockAdjustmentLines(d, c),
    },
    load: async () => {
      const [docs, items] = await Promise.all([api(`${ledgerUrl()}/stock-adjustments`), api(`${ledgerUrl()}/items`)]);
      return { docs, items };
    },
    searchText: (d, c) => [d.description, d.reference, ...(d.lines ?? []).map((l) => `${c.itemName(l.item_id)} ${l.description ?? ""}`)].join(" "),
    open: (d, c) => showAdjustment(d, c.itemName),
    edit: (d) => editAdjustment(d.id),
  },
  // Stock transfers move quantity only, so the value column is Quantity, formatted without a currency.
  stock_transfer: {
    path: "stock-transfers", container: "inventory-transfers", noun: "stock transfer", plural: "stock transfers", prefix: "ST",
    numberKey: "transfer_no", newLabel: "New transfer", newAction: "transferStockForm()",
    filterParty: false, partyLabel: "Transfer",
    hide: BANK_HIDDEN, extra: "none", tabs: false, hideGroup: true, searchHint: "ST, item, location, reference",
    amounts: [{ key: "quantity", label: "Quantity" }], amountHeader: "Quantity",
    formatTotal: (total) => quantityText(total),
    columnOrder: ["party", "date", "reference", "items", "amount", "status", "description"],
    columnDefaults: ["description"],
    extraColumns: {
      items: { label: "Items", always: true, sortable: true, w: "230px", value: (d, c) => (d.lines ?? []).map((l) => c.itemName(l.item_id)).join(" ").toLowerCase() },
    },
    cells: {
      party: (d, c) => stockTransferIdentity(d, c),
      items: (d, c) => stockTransferLines(d, c),
      amount: (d) => quantityText(d.quantity),
    },
    load: async () => {
      const [docs, items, locations] = await Promise.all([
        api(`${ledgerUrl()}/stock-transfers`), api(`${ledgerUrl()}/items`), api(`${ledgerUrl()}/locations`),
      ]);
      return {
        docs: docs.map((d) => ({ ...d, quantity: d.lines.reduce((total, l) => total + Number(l.quantity), 0) })),
        items, locations,
      };
    },
    searchText: (d, c) => [d.reference, d.description, c.locationName(d.from_location_id), c.locationName(d.to_location_id),
      ...(d.lines ?? []).map((l) => c.itemName(l.item_id))].join(" "),
    open: (d, c) => showStockTransfer(d, c.itemName, c.locationName),
    noEdit: true,
  },
  // Assemblies are posted when made: the row is the made item, its quantity and components, and the value.
  assembly: {
    path: "assemblies", container: "inventory-assemblies", noun: "assembly", plural: "assemblies", prefix: "AS",
    numberKey: "assembly_no", newLabel: "Make assembly", newAction: "assemblyForm()",
    filterParty: false, partyLabel: "Assembly",
    hide: BANK_HIDDEN, extra: "none", tabs: false, hideGroup: true, searchHint: "AS, item, component, reference",
    amounts: [{ key: "value", label: "Value" }],
    columnOrder: ["party", "date", "reference", "quantity", "components", "amount", "status", "description"],
    columnDefaults: ["description"],
    extraColumns: {
      quantity: { label: "Quantity", num: true, always: true, sortable: true, noTotal: true, w: "100px", value: (d) => Number(d.quantity) },
      components: { label: "Components", always: true, sortable: true, w: "250px", value: (d, c) => (d.components ?? []).map((x) => c.itemName(x.component_id)).join(" ").toLowerCase() },
    },
    cells: {
      party: (d, c) => assemblyIdentity(d, c),
      quantity: (d) => quantityText(d.quantity),
      components: (d, c) => assemblyComponents(d, c),
    },
    load: async () => {
      const [docs, items] = await Promise.all([api(`${ledgerUrl()}/assemblies`), api(`${ledgerUrl()}/items`)]);
      return { docs, items };
    },
    searchText: (d, c) => [d.reference, d.description, c.itemName(d.item_id), ...(d.components ?? []).map((x) => c.itemName(x.component_id))].join(" "),
    open: (d, c) => showAssembly(d, c.itemName),
    noEdit: true,
  },
  // Journals: one row per posting, with its revision and source. No tabs: drafts and errors carry their
  // status in the row; a journal created by a document is opened there, not edited here.
  journal: {
    path: "journals", container: "journals", noun: "journal", plural: "journals", prefix: "J",
    numberKey: "journal_no", newLabel: "New journal", newAction: "journalForm()",
    filterParty: false, partyLabel: "Journal",
    hide: BANK_HIDDEN, extra: "none", tabs: false, hideGroup: true, searchHint: "reference, description, account, status",
    amounts: [{ key: "total", label: "Amount" }],
    columnOrder: ["party", "date", "reference", "revision", "amount", "status", "description"],
    columnDefaults: [],
    extraColumns: {
      revision: { label: "Revision", num: true, always: true, sortable: true, noTotal: true, w: "90px", value: (d) => d.revision_no },
    },
    cells: {
      party: (d, c) => {
        const name = d.description || d.reference || `Journal v${d.revision_no}`;
        const reference = !c.chosen.has("reference") && d.reference ? ` · ${esc(d.reference)}` : "";
        return `<div class="cell-main"><span class="cell-clip" title="${esc(name)}">${esc(name)}</span></div>
          <div class="cell-sub">${d.accounting_date}${reference}</div>`;
      },
      revision: (d) => `v${d.revision_no}`,
    },
    load: async () => {
      const docs = await api(`${ledgerUrl()}/journals`);
      return {
        docs: docs.map((d) => ({ ...d, date: d.accounting_date, total: d.lines.reduce((s, l) => s + Number(l.debit), 0).toFixed(2) })),
      };
    },
    searchText: (d) => [d.reference, d.description, d.status, ...(d.lines ?? []).map((l) => accountName(l.account_id))].join(" "),
    open: (d) => showJournal(d),
    edit: (d) => editJournal(d.id),
    editWhen: (d) => !d.source_kind,
  },
  // Pay runs are imported, not created here: one row per run, with its employee count and the amounts the
  // payroll system reported. The inspector below the table is opened from a row.
  pay_run: {
    path: "pay-runs", container: "payroll-runs", noun: "pay run", plural: "pay runs", prefix: "PR",
    numberKey: "pay_run_no", filterParty: false, partyLabel: "Pay run",
    hide: BANK_HIDDEN, extra: "none", tabs: false, hideGroup: true, tableClass: "payroll", searchHint: "PR, period, reference, status",
    amounts: [
      { key: "gross", label: "Gross" }, { key: "tax", label: "Tax on salary" }, { key: "nssf", label: "NSSF" }, { key: "net", label: "Net" },
    ],
    amountWidth: "110px",
    columnOrder: ["party", "date", "reference", "currency", "employees", "gross", "tax", "nssf", "amount", "status", "description"],
    columnDefaults: ["gross", "tax", "nssf", "description"],
    extraColumns: {
      currency: { label: "Currency", always: true, sortable: true, w: "80px", value: (d) => d.currency ?? "" },
      employees: { label: "Employees", num: true, always: true, sortable: true, noTotal: true, w: "96px", value: (d) => d.employees },
    },
    cells: {
      party: (d, c) => {
        const reference = !c.chosen.has("reference") && d.reference ? ` · ${esc(d.reference)}` : "";
        return `<div class="cell-main"><span class="cell-clip" title="${esc(d.period)}">${esc(d.period)}</span></div>
          <div class="cell-sub">${d.date}${reference}</div>`;
      },
      employees: (d) => Number(d.employees).toLocaleString(),
      currency: (d) => esc(d.currency ?? ""),
    },
    load: async () => {
      const docs = await api(`${ledgerUrl()}/pay-runs`);
      return {
        docs: docs.map((d) => ({
          ...d,
          employees: d.totals.employees,
          gross: d.totals.gross,
          tax: (Number(d.totals.salary_tax) + Number(d.totals.fringe_benefit_tax)).toFixed(2),
          nssf: (Number(d.totals.nssf_employee) + Number(d.totals.nssf_employer)).toFixed(2),
          net: d.totals.net,
        })),
      };
    },
    currencyOf: (d) => d.currency || state.ledger.base_currency,
    searchText: (d) => [d.period, d.reference, d.description, d.status, d.currency].join(" "),
    open: (d) => showPayRun(d),
    noEdit: true,
    after: `<div id="payroll-run"></div>`,
  },
  // Depreciation runs are one posted journal for a month. The period is the row's identity; the date field
  // is the first of the month, for the period filter and sort.
  depreciation_run: {
    path: "depreciation-runs", container: "assets-runs", noun: "depreciation run", plural: "depreciation runs", prefix: "",
    numberKey: "period", newLabel: "Post depreciation", newAction: "depreciationForm()",
    filterParty: false, partyLabel: "Depreciation",
    hide: BANK_HIDDEN, extra: "none", tabs: false, hideGroup: true, searchHint: "period, status",
    amounts: [{ key: "total", label: "Total" }],
    columnOrder: ["party", "date", "amount", "status"],
    columnDefaults: [],
    cells: {
      party: (d) => `<div class="cell-main"><span class="cell-clip" title="${esc(d.period)}">${esc(d.period)}</span></div>
        <div class="cell-sub">${d.lines.length} ${d.lines.length === 1 ? "asset" : "assets"}</div>`,
    },
    load: async () => {
      const docs = await api(`${ledgerUrl()}/depreciation-runs`);
      return { docs: docs.map((d) => ({ ...d, date: `${d.period}-01` })) };
    },
    searchText: (d) => [d.period, d.status].join(" "),
    open: (d) => showDepreciationRun(d),
    noEdit: true,
  },
};

const DOC_PRESETS = ["this-month", "last-month", "this-quarter", "this-year", "last-year", "custom"];
const PRESET_LABELS = {
  "this-month": "This month", "last-month": "Last month", "this-quarter": "This quarter",
  "this-year": "This year", "last-year": "Last year", custom: "Custom…",
};

// Master data: a simple list has no period, no tabs and no totals. It is for records that are read and
// edited, not documents that post. Columns are fixed and the row count is a footer, not a total row.
const partyRoles = (p) => [p.is_customer ? "Customer" : null, p.is_supplier ? "Supplier" : null].filter(Boolean).join(", ");
const SIMPLE_KINDS = {
  party: {
    container: "parties", noun: "business party", plural: "business parties",
    newLabel: "New business party", newAction: "partyForm()",
    searchHint: "name, tax ID, contact, email, phone",
    columns: [
      { key: "name", label: "Business party", sortable: true },
      { key: "roles", label: "Roles", sortable: true, w: "150px" },
      { key: "tax_id", label: "Tax ID", sortable: true, w: "128px" },
      { key: "terms", label: "Terms (days)", sortable: true, num: true, w: "100px" },
      { key: "status", label: "Status", sortable: true, w: "86px" },
    ],
    load: async () => api(`${ledgerUrl()}/parties`),
    // The second line is how to reach the person: their contact, or the party's email or phone.
    cells: (p) => ({
      name: `<div class="cell-main"><span class="cell-clip" title="${esc(p.name)}">${esc(p.name)}</span></div>
        <div class="cell-sub">${esc([p.contact_name, p.email || p.phone].filter(Boolean).join(" · "))}</div>`,
      roles: esc(partyRoles(p)),
      tax_id: `<span class="cell-clip" title="${esc(p.tax_id ?? "")}">${esc(p.tax_id ?? "")}</span>`,
      terms: p.payment_terms_days ?? "",
      status: `<span class="pill ${p.active ? "posted" : ""}">${p.active ? "active" : "inactive"}</span>`,
    }),
    searchText: (p) => `${partyRoles(p)} ${p.tax_id ?? ""} ${p.contact_name ?? ""} ${p.email ?? ""} ${p.phone ?? ""} ${p.address ?? ""} ${p.active ? "active" : "inactive"}`,
    open: (p) => partyForm(p),
  },
};

function docFilters(kind) {
  state.docFilters ??= {};
  state.docFilters[kind] ??= { preset: "this-year", from: "", to: "", party: "", facets: {}, q: "" };
  return state.docFilters[kind];
}

const iso = (date) => date.toISOString().slice(0, 10);
const monthStart = (date) => new Date(Date.UTC(date.getFullYear(), date.getMonth(), 1));
const addMonths = (date, months) => new Date(Date.UTC(date.getFullYear(), date.getMonth() + months, 1));
const dayBefore = (date) => new Date(date.getTime() - 86400000);

// The fiscal year the given date falls in, from the ledger's start month: the unit a bookkeeper thinks
// in, and it includes dates later in the year (a bill dated at month end).
function fiscalYear(ledger, today = new Date()) {
  const startMonth = ledger.fy_start_month ?? 1;
  const year = today.getMonth() + 1 >= startMonth ? today.getFullYear() : today.getFullYear() - 1;
  const start = new Date(Date.UTC(year, startMonth - 1, 1));
  return { start, end: dayBefore(addMonths(start, 12)) };
}

function presetRange(preset, f) {
  const now = new Date();
  const fy = fiscalYear(state.ledger, now);
  switch (preset) {
    case "this-month": { const s = monthStart(now); return { from: iso(s), to: iso(dayBefore(addMonths(s, 1))) }; }
    case "last-month": { const s = addMonths(monthStart(now), -1); return { from: iso(s), to: iso(dayBefore(addMonths(s, 1))) }; }
    case "this-quarter": {
      const q = Math.floor(now.getMonth() / 3) * 3;
      const s = new Date(Date.UTC(now.getFullYear(), q, 1));
      return { from: iso(s), to: iso(dayBefore(addMonths(s, 3))) };
    }
    case "last-year": { const s = addMonths(fy.start, -12); return { from: iso(s), to: iso(dayBefore(addMonths(s, 12))) }; }
    case "custom": return { from: f.from || iso(fy.start), to: f.to || iso(fy.end) };
    default: return { from: iso(fy.start), to: iso(fy.end) };
  }
}

const docTab = (kind) => ((state.docTab ??= {})[kind] ??= "posted");
const docSort = (kind) => ((state.docSort ??= {})[kind] ??= { key: "date", dir: "desc" });
// Optional columns, off by default: the list shows one amount and the rest is detail.
// Columns on by default: the owner's choice on 2026-10-25. Amount is always shown and is not in the set.
// A kind may choose its own defaults (the bank lists show description; they have no VAT or FX column).
const DOC_COLUMN_DEFAULTS = ["net_total", "tax_total", "other_tax_total", "description", "fx"];
const docColumns = (kind) => ((state.docCols ??= {})[kind] ??= new Set(DOC_KINDS[kind]?.columnDefaults ?? DOC_COLUMN_DEFAULTS));
// The saved column order for a list, reconciled with the columns that exist now.
function docColumnOrder(kind, all) {
  state.docOrder ??= {};
  const saved = (state.docOrder[kind] ?? []).filter((key) => all.includes(key));
  const missing = all.filter((key) => !saved.includes(key));
  state.docOrder[kind] = [...saved, ...missing];
  return state.docOrder[kind];
}
const docDensity = () => (state.density ??= "comfortable");
const groupByMonth = (kind) => ((state.groupByMonth ??= {})[kind] ??= false);
const docSelection = (kind) => ((state.selection ??= {})[kind] ??= new Set());
const monthLabel = (key) => new Intl.DateTimeFormat("en-US", { month: "long", year: "numeric", timeZone: "UTC" }).format(new Date(`${key}-01T00:00:00Z`));
const colsOpen = (kind) => ((state.colsOpen ??= {})[kind] ??= false);
const docPageSize = () => (state.pageSize ??= 50);
const overdueDays = (d) => Math.floor((new Date(today()) - new Date(d.due_date)) / 86400000);

// ---- Cash and bank rows ----
// A bank account is an account id plus details; the list has to turn the id into the two-line cell the
// table shows, and its currency has to travel with every amount.
const bankOf = (id) => (state.banks ?? []).find((b) => b.account_id === id);
const bankLabel = (id) => {
  const b = bankOf(id);
  return b ? `${b.code} ${b.name}` : "";
};
const bankCurrency = (id) => bankOf(id)?.currency ?? state.ledger?.base_currency;
// Quantities are not money: no currency decimals, but up to four places for a fraction of a unit.
const quantityText = (value) => {
  const n = Number(value ?? 0);
  return Number.isFinite(n) ? n.toLocaleString("en-US", { maximumFractionDigits: 4 }) : String(value ?? "");
};
const bankCell = (id) => {
  const b = bankOf(id);
  if (!b) return "";
  return `<div class="cell-main"><span class="cell-clip" title="${esc(`${b.code} ${b.name}`)}">${esc(b.code)} ${esc(b.name)}</span></div>
    <div class="cell-sub">${esc(b.bank_name)}${b.currency ? ` · ${esc(b.currency)}` : ""}</div>`;
};
// What a line was for. A payment has one or more categories: an account, an invoice it settles, or the
// party's balance. The first is shown, the count of the rest is a badge, and all of them are in the title.
const paymentLineText = (l) => {
  if (l.kind === "account") return accountName(l.account_id);
  if (l.kind === "invoice") return "Settles an invoice";
  return "Party balance";
};
const paymentCategoryText = (d) => (d.lines ?? []).map((l) => paymentLineText(l)).filter(Boolean).join(", ");
const paymentCategoryCell = (d) => {
  const labels = (d.lines ?? []).map((l) => paymentLineText(l)).filter(Boolean);
  if (!labels.length) return "";
  const more = labels.length > 1 ? ` <span class="pill" title="${esc(labels.slice(1).join(", "))}">+${labels.length - 1}</span>` : "";
  return `<span class="cell-clip" title="${esc(labels.join(", "))}">${esc(labels[0])}</span>${more}`;
};
// The first cell is the row's identity. A payment is for a party when one is named; a bank fee has no
// party, so its description or category stands in. A transfer is the two accounts it moves between.
const documentIdentity = (d, c) => {
  const name = c.partyName(d) || d.description || paymentCategoryText(d) || "No party";
  const reference = !c.chosen.has("reference") && d.reference ? ` · ${esc(d.reference)}` : "";
  return `<div class="cell-main"><span class="cell-clip" title="${esc(name)}">${esc(name)}</span></div>
    <div class="cell-sub">${d.date}${reference}</div>`;
};
// The merged bank list has three document kinds. A transfer's description is its identity; its bank cell
// says which side of the movement this account is on, and the In/Out columns say which way the money moved.
const transactionCategoryText = (d) => (d.doc_kind === "transfer"
  ? (d.direction === "in" ? "Transfer in" : "Transfer out")
  : paymentCategoryText(d));
const transactionCategoryCell = (d) => (d.doc_kind === "transfer"
  ? `<span class="muted">${d.direction === "in" ? "Transfer in" : "Transfer out"}</span>`
  : paymentCategoryCell(d));
const transactionIdentity = (d, c) => {
  if (d.doc_kind !== "transfer") return documentIdentity(d, c);
  const name = d.description || "Bank transfer";
  const reference = !c.chosen.has("reference") && d.reference ? ` · ${esc(d.reference)}` : "";
  return `<div class="cell-main"><span class="cell-clip" title="${esc(name)}">${esc(name)}</span></div>
    <div class="cell-sub">${d.date}${reference}</div>`;
};
const transactionBankCell = (d) => {
  if (d.doc_kind !== "transfer") return bankCell(d.bank_account_id);
  const account = bankOf(d.bank_account_id);
  if (!account) return "";
  const other = bankLabel(d.counterparty_account_id);
  return `<div class="cell-main"><span class="cell-clip" title="${esc(`${account.code} ${account.name}`)}">${esc(account.code)} ${esc(account.name)}</span></div>
    <div class="cell-sub">${d.direction === "in" ? "←" : "→"} ${esc(other)}</div>`;
};

// ---- Inventory rows ----
const stockAdjustmentIdentity = (d, c) => {
  const names = (d.lines ?? []).map((l) => c.itemName(l.item_id)).filter(Boolean);
  const name = d.description || names.slice(0, 2).join(", ") || "Stock adjustment";
  const reference = !c.chosen.has("reference") && d.reference ? ` · ${esc(d.reference)}` : "";
  return `<div class="cell-main"><span class="cell-clip" title="${esc(name)}">${esc(name)}</span></div>
    <div class="cell-sub">${d.date}${reference}</div>`;
};
const stockAdjustmentLines = (d, c) => {
  const parts = (d.lines ?? []).map((l) => `${c.itemName(l.item_id)} ${quantityText(l.quantity)}`);
  return `<span class="cell-clip" title="${esc(parts.join("; "))}">${esc(parts.join("; "))}</span>`;
};
const stockTransferIdentity = (d, c) => {
  const name = `${c.locationName(d.from_location_id)} → ${c.locationName(d.to_location_id)}`;
  const reference = !c.chosen.has("reference") && d.reference ? ` · ${esc(d.reference)}` : "";
  return `<div class="cell-main"><span class="cell-clip" title="${esc(name)}">${esc(c.locationName(d.from_location_id))} <span class="muted">→</span> ${esc(c.locationName(d.to_location_id))}</span></div>
    <div class="cell-sub">${d.date}${reference}</div>`;
};
const stockTransferLines = (d, c) => {
  const parts = (d.lines ?? []).map((l) => c.itemName(l.item_id));
  return `<span class="cell-clip" title="${esc(parts.join("; "))}">${esc(parts.join("; "))}</span>`;
};
const assemblyIdentity = (d, c) => {
  const name = c.itemName(d.item_id) || "Assembly";
  const reference = !c.chosen.has("reference") && d.reference ? ` · ${esc(d.reference)}` : "";
  return `<div class="cell-main"><span class="cell-clip" title="${esc(name)}">${esc(name)}</span></div>
    <div class="cell-sub">${d.date}${reference}</div>`;
};
const assemblyComponents = (d, c) => {
  const parts = (d.components ?? []).map((x) => `${c.itemName(x.component_id)} ${quantityText(x.quantity)}`);
  return `<span class="cell-clip" title="${esc(parts.join("; "))}">${esc(parts.join("; "))}</span>`;
};

async function loadDocData(k) {
  if (k.load) return k.load();
  const [docs, counterparts, parties] = await Promise.all([
    api(`${ledgerUrl()}/${k.path}`),
    k.refPath ? api(`${ledgerUrl()}/${k.refPath}`) : Promise.resolve([]),
    api(`${ledgerUrl()}/parties`),
  ]);
  return { docs, counterparts, parties };
}

async function renderDocList(kind) {
  const k = DOC_KINDS[kind];
  if (!k) return;
  const f = docFilters(kind);
  const withTabs = k.tabs !== false;
  const tab = withTabs ? docTab(kind) : "all";
  const sort = docSort(kind);
  const range = presetRange(f.preset, f);
  // The data is fetched once per list and reused while the page stays open; a save or a delete clears
  // the cache through refreshLedger. Column and density changes then cost nothing.
  state.docData ??= {};
  if (!state.docData[kind]) state.docData[kind] = await loadDocData(k);
  const data = state.docData[kind];
  const docs = data.docs ?? [];
  const counterparts = data.counterparts ?? [];
  const parties = data.parties ?? [];
  const banks = data.banks ?? state.banks ?? [];
  const items = data.items ?? [];
  const locations = data.locations ?? [];
  if (data.banks) state.banks = banks;
  const nameOf = (id) => parties.find((p) => p.id === id)?.name ?? "";
  const partyName = (d) => d.party_label ?? (k.partyText ? k.partyText(d) : nameOf(d[k.partyKey]));
  const itemName = (id) => items.find((i) => i.id === id)?.name ?? "";
  const locationName = (id) => locations.find((l) => l.id === id)?.name ?? "";
  const counterpartNo = (id) => counterparts.find((c) => c.id === id)?.invoice_no ?? "";
  const khr = (d) => (d.tax_currency ? `${money(d.tax_currency.gross_total, "KHR")} ៛` : "");
  const sum = (list, key) => list.reduce((s, d) => s + Number(d[key] ?? 0), 0).toFixed(2);
  const ids = { filter: `${k.container}-filter`, tabs: `${k.container}-tabs`, party: `${k.container}-party`, list: `${k.container}-party-list`, facet: `${k.container}-facet`, search: `${k.container}-search`, cols: `${k.container}-cols`, colsMenu: `${k.container}-cols-menu`, density: `${k.container}-density`, group: `${k.container}-group`, bar: `${k.container}-bar`, selectAll: `${k.container}-selectall` };

  const chosen = docColumns(kind);
  const currency = state.ledger.base_currency;
  const numberOf = (d) => d[k.numberKey ?? "invoice_no"] ?? "";
  const pathOf = k.pathOf ?? (() => k.path);
  const nounOf = k.nounOf ?? (() => k.noun);
  const currencyOf = k.currencyOf ?? (() => currency);
  const amountKey = k.amounts[k.amounts.length - 1].key;
  const amountLabel = k.amounts[k.amounts.length - 1].label;
  const ctx = { docs, parties, counterparts, banks, items, locations, itemName, locationName, nameOf, partyName, chosen, numberOf, currencyOf, amountKey, amountLabel };
  const facets = f.facets ??= {};
  const facetDefs = (k.facets ?? []).map((def) => ({ ...def, options: def.options(ctx) }));
  // A required facet is a choice, not a filter: the page always works on one bank account and never adds
  // different currencies together. Fall back to the first account if the saved one is gone.
  for (const def of facetDefs) {
    if (def.required && !def.options.some((o) => o.value === facets[def.key])) facets[def.key] = def.options[0]?.value ?? "";
  }
  const facetValue = (def) => facets[def.key];
  const facetLabel = (def, value) => def.options.find((o) => o.value === value)?.label ?? value;
  const activeFacets = facetDefs.filter((def) => !def.required && facetValue(def));
  const amountTotal = (list, key = amountKey, separator = "<br>") => {
    if (k.formatTotal) return k.formatTotal(sum(list, key));
    if (!k.currencyOf) return money(sum(list, key));
    const groups = new Map();
    for (const d of list) {
      const cur = currencyOf(d);
      groups.set(cur, (groups.get(cur) ?? 0) + Number(d[key] ?? 0));
    }
    return [...groups.entries()].map(([cur, total]) => `${money(total, cur)} ${cur}`).join(separator) || money(0);
  };

  const inPeriod = docs.filter((d) => d.status !== "deleted");
  const inRange = inPeriod.filter((d) => d.date >= range.from && d.date <= range.to);
  // What the search box looks in: the number, the reference, the party, the description, and every
  // amount on the document — as stored ("2250.00") and as shown ("2,250.00"). Bank lists add the bank
  // account and the category. It is built once per document when the list loads: at a few thousand
  // documents, rebuilding it per keystroke is the difference between typing and waiting.
  const searchIndex = (state.docSearchIndex ??= {});
  if (!searchIndex[kind]) {
    searchIndex[kind] = new Map(docs.map((d) => {
      const amounts = [...k.amounts.map((a) => a.key), "other_tax_total"]
        .map((key) => d[key])
        .filter((value) => value !== undefined && value !== null && value !== "")
        .flatMap((value) => [String(value), money(value, currencyOf(d))]);
      const extra = k.searchText ? k.searchText(d, ctx) : "";
      return [d.id, `${k.prefix} ${numberOf(d)} ${d.reference ?? ""} ${d.description ?? ""} ${partyName(d)} ${extra} ${amounts.join(" ")}`.toLowerCase()];
    }));
  }
  const needle = f.q.trim().toLowerCase();
  const rows = inRange.filter((d) => (!f.party || d[k.partyKey] === f.party)
    && facetDefs.every((def) => {
      const value = facets[def.key];
      return def.required ? !!value && d[def.key] === value : !value || d[def.key] === value;
    })
    && (!needle || searchIndex[kind].get(d.id)?.includes(needle)));
  const posted = rows.filter((d) => d.status === "posted");
  const drafts = rows.filter((d) => d.status !== "posted");
  const shown = withTabs ? (tab === "posted" ? posted : drafts) : rows;
  // Drafts never count: in the no-tab variant the totals row is the posted documents only.
  const counted = withTabs ? shown : posted;

  const late = posted.filter((d) => d.due_date && d.due_date < today());
  const partyOf = (d) => parties.find((p) => p.id === d[k.partyKey]);
  const fxOf = (d) => `${d.currency || currency}${d.exchange_rate ? ` @ ${d.exchange_rate}` : ""}`;

  // Every column the list can show, with its default place. `always` ones cannot be hidden; the order
  // of all of them can be changed, and both choices are kept per list kind for the session.
  const baseMeta = {
    party: { label: k.partyLabel, always: true, sortable: true },
    due: { label: "Due", always: true, sortable: true, w: "100px" },
    date: { label: "Date", sortable: true, w: "96px" },
    reference: { label: "Reference", sortable: true, w: "132px" },
    ...Object.fromEntries(k.amounts.slice(0, -1).map((a) => [a.key, { label: a.label, sortable: true, num: true, always: !!a.always, w: a.w ?? "84px" }])),
    other_tax_total: { label: "Other tax", sortable: true, num: true, w: "84px" },
    amount: { label: k.amountHeader ?? (k.currencyOf ? amountLabel : `${amountLabel} (${currency})`), always: true, sortable: true, num: true, valueKey: amountKey, w: k.amountWidth ?? "116px" },
    fx: { label: "Currency", sortable: true, w: "84px" },
    khr: { label: "KHR", sortable: true, num: true, w: "112px" },
    status: { label: "Status", always: true, sortable: true, w: k.statusWidth ?? "80px" },
    description: { label: "Description", sortable: true, w: "168px" },
    lines_count: { label: "Lines", sortable: true, num: true, noTotal: true, w: "68px" },
    party_tax_id: { label: "Tax ID", sortable: true, w: "128px" },
    party_terms: { label: "Terms (days)", sortable: true, num: true, noTotal: true, w: "96px" },
    journal: { label: "Journal No.", sortable: true, w: "96px" },
    ...(k.extraColumns ?? {}),
  };
  const META = {};
  const hidden = new Set(k.hide ?? []);
  for (const [key, def] of Object.entries(baseMeta)) if (!hidden.has(key)) META[key] = def;
  const order = docColumnOrder(kind, (k.columnOrder ?? Object.keys(META)).filter((key) => META[key]));

  const sortValue = (d) => {
    const party = parties.find((p) => p.id === d[k.partyKey]);
    const meta = META[sort.key];
    if (meta?.value) return meta.value(d, ctx);
    if (meta?.valueKey) return Number(d[meta.valueKey] ?? 0);
    switch (sort.key) {
      case "date": return d.date;
      case "due": return d.due_date ?? "";
      case "party": return partyName(d).toLowerCase();
      case "party_tax_id": return (party?.tax_id ?? "").toLowerCase();
      case "party_terms": return Number(party?.payment_terms_days ?? 0);
      case "status": return d.status;
      case "description": return (d.description ?? "").toLowerCase();
      case "lines_count": return d.lines?.length ?? 0;
      case "other_tax_total": return Number(d.other_tax_total ?? 0);
      case "fx": return d.currency ?? "";
      case "khr": return Number(d.tax_currency?.gross_total ?? 0);
      case "amount": return Number(d[amountKey] ?? 0);
      case "journal": return Number(d.journal_no ?? 0);
      case "reference": return (d.reference ?? "").toLowerCase();
      default: return Number(d[sort.key] ?? 0);
    }
  };
  const sorted = [...shown].sort((a, b) => {
    const va = sortValue(a), vb = sortValue(b);
    const cmp = typeof va === "number" && typeof vb === "number" ? va - vb : String(va).localeCompare(String(vb));
    return sort.dir === "desc" ? -cmp : cmp;
  });
  const columns = [{ key: "select", label: "", w: "34px" }]
    .concat(order
      .filter((key) => META[key] && (META[key].always || chosen.has(key)))
      .map((key) => ({ key, ...META[key] })))
    .concat([{ key: "menu", label: "", w: "46px" }]);

  const header = columns.map((c) => {
    const cls = [c.num ? "num" : "", c.sortable ? "sortable" : "", c.sortable && sort.key === c.key ? "sorted" : ""].filter(Boolean).join(" ");
    const caret = c.sortable && sort.key === c.key ? `<span class="caret">${sort.dir === "asc" ? "▲" : "▼"}</span>` : "";
    if (c.key === "select") return `<th style="width:${c.w}"><input type="checkbox" id="${ids.selectAll}" title="Select all on this page"></th>`;
    return `<th class="${cls} col-${c.key}" ${c.sortable ? `data-sort="${c.key}"` : ""}${c.w ? ` style="width:${c.w}"` : ""}>${c.label}${caret}</th>`;
  }).join("");

  const cellDefaults = (d) => {
    const lateDays = d.status === "posted" && d.due_date && d.due_date < today() ? overdueDays(d) : 0;
    const party = partyOf(d);
    // The first cell is the row's identity: who it is for, and when. The system number stays out of the
    // list; the reference joins the second line only while it has no column of its own.
    const reference = chosen.has("reference") ? esc(d.reference ?? "") : "";
    return {
      select: `<input type="checkbox" class="row-check" data-select="${d.id}" ${docSelection(kind).has(d.id) ? "checked" : ""}>`,
      party: `<div class="cell-main"><span class="cell-clip" title="${esc(partyName(d))}">${esc(partyName(d))}</span></div>
        <div class="cell-sub">${d.date}${!reference && d.reference ? ` · ${esc(d.reference)}` : ""}</div>`,
      due: d.due_date ? `${d.due_date}${lateDays > 0 ? ` <span class="late" title="${lateDays} days overdue">${lateDays}d late</span>` : ""}` : "",
      date: d.date,
      reference: `<span class="cell-clip" title="${esc(d.reference ?? "")}">${reference}</span>`,
      amount: money(d[amountKey], currencyOf(d)),
      khr: khr(d),
      fx: fxOf(d),
      status: `<span class="pill ${d.status}">${d.status}</span>`,
      description: `<span class="cell-clip" title="${esc(d.description ?? "")}">${esc(d.description ?? "")}</span>`,
      lines_count: d.lines?.length ?? "",
      party_tax_id: `<span class="cell-clip" title="${esc(party?.tax_id ?? "")}">${esc(party?.tax_id ?? "")}</span>`,
      party_terms: party?.payment_terms_days ?? "",
      journal: d.journal_no ? `No. ${d.journal_no}` : "",
      other_tax_total: money(d.other_tax_total, currencyOf(d)),
      menu: `<button class="row-menu-btn" data-menu="${d.id}" title="Actions">⋯</button>`,
      ...Object.fromEntries(k.amounts.slice(0, -1).map((a) => [a.key, money(d[a.key], currencyOf(d))])),
    };
  };
  const cell = (d) => {
    const values = cellDefaults(d);
    for (const [key, fn] of Object.entries(k.cells ?? {})) values[key] = fn(d, ctx);
    for (const [key, meta] of Object.entries(META)) {
      if (values[key] === undefined && meta.cell) values[key] = meta.cell(d, ctx);
    }
    return values;
  };

  // Grouping by month means whole months, so paging steps aside while it is on.
  const grouped = k.hideGroup ? false : groupByMonth(kind);
  const pageSize = docPageSize();
  state.docPage ??= {};
  const pageCount = grouped ? 1 : Math.max(1, Math.ceil(sorted.length / pageSize));
  const page = grouped ? 1 : Math.min(state.docPage[kind] ?? 1, pageCount);
  state.docPage[kind] = page;
  const paged = grouped ? sorted : sorted.slice((page - 1) * pageSize, page * pageSize);

  const rowHtml = (d) => {
    const cells = cell(d);
    return `<tr class="click" data-id="${d.id}">${columns.map((c) => `<td class="${c.num ? "num" : ""} col-${c.key}">${cells[c.key] ?? ""}</td>`).join("")}</tr>`;
  };
  const columnTotal = (c, list) => {
    const countedRows = k.tabs === false ? list.filter((d) => d.status === "posted") : list;
    if (!c.num || c.noTotal) return "";
    if (c.total) return c.total(countedRows, ctx);
    return amountTotal(countedRows, c.valueKey ?? c.key);
  };
  const subtotalRow = (list) => `<tr class="subtotal">${columns.map((c) => {
    const cellClass = `col-${c.key}`;
    const countedRows = k.tabs === false ? list.filter((d) => d.status === "posted") : list;
    if (c.key === "select" || c.key === "menu") return `<td class="${cellClass}"></td>`;
    if (c.key === "party") return `<td class="${cellClass}"><span class="muted">Subtotal</span></td>`;
    if (c.key === "status") return `<td class="${cellClass}">${countedRows.length} ${countedRows.length === 1 ? k.noun : k.plural}</td>`;
    return `<td class="${c.num ? "num" : ""} ${cellClass}">${columnTotal(c, countedRows)}</td>`;
  }).join("")}</tr>`;

  const body = grouped
    ? (() => {
        const months = new Map();
        for (const d of sorted) {
          const key = d.date.slice(0, 7);
          if (!months.has(key)) months.set(key, []);
          months.get(key).push(d);
        }
        return [...months.entries()].map(([key, list]) => `<tr class="group-head"><td colspan="${columns.length}">${monthLabel(key)}</td></tr>${list.map(rowHtml).join("")}${subtotalRow(list)}`).join("");
      })()
    : paged.map(rowHtml).join("");

  const totals = `<tr class="total">${columns.map((c) => {
    const cellClass = `col-${c.key}`;
    if (c.key === "select" || c.key === "menu") return `<td class="${cellClass}"></td>`;
    if (c.key === "party") return `<td class="${cellClass}">Total</td>`;
    if (c.key === "status") return `<td class="${cellClass}">${counted.length} ${counted.length === 1 ? k.noun : k.plural}</td>`;
    return `<td class="${c.num ? "num" : ""} ${cellClass}">${columnTotal(c, counted)}</td>`;
  }).join("")}</tr>`;

  const pager = `<div class="pager">
    <span class="muted small">${sorted.length ? `${(page - 1) * pageSize + 1}–${Math.min(page * pageSize, sorted.length)} of ${sorted.length}` : ""}</span>
    <label class="inline-label">Rows <select id="${k.container}-pagesize">
      ${[25, 50, 100, 1000].map((n) => `<option value="${n}" ${pageSize === n ? "selected" : ""}>${n}</option>`).join("")}
    </select></label>
    ${pageCount > 1 ? `<button type="button" class="secondary small" data-page="prev" ${page === 1 ? "disabled" : ""}>‹</button>
      <span class="muted small">${page} / ${pageCount}</span>
      <button type="button" class="secondary small" data-page="next" ${page === pageCount ? "disabled" : ""}>›</button>` : ""}
  </div>`;

  // One line of facts in the tab row: the tiles that stood here repeated the totals row.
  const overdue = sum(late, amountKey);
  const credited = new Set(shown.map((d) => d.original_invoice_id).filter(Boolean)).size;
  const facts = tab === "draft"
    ? ""
    : k.extra === "none"
      ? ""
      : k.extra === "overdue"
      ? (Number(overdue) > 0 ? `Overdue ${money(overdue)} ${currency}` : "")
      : credited > 0
        ? `${credited} ${k.side === "sale" ? "invoice" : "bill"}${credited === 1 ? "" : "s"} ${k.side === "sale" ? "credited" : "debited"}`
        : "";

  const chips = [
    f.preset !== "this-year"
      ? `<span class="chip">${PRESET_LABELS[f.preset] ?? "This year"}${f.preset === "custom" ? ` · ${range.from} → ${range.to}` : ""}<button data-clear="preset" title="Back to the whole fiscal year">×</button></span>`
      : "",
    ...activeFacets.map((def) => `<span class="chip">${esc(def.label)}: ${esc(facetLabel(def, facets[def.key]))}<button data-clear="facet:${def.key}" title="Remove">×</button></span>`),
    f.party ? `<span class="chip">${k.partyLabel}: ${esc(nameOf(f.party))}<button data-clear="party" title="Remove">×</button></span>` : "",
    f.q ? `<span class="chip">Search: “${esc(f.q)}”<button data-clear="q" title="Remove">×</button></span>` : "",
  ].filter(Boolean).join("");
  const filtered = f.party || f.q || activeFacets.length > 0 || f.preset !== "this-year";
  const chipRow = chips || filtered ? `<div class="chips">${chips}${filtered ? `<button class="link" id="${k.container}-clear">Clear all</button>` : ""}</div>` : "";

  // The chooser lists every column in its current order: a checkbox to show it, arrows to move it.
  const columnRow = (key) => {
    const meta = META[key];
    const i = order.indexOf(key);
    return `<div class="col-row">
      <label class="check"><input type="checkbox" data-col="${key}" ${meta.always ? "checked disabled" : chosen.has(key) ? "checked" : ""}> ${meta.label}</label>
      <span class="col-move">
        <button type="button" data-move="up" data-key="${key}" ${i === 0 ? "disabled" : ""} title="Move left">↑</button>
        <button type="button" data-move="down" data-key="${key}" ${i === order.length - 1 ? "disabled" : ""} title="Move right">↓</button>
      </span>
    </div>`;
  };
  const columnsMenu = `<div class="popover${colsOpen(kind) ? "" : " hidden"}" id="${ids.colsMenu}">
    <p class="muted small" style="margin:0 0 6px">Show a column, or move it left and right</p>
    ${order.map(columnRow).join("")}
    <p class="muted small" style="margin:6px 0 0">${amountLabel} always shows; the arrows set the order.</p>
    <button type="button" class="link" id="${ids.colsMenu}-reset" style="font-size:12px;padding:2px 0">Reset to defaults</button></div>`;

  setActions("");

  const previousSearch = $(ids.search);
  const searchHadFocus = previousSearch && document.activeElement === previousSearch;
  const searchSelection = searchHadFocus ? [previousSearch.selectionStart, previousSearch.selectionEnd] : null;

  const newAction = k.newAction ?? (k.formKey ? `documentForm('${k.formKey}')` : null);
  const newButton = canEdit() && (newAction || k.newMenu)
    ? `<button type="button" class="new-doc" ${newAction ? `onclick="${newAction}"` : ""}>+ ${esc(k.newLabel)}${k.newMenu ? " ▾" : ""}</button>`
    : "";
  const partyPicker = k.filterParty === false ? "" : `<label class="combo-label">${k.partyLabel}
        <span class="combo">
          <input id="${ids.party}" name="party" placeholder="All ${k.partyLabel.toLowerCase()}s" value="${esc(f.party ? nameOf(f.party) : "")}">
          <div class="combo-list" id="${ids.list}" hidden></div>
        </span>
      </label>`;
  const facetPickers = facetDefs.map((def) => `<label>${def.label}
      <select id="${ids.facet}-${def.key}" name="facet-${def.key}" ${def.options.length ? "" : "disabled"}>
        ${def.required ? "" : `<option value="">${esc(def.all ?? "All")}</option>`}
        ${def.options.map((o) => `<option value="${esc(o.value)}" ${facets[def.key] === o.value ? "selected" : ""}>${esc(o.label)}</option>`).join("")}
      </select></label>`).join("");
  const tabMeta = withTabs
    ? `<div class="tabs" id="${ids.tabs}">
      <button data-doc-tab="posted" class="${tab === "posted" ? "active" : ""}">Posted <span class="count">${posted.length}</span></button>
      <button data-doc-tab="draft" class="${tab === "draft" ? "active" : ""}">Draft <span class="count">${drafts.length}</span></button>
      <span class="tabs-meta">${tab === "posted"
        ? `showing ${paged.length} of ${posted.length}${facts ? ` · <span class="${Number(overdue) > 0 && k.extra === "overdue" ? "error" : ""}">${facts}</span>` : ""}`
        : `showing ${paged.length} of ${drafts.length} ${drafts.length === 1 ? "draft" : "drafts"} · not in the books`}</span>
    </div>`
    : `<div class="list-meta">
      <span>Showing ${paged.length} of ${rows.length} ${rows.length === 1 ? k.noun : k.plural}${drafts.length ? ` · ${drafts.length} ${drafts.length === 1 ? "draft" : "drafts"} not in the books` : ""}${facts ? ` · ${facts}` : ""}</span>
    </div>`;

  $(`tab-${k.container}`).innerHTML = `
    <form class="filter-bar" id="${ids.filter}" autocomplete="off">
      ${newButton}
      <label>Period <select id="${ids.filter}-preset" name="preset">
        ${DOC_PRESETS.map((p) => `<option value="${p}" ${f.preset === p ? "selected" : ""}>${PRESET_LABELS[p]}</option>`).join("")}
      </select></label>
      ${f.preset === "custom" ? `<label>From <input type="date" name="from" value="${range.from}"></label>
        <label>To <input type="date" name="to" value="${range.to}"></label>` : ""}
      ${facetPickers}
      ${partyPicker}
      <label class="search">Search <input id="${ids.search}" name="q" value="${esc(f.q)}" placeholder="${esc(k.searchHint ?? `${k.prefix}, reference, ${k.partyLabel.toLowerCase()}, description, amount`)}"></label>
      ${k.hideGroup ? "" : `<button type="button" class="secondary small" id="${ids.group}" title="Group by month">${grouped ? "Ungroup" : "Group by month"}</button>`}
      <button type="button" class="secondary small" id="${ids.cols}" title="Extra columns">Columns</button>
      <button type="button" class="secondary small" id="${ids.density}" title="Row height">${docDensity() === "compact" ? "Comfortable" : "Compact"}</button>
      ${columnsMenu}
    </form>
    ${chipRow}
    ${tabMeta}

    <div class="select-bar hidden" id="${ids.bar}">
      <b id="${ids.bar}-count">0</b> selected <span class="muted small" id="${ids.bar}-total"></span>
      <span class="spacer"></span>
      ${k.canPrint ?? k.side === "sale" ? `<button type="button" class="secondary small" data-bulk="print">Download PDF</button>` : ""}
      <button type="button" class="danger small" data-bulk="delete">Delete</button>
      <button type="button" class="secondary small" data-bulk="clear">Clear</button>
    </div>
    ${(tab === "draft" && drafts.length) || (k.tabs === false && drafts.length) ? `<p class="warning">These are not posted, so they are not in the ledger and they block filing the months they are dated in. Fix and post each one, or delete it.</p>` : ""}
    ${sorted.length
      ? `<div class="table-scroll"><table class="list ${docDensity()} ${k.tableClass ?? ""}"><tr>${header}</tr>${body}${(withTabs ? tab === "posted" : counted.length > 0) ? totals : ""}</table></div>${grouped ? "" : pager}`
      : (k.noData?.(ctx) ? `<p class="muted">${esc(k.noData(ctx))}</p>` : `<p class="muted">No ${withTabs && tab === "draft" ? "draft" : "posted"} ${k.plural} in ${PRESET_LABELS[f.preset]?.toLowerCase() ?? "this period"}${f.party ? ` for ${esc(nameOf(f.party))}` : ""}${activeFacets.map((def) => ` on ${esc(facetLabel(def, facets[def.key]))}`).join("")}${f.q ? ` matching “${esc(f.q)}”` : ""}.</p>`)}
    ${k.after ?? ""}`;

  $(`${ids.filter}-preset`).onchange = (event) => {
    f.preset = event.target.value;
    if (f.preset !== "custom") { f.from = ""; f.to = ""; }
    renderDocList(kind);
  };
  $(ids.filter).onsubmit = (event) => event.preventDefault();
  for (const input of $(ids.filter).querySelectorAll('input[type="date"]')) {
    input.onchange = () => {
      const form = $(ids.filter);
      f.preset = "custom";
      f.from = form.querySelector('input[name="from"]').value;
      f.to = form.querySelector('input[name="to"]').value;
      state.docPage[kind] = 1;
      renderDocList(kind);
    };
  }
  for (const def of facetDefs) {
    const select = $(`${ids.facet}-${def.key}`);
    if (!select) continue;
    select.onchange = () => {
      facets[def.key] = select.value;
      state.docPage[kind] = 1;
      renderDocList(kind);
    };
  }
  if (k.newMenu) {
    $(`${ids.filter}`).querySelector(".new-doc")?.addEventListener("click", (event) => openRowMenu(event.currentTarget, k.newMenu));
  }
  $(ids.cols).onclick = () => { state.colsOpen[kind] = !colsOpen(kind); renderDocList(kind); };
  for (const box of $(ids.colsMenu).querySelectorAll("input[data-col]")) {
    box.onchange = () => {
      if (box.checked) chosen.add(box.dataset.col);
      else chosen.delete(box.dataset.col);
      state.colsOpen[kind] = true;
      saveUi();
      renderDocList(kind);
    };
  }
  $(`${ids.colsMenu}-reset`).onclick = () => {
    state.docCols[kind] = new Set(k.columnDefaults ?? DOC_COLUMN_DEFAULTS);
    delete state.docOrder[kind];
    saveUi();
    renderDocList(kind);
  };
  for (const button of $(ids.colsMenu).querySelectorAll("button[data-move]")) {
    button.onclick = () => {
      const i = order.indexOf(button.dataset.key);
      const j = button.dataset.move === "up" ? i - 1 : i + 1;
      if (j < 0 || j >= order.length) return;
      [order[i], order[j]] = [order[j], order[i]];
      state.colsOpen[kind] = true;
      saveUi();
      renderDocList(kind);
    };
  }
  $(ids.density).onclick = () => { state.density = docDensity() === "compact" ? "comfortable" : "compact"; saveUi(); renderDocList(kind); };
  $(ids.group)?.addEventListener("click", () => { state.groupByMonth[kind] = !grouped; renderDocList(kind); });
  // The pager is not rendered while grouped, so this control may not exist.
  $(`${k.container}-pagesize`)?.addEventListener("change", (event) => {
    state.pageSize = Number(event.target.value);
    state.docPage[kind] = 1;
    saveUi();
    renderDocList(kind);
  });
  for (const b of $(`tab-${k.container}`).querySelectorAll("[data-page]")) {
    b.onclick = () => {
      state.docPage[kind] = (state.docPage[kind] ?? 1) + (b.dataset.page === "next" ? 1 : -1);
      renderDocList(kind);
    };
  }

  // Selection: checkboxes in the first column, and an action bar that appears with the first pick.
  const selectedDocs = () => shown.filter((d) => docSelection(kind).has(d.id));
  const paintBar = () => {
    const bar = $(ids.bar);
    if (!bar) return;
    const picked = selectedDocs();
    bar.classList.toggle("hidden", picked.length === 0);
    $(`${ids.bar}-count`).textContent = picked.length;
    const postedPicked = picked.filter((d) => d.status === "posted");
    const draftsPicked = picked.length - postedPicked.length;
    const pickedTotal = postedPicked.length ? (k.summary ? k.summary(postedPicked) : amountTotal(postedPicked, amountKey, " · ")) : "";
    $(`${ids.bar}-total`).textContent = picked.length
      ? [pickedTotal, draftsPicked ? `${draftsPicked} ${draftsPicked === 1 ? "draft" : "drafts"} not counted` : ""].filter(Boolean).join(" · ")
      : "";
  };
  for (const box of $(`tab-${k.container}`).querySelectorAll(".row-check")) {
    box.onchange = () => {
      const sel = docSelection(kind);
      if (box.checked) sel.add(box.dataset.select);
      else sel.delete(box.dataset.select);
      paintBar();
    };
    box.onclick = (event) => event.stopPropagation();
  }
  $(ids.selectAll)?.addEventListener("change", (event) => {
    const sel = docSelection(kind);
    for (const d of grouped ? sorted : paged) {
      if (event.target.checked) sel.add(d.id);
      else sel.delete(d.id);
    }
    renderDocList(kind);
  });
  for (const button of $(ids.bar).querySelectorAll("[data-bulk]")) {
    button.onclick = async () => {
      const picked = selectedDocs();
      const which = button.dataset.bulk;
      if (which === "clear") { docSelection(kind).clear(); renderDocList(kind); return; }
      if (which === "print") {
        for (const d of picked) download(`${ledgerUrl()}/${k.path}/${d.id}/pdf`, `${k.prefix}-${numberOf(d)}.pdf`);
        return;
      }
      if (which === "delete") {
        const posted = picked.some((d) => d.status === "posted");
        if (!confirm(`Delete ${picked.length} ${picked.length === 1 ? k.noun : k.plural}?${posted ? " Posted ones lose their journals too." : ""}`)) return;
        try {
          for (const d of picked) await write("DELETE", `${ledgerUrl()}/${pathOf(d)}/${d.id}`, undefined, d.version);
          docSelection(kind).clear();
          await refreshLedger();
        } catch (error) {
          showError(error);
        }
      }
    };
  }
  paintBar();

  if (k.filterParty !== false) {
    const partyInput = $(ids.party);
    const partyList = $(ids.list);
    const partyRows = parties.filter(k.partyFilter ?? (() => true));
    let active = -1;
    const paintParties = (text) => {
      const needle = text.trim().toLowerCase();
      const matches = partyRows.filter((p) => !needle || `${p.name} ${p.tax_id ?? ""}`.toLowerCase().includes(needle)).slice(0, 40);
      partyList.innerHTML = `<button type="button" data-party="">All ${k.partyLabel.toLowerCase()}s</button>`
        + matches.map((p) => `<button type="button" data-party="${p.id}">${esc(p.name)}${p.tax_id ? ` <span class="muted small">${esc(p.tax_id)}</span>` : ""}</button>`).join("");
      partyList.hidden = false;
      active = -1;
    };
    const chooseParty = (id) => { f.party = id; partyList.hidden = true; state.docPage[kind] = 1; renderDocList(kind); };
    partyInput.onfocus = () => paintParties("");
    partyInput.oninput = () => paintParties(partyInput.value);
    partyInput.onkeydown = (event) => {
      const buttons = [...partyList.querySelectorAll("button")];
      if (event.key === "ArrowDown" || event.key === "ArrowUp") {
        event.preventDefault();
        active = Math.max(0, Math.min(buttons.length - 1, active + (event.key === "ArrowDown" ? 1 : -1)));
        buttons[active]?.focus();
      } else if (event.key === "Enter") {
        event.preventDefault();
        if (active >= 0) buttons[active].click();
      } else if (event.key === "Escape") {
        partyList.hidden = true;
        partyInput.blur();
      }
    };
    partyList.onmousedown = (event) => {
      const button = event.target.closest("button[data-party]");
      if (button) { event.preventDefault(); chooseParty(button.dataset.party); }
    };
    partyInput.onblur = () => setTimeout(() => {
      partyList.hidden = true;
      partyInput.value = f.party ? nameOf(f.party) : "";
    }, 120);
  }

  for (const button of $(`tab-${k.container}`).querySelectorAll("[data-clear]")) {
    button.onclick = () => {
      const what = button.dataset.clear;
      if (what === "party") f.party = "";
      if (what.startsWith("facet:")) delete facets[what.slice(6)];
      if (what === "q") f.q = "";
      if (what === "preset") { f.preset = "this-year"; f.from = ""; f.to = ""; }
      state.docPage[kind] = 1;
      renderDocList(kind);
    };
  }
  $(`${k.container}-clear`)?.addEventListener("click", () => {
    state.docFilters[kind] = undefined;
    renderDocList(kind);
  });

  // Search filters as it is typed, but never while an input method is composing: replacing the field
  // mid-composition aborts the IME and leaves the keyboard stuck.
  // Put the old search field back in place of the fresh one, so focus, caret and an IME composition in
  // progress all survive the redraw. Its listeners are attached once, on the first render.
  const freshSearch = $(ids.search);
  if (previousSearch) {
    freshSearch.replaceWith(previousSearch);
    if (!searchHadFocus) previousSearch.value = f.q;
    if (searchHadFocus) {
      previousSearch.focus();
      if (searchSelection) previousSearch.setSelectionRange(searchSelection[0], searchSelection[1]);
    }
  }
  const searchField = previousSearch ?? freshSearch;
  if (!searchField.dataset.wired) {
    searchField.dataset.wired = "1";
    let typing;
    let composing = false;
    const schedule = (value) => {
      clearTimeout(typing);
      typing = setTimeout(() => {
        if (composing) return;
        f.q = value.trim();
        state.docPage[kind] = 1;
        renderDocList(kind);
      }, 250);
    };
    searchField.addEventListener("compositionstart", () => { composing = true; clearTimeout(typing); });
    searchField.addEventListener("compositionend", (event) => { composing = false; schedule(event.target.value); });
    searchField.addEventListener("input", (event) => {
      if (composing || event.isComposing) return;
      schedule(event.target.value);
    });
  }

  for (const b of withTabs ? $(ids.tabs).querySelectorAll("button") : []) {
    b.onclick = () => { state.docTab[kind] = b.dataset.docTab; state.docPage[kind] = 1; docSelection(kind).clear(); renderDocList(kind); };
  }
  for (const th of $(`tab-${k.container}`).querySelectorAll("th.sortable")) {
    th.onclick = () => {
      const key = th.dataset.sort;
      if (sort.key === key) sort.dir = sort.dir === "asc" ? "desc" : "asc";
      else { sort.key = key; sort.dir = key === "invoice" ? "desc" : "asc"; }
      state.docPage[kind] = 1;
      renderDocList(kind);
    };
  }

  const openDoc = (doc) => (k.open ? k.open(doc, ctx) : k.side === "sale" ? showSale(doc, nameOf) : showPurchase(doc, nameOf));
  for (const tr of $(`tab-${k.container}`).querySelectorAll("tr[data-id]")) {
    tr.onclick = (event) => {
      if (event.target.closest(".row-menu-btn") || event.target.closest(".row-menu")) return;
      openDoc(sorted.find((d) => d.id === tr.dataset.id));
    };
  }
  for (const button of $(`tab-${k.container}`).querySelectorAll(".row-menu-btn")) {
    button.onclick = (event) => {
      event.stopPropagation();
      const doc = sorted.find((d) => d.id === button.dataset.menu);
      openRowMenu(button, [
        { label: "Open", run: () => openDoc(doc) },
        ...((k.canPrint ?? k.side === "sale") ? [{ label: "Print PDF", run: () => openPdf(`${k.path}/${doc.id}/pdf`) }] : []),
        ...(canEdit() && !k.noEdit && (!k.editWhen || k.editWhen(doc)) ? [{ label: "Edit", run: () => (k.edit ? k.edit(doc, ctx) : editDocument(k.side, doc.type, doc.id)) }] : []),
        ...(canEdit() ? [{
          label: "Delete", danger: true, run: async () => {
            if (!confirm(`Delete ${nounOf(doc)} ${numberOf(doc)}?${doc.status === "posted" ? " Its journal is removed too." : ""}`)) return;
            try {
              await write("DELETE", `${ledgerUrl()}/${pathOf(doc)}/${doc.id}`, undefined, doc.version);
              await refreshLedger();
            } catch (error) { showError(error); }
          },
        }] : []),
      ]);
    };
  }
}

// A master-data list: search, a role or status filter, sortable columns, no period, no tabs and no totals.
// Clicking a row opens its form; the footer counts the records, it does not add up money.
const SIMPLE_PAGE_SIZE = 50;

async function renderSimpleList(kind) {
  const k = SIMPLE_KINDS[kind];
  if (!k) return;
  state.simpleData ??= {};
  if (!state.simpleData[kind]) state.simpleData[kind] = await k.load();
  const data = state.simpleData[kind];
  state.simpleFilters ??= {};
  const f = state.simpleFilters[kind] ??= { q: "", role: "", status: "" };
  state.simpleSort ??= {};
  const sort = state.simpleSort[kind] ??= { key: k.columns[0].key, dir: "asc" };
  const searchIndex = (state.simpleSearchIndex ??= {});
  if (!searchIndex[kind]) {
    searchIndex[kind] = new Map(data.map((p) => [p.id, `${p.name} ${k.searchText(p)}`.toLowerCase()]));
  }
  const needle = f.q.trim().toLowerCase();
  const roleMatch = (p) => !f.role
    || (f.role === "both" ? p.is_customer && p.is_supplier : f.role === "customer" ? p.is_customer : p.is_supplier);
  const rows = data.filter((p) => roleMatch(p)
    && (!f.status || (f.status === "active" ? p.active : !p.active))
    && (!needle || searchIndex[kind].get(p.id)?.includes(needle)));
  const sortValue = (p) => {
    switch (sort.key) {
      case "name": return p.name.toLowerCase();
      case "roles": return partyRoles(p).toLowerCase();
      case "tax_id": return (p.tax_id ?? "").toLowerCase();
      case "terms": return Number(p.payment_terms_days ?? 0);
      case "status": return p.active ? 1 : 0;
      default: return "";
    }
  };
  const sorted = [...rows].sort((a, b) => {
    const va = sortValue(a), vb = sortValue(b);
    const cmp = typeof va === "number" && typeof vb === "number" ? va - vb : String(va).localeCompare(String(vb));
    return sort.dir === "desc" ? -cmp : cmp;
  });
  const pageCount = Math.max(1, Math.ceil(sorted.length / SIMPLE_PAGE_SIZE));
  state.simplePage ??= {};
  const page = Math.min(state.simplePage[kind] ?? 1, pageCount);
  state.simplePage[kind] = page;
  const paged = sorted.slice((page - 1) * SIMPLE_PAGE_SIZE, page * SIMPLE_PAGE_SIZE);

  const previousSearch = $(`${k.container}-search`);
  const searchHadFocus = previousSearch && document.activeElement === previousSearch;
  const searchSelection = searchHadFocus ? [previousSearch.selectionStart, previousSearch.selectionEnd] : null;
  setActions("");

  const header = k.columns.map((c) => {
    const cls = [c.num ? "num" : "", c.sortable ? "sortable" : "", c.sortable && sort.key === c.key ? "sorted" : ""].filter(Boolean).join(" ");
    const caret = c.sortable && sort.key === c.key ? `<span class="caret">${sort.dir === "asc" ? "▲" : "▼"}</span>` : "";
    return `<th class="${cls} col-${c.key}" ${c.sortable ? `data-sort="${c.key}"` : ""}${c.w ? ` style="width:${c.w}"` : ""}>${c.label}${caret}</th>`;
  }).join("");
  const body = paged.map((p) => {
    const values = k.cells(p);
    return `<tr ${canEdit() ? `class="click" data-party="${p.id}"` : ""}>${k.columns.map((c) => `<td class="${c.num ? "num" : ""} col-${c.key}">${values[c.key] ?? ""}</td>`).join("")}</tr>`;
  }).join("");
  const from = sorted.length ? (page - 1) * SIMPLE_PAGE_SIZE + 1 : 0;
  const to = Math.min(page * SIMPLE_PAGE_SIZE, sorted.length);
  const pager = `<div class="simple-meta">
    <span class="muted small">${sorted.length ? `${from}–${to} of ${sorted.length} ${sorted.length === 1 ? k.noun : k.plural}` : `0 ${k.plural}`}</span>
    <span class="spacer"></span>
    <span class="muted small">${canEdit() ? "Click a row to edit" : "Read only"}</span>
    ${pageCount > 1 ? `<button type="button" class="secondary small" data-page="prev" ${page === 1 ? "disabled" : ""}>‹</button>
      <span class="muted small">${page} / ${pageCount}</span>
      <button type="button" class="secondary small" data-page="next" ${page === pageCount ? "disabled" : ""}>›</button>` : ""}
  </div>`;

  const newButton = canEdit() && k.newAction ? `<button type="button" class="new-doc" onclick="${k.newAction}">+ ${esc(k.newLabel)}</button>` : "";
  const clear = f.q || f.role || f.status ? `<button type="button" class="link" id="${k.container}-simple-clear">Clear</button>` : "";
  const empty = `<p class="muted">No ${k.plural}${f.role ? " with that role" : ""}${f.status ? ` that are ${f.status}` : ""}${f.q ? ` matching “${esc(f.q)}”` : ""}.</p>`;

  $(`tab-${k.container}`).innerHTML = `
    <form class="filter-bar" id="${k.container}-simple-filter" autocomplete="off">
      ${newButton}
      <label>Role <select id="${k.container}-role">
        <option value="">All roles</option>
        <option value="customer" ${f.role === "customer" ? "selected" : ""}>Customers</option>
        <option value="supplier" ${f.role === "supplier" ? "selected" : ""}>Suppliers</option>
        <option value="both" ${f.role === "both" ? "selected" : ""}>Customers and suppliers</option>
      </select></label>
      <label>Status <select id="${k.container}-status">
        <option value="">All statuses</option>
        <option value="active" ${f.status === "active" ? "selected" : ""}>Active</option>
        <option value="inactive" ${f.status === "inactive" ? "selected" : ""}>Inactive</option>
      </select></label>
      <label class="search">Search <input id="${k.container}-search" name="q" value="${esc(f.q)}" placeholder="${esc(k.searchHint)}"></label>
      ${clear}
    </form>
    ${sorted.length ? `<div class="table-scroll"><table class="list simple"><tr>${header}</tr>${body}</table></div>${pager}` : empty}`;

  $(`${k.container}-role`).onchange = (event) => {
    f.role = event.target.value;
    state.simplePage[kind] = 1;
    renderSimpleList(kind);
  };
  $(`${k.container}-status`).onchange = (event) => {
    f.status = event.target.value;
    state.simplePage[kind] = 1;
    renderSimpleList(kind);
  };
  $(`${k.container}-simple-clear`)?.addEventListener("click", () => {
    f.q = "";
    f.role = "";
    f.status = "";
    state.simplePage[kind] = 1;
    renderSimpleList(kind);
  });

  // Keep the search field itself across redraws, as the document lists do, so focus, caret and an IME
  // composition survive a keystroke.
  const freshSearch = $(`${k.container}-search`);
  if (previousSearch) {
    freshSearch.replaceWith(previousSearch);
    if (!searchHadFocus) previousSearch.value = f.q;
    if (searchHadFocus) {
      previousSearch.focus();
      if (searchSelection) previousSearch.setSelectionRange(searchSelection[0], searchSelection[1]);
    }
  }
  const searchField = previousSearch ?? freshSearch;
  if (!searchField.dataset.wired) {
    searchField.dataset.wired = "1";
    let typing;
    let composing = false;
    const schedule = (value) => {
      clearTimeout(typing);
      typing = setTimeout(() => {
        if (composing) return;
        f.q = value.trim();
        state.simplePage[kind] = 1;
        renderSimpleList(kind);
      }, 250);
    };
    searchField.addEventListener("compositionstart", () => { composing = true; clearTimeout(typing); });
    searchField.addEventListener("compositionend", (event) => { composing = false; schedule(event.target.value); });
    searchField.addEventListener("input", (event) => {
      if (composing || event.isComposing) return;
      schedule(event.target.value);
    });
  }

  for (const th of $(`tab-${k.container}`).querySelectorAll("th.sortable")) {
    th.onclick = () => {
      const key = th.dataset.sort;
      if (sort.key === key) sort.dir = sort.dir === "asc" ? "desc" : "asc";
      else { sort.key = key; sort.dir = "asc"; }
      state.simplePage[kind] = 1;
      renderSimpleList(kind);
    };
  }
  for (const b of $(`tab-${k.container}`).querySelectorAll("[data-page]")) {
    b.onclick = () => {
      state.simplePage[kind] = (state.simplePage[kind] ?? 1) + (b.dataset.page === "next" ? 1 : -1);
      renderSimpleList(kind);
    };
  }
  for (const tr of $(`tab-${k.container}`).querySelectorAll("tr[data-party]")) {
    tr.onclick = () => k.open(data.find((p) => p.id === tr.dataset.party));
  }
}

// A small menu under the row button; it closes on the next click anywhere else.
function openRowMenu(anchor, items) {
  document.querySelector(".row-menu")?.remove();
  const menu = document.createElement("div");
  menu.className = "row-menu";
  menu.innerHTML = items.map((item, i) => `<button type="button" data-item="${i}" class="${item.danger ? "danger" : ""}">${esc(item.label)}</button>`).join("");
  document.body.appendChild(menu);
  const rect = anchor.getBoundingClientRect();
  menu.style.top = `${rect.bottom + window.scrollY + 4}px`;
  menu.style.left = `${Math.max(8, rect.right + window.scrollX - menu.offsetWidth)}px`;
  const close = () => { menu.remove(); document.removeEventListener("mousedown", outside); };
  const outside = (event) => { if (!menu.contains(event.target)) close(); };
  menu.querySelectorAll("button").forEach((b) => {
    b.onclick = () => { close(); items[Number(b.dataset.item)].run(); };
  });
  setTimeout(() => document.addEventListener("mousedown", outside), 0);
}
