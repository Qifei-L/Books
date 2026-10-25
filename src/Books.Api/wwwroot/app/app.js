// Preview of Books, for local testing only. Plain JavaScript, no build step; data entry is in forms.js.
const state = { token: sessionStorage.getItem("books.token"), org: null, ledger: null, accounts: [] };
const $ = (id) => document.getElementById(id);

// What the reader chose — visible columns, their order, row density, rows per page — lives in the
// browser, not on the server: it is a reading preference, not part of the books.
const UI_KEY = "books.ui.v1";

function loadUi() {
  try {
    const saved = JSON.parse(localStorage.getItem(UI_KEY) ?? "null");
    if (!saved) return;
    state.docCols = Object.fromEntries(Object.entries(saved.cols ?? {}).map(([kind, keys]) => [kind, new Set(keys)]));
    state.docOrder = saved.order ?? {};
    if (saved.density) state.density = saved.density;
    if (saved.pageSize) state.pageSize = saved.pageSize;
  } catch {
    // A broken or unreadable value must not stop the app; the defaults take over.
  }
}

function saveUi() {
  try {
    localStorage.setItem(UI_KEY, JSON.stringify({
      cols: Object.fromEntries(Object.entries(state.docCols ?? {}).map(([kind, keys]) => [kind, [...keys]])),
      order: state.docOrder ?? {},
      density: state.density,
      pageSize: state.pageSize,
    }));
  } catch {
    // Private mode or a full quota: the choice simply does not persist.
  }
}
const esc = (s) => String(s ?? "").replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[c]);

async function api(path, options = {}) {
  const response = await fetch(path, {
    ...options,
    headers: { "Content-Type": "application/json", ...(state.token ? { Authorization: `Bearer ${state.token}` } : {}), ...options.headers },
  });
  const body = response.status === 204 ? null : await response.json();
  if (!response.ok) {
    if (response.status === 401) signOut();
    throw new Error(body?.detail ?? body?.title ?? `HTTP ${response.status}`);
  }
  return body.data;
}

function showError(error) {
  $("error").textContent = error.message;
  $("error").classList.remove("hidden");
}

function signOut() {
  sessionStorage.removeItem("books.token");
  state.token = null;
  $("home").classList.add("hidden");
  $("signin").classList.remove("hidden");
}

$("signin-form").addEventListener("submit", async (event) => {
  event.preventDefault();
  const form = new FormData(event.target);
  const response = await fetch("/dev/token", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ subject: form.get("subject"), email: form.get("email"), name: form.get("subject") }),
  });
  if (!response.ok) return showError(new Error("The local issuer is not available (Development only)."));
  state.token = (await response.json()).access_token;
  sessionStorage.setItem("books.token", state.token);
  start();
});

$("demo").addEventListener("click", async () => {
  $("demo").disabled = true;
  $("demo").textContent = "Creating…";
  try {
    const result = await fetch("/dev/demo", { method: "POST", headers: { Authorization: `Bearer ${state.token}` } });
    if (!result.ok) throw new Error("Demo data could not be created.");
    const { data } = await result.json();
    await loadOrganizations(data.organization_id);
  } catch (error) {
    showError(error);
  } finally {
    $("demo").disabled = false;
    $("demo").textContent = "New demo";
  }
});

async function start() {
  if (!state.token) return signOut();
  try {
    const me = await api("/api/v1/me");
    $("who").textContent = `${me.display_name} · ${me.email ?? "no verified email"}`;
    $("signin").classList.add("hidden");
    $("home").classList.remove("hidden");
    await loadOrganizations();
  } catch (error) {
    showError(error);
  }
}

// The organization and company are picked in the header; the page itself is the chosen company's books.
async function loadOrganizations(select) {
  const orgs = await api("/api/v1/me/organizations");
  $("switcher").classList.remove("hidden");
  $("org-pick").innerHTML = orgs.length
    ? orgs.map((o) => `<option value="${o.id}">${esc(o.name)} (${esc(o.role)})</option>`).join("")
    : `<option value="">No organizations yet</option>`;
  $("org-pick").onchange = (e) => chooseOrganization(e.target.value);
  const first = select ?? orgs[0]?.id;
  if (first) await chooseOrganization(first);
  else showLedger(false);
}

async function chooseOrganization(orgId) {
  state.org = orgId;
  $("org-pick").value = orgId;
  const role = (await api("/api/v1/me/organizations")).find((o) => o.id === orgId)?.role;
  const ledgers = await api(`/api/v1/organizations/${orgId}/ledgers`);
  // Another organization was picked while this one loaded: leave the page to that one.
  if (state.org !== orgId) return;
  state.role = role;
  $("ledger-pick").innerHTML = ledgers.map((l) => `<option value="${l.id}">${esc(l.company_name)}</option>`).join("");
  $("ledger-pick").onchange = (e) => chooseLedger(ledgers.find((l) => l.id === e.target.value));
  if (ledgers[0]) await chooseLedger(ledgers[0]);
  else showLedger(false);
}

function showLedger(shown) {
  $("ledger").classList.toggle("hidden", !shown);
  $("no-ledger").classList.toggle("hidden", shown);
  $("ledger-pick").classList.toggle("hidden", !shown);
  if (!shown) $("ledger-meta").textContent = "";
}

const ledgerUrl = () => `/api/v1/organizations/${state.org}/ledgers/${state.ledger.id}`;

async function chooseLedger(ledger) {
  state.ledger = ledger;
  $("ledger-pick").value = ledger.id;
  showLedger(true);
  const months = ["", "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
  $("ledger-meta").textContent = `${ledger.base_currency} · FY from ${months[ledger.fy_start_month]} · `
    + (ledger.closed_through ? `closed to ${ledger.closed_through}` : "not closed") + (ledger.effective_access === "edit" ? "" : " · read only");
  state.rendered = {};
  // The cached document lists belong to the previous company.
  state.docData = {};
  state.docSearchIndex = {};
  state.banks = null;
  state.simpleData = {};
  state.simpleSearchIndex = {};
  state.simplePage = {};
  await loadAccounts();
  await showTab(state.tab ?? "sales-invoices", true);
}

// Drilldown: a report's account opens its GL detail for the same period; a GL line opens its journal.
async function openGl(accountId, from, to) {
  const f = $("gl-form");
  f.account.value = accountId;
  f.from.value = from;
  f.to.value = to;
  await showTab("gl");
  f.requestSubmit();
}

const SOURCES = {
  purchase_invoice: "Purchase invoice", purchase_debit_note: "Debit note", sales_invoice: "Sales invoice", sales_credit_note: "Credit note",
  payment: "Payment", receipt: "Receipt", bank_transfer: "Bank transfer", pay_run: "Pay run", stock_adjustment: "Stock adjustment",
  fx_revaluation: "Exchange revaluation", fx_revaluation_reversal: "Revaluation reversal",
};

async function openJournal(id) {
  try {
    showJournal(await api(`${ledgerUrl()}/journals/${id}`));
  } catch (error) {
    showError(error);
  }
}

async function openJournalNo(no) {
  const journal = (await api(`${ledgerUrl()}/journals`)).find((j) => j.journal_no === Number(no));
  if (journal) showJournal(journal);
}

const DOC_LISTS = {
  sales_invoice: "sales-invoices", sales_credit_note: "credit-notes", purchase_invoice: "purchase-invoices", purchase_debit_note: "debit-notes",
  payment: "payments", receipt: "receipts", pay_run: "pay-runs", bank_transfer: "transfers", stock_adjustment: "stock-adjustments",
};

// Opens whatever produced a posting: the document's own view where there is one, otherwise its journal.
async function openDocument(kind, id) {
  try {
    if (kind === "journal" || !DOC_LISTS[kind]) return openJournal(id);
    const [docs, parties] = await Promise.all([api(`${ledgerUrl()}/${DOC_LISTS[kind]}`), api(`${ledgerUrl()}/parties`)]);
    const party = (pid) => parties.find((p) => p.id === pid)?.name ?? "";
    const doc = docs.find((d) => d.id === id);
    if (!doc) return;
    if (kind.startsWith("sales")) showSale(doc, party);
    else if (kind.startsWith("purchase")) showPurchase(doc, party);
    else if (kind === "payment" || kind === "receipt") showPayment(doc, party);
    else if (kind === "pay_run") {
      $("journal-dialog").close();
      await showTab("payroll");
      showPayRun(doc);
    }
    else if (kind === "stock_adjustment") {
      const items = await api(`${ledgerUrl()}/items`);
      showAdjustment(doc, (iid) => items.find((i) => i.id === iid)?.name ?? "");
    }
    else if (doc.journal_id) openJournal(doc.journal_id);
  } catch (error) {
    showError(error);
  }
}

// Warnings never stop a save (for example stock going below zero); they come back with the saved document.
const warningsHtml = (d) => (d.warnings ?? []).map((w) => `<div class="warning">${esc(w)}</div>`).join("");

const journalButton = (d) => d.journal_id ? `<p><button class="secondary" onclick="openJournal('${d.journal_id}')">Journal ${d.journal_no ?? ""}</button></p>` : "";

function showPayment(d, party) {
  $("journal-body").innerHTML = `<h3>${d.type === "receipt" ? "Receipt" : "Payment"} ${d.payment_no} <span class="pill ${d.status}">${d.status}</span></h3>
    <p class="muted">${esc(accountName(d.bank_account_id))} · ${d.date} · ${esc(party(d.party_id))} · ${esc(d.reference ?? "")} · ${esc(d.description ?? "")}
      ${d.exchange_rate ? ` · ${d.currency} at ${d.exchange_rate}` : ""}</p>
    <table><tr><th>Category</th><th>Description</th><th class="num">Amount</th><th class="num">VAT</th><th class="num">WHT</th><th class="num">Total</th></tr>
    ${d.lines.map((l) => `<tr><td>${l.kind === "account" ? esc(accountName(l.account_id)) : `${esc(accountName(l.control_account_id))} · ${l.kind === "invoice" ? "settles an invoice" : "party balance"}`}</td>
      <td>${esc(l.description ?? "")}</td><td class="num">${l.amount}</td><td class="num">${blank(l.tax_amount)}</td><td class="num">${blank(l.withholding_amount)}</td><td class="num">${l.total}</td></tr>`).join("")}
    <tr class="total"><td colspan="5">Total</td><td class="num">${d.total} ${d.currency ?? ""}</td></tr></table>
    ${d.issues.map((i) => `<div class="warning">${esc(i.code)}: ${esc(i.message)}</div>`).join("")}${warningsHtml(d)}${journalButton(d)}
    ${canEdit() ? `<p><button class="secondary" onclick="editPayment('${d.type}', '${d.id}')">Edit</button></p>` : ""}`;
  $("journal-dialog").showModal();
}

// Transfers have no party and no line grid; the detail is the two accounts, what was sent and received,
// the fee and the journal. Editing is a form of its own (setup.js).
function showTransfer(d) {
  const from = `${accountName(d.from_account_id)} (${bankCurrency(d.from_account_id)})`;
  const to = `${accountName(d.to_account_id)} (${bankCurrency(d.to_account_id)})`;
  $("journal-body").innerHTML = `<h3>Bank transfer ${d.transfer_no} <span class="pill ${d.status}">${d.status}</span></h3>
    <p class="muted">${d.date} · ${esc(from)} → ${esc(to)} · ${esc(d.reference ?? "")} · ${esc(d.description ?? "")}</p>
    <table><tr><th>From</th><th>To</th><th class="num">Sent</th><th class="num">Received</th><th class="num">Fee</th></tr>
      <tr><td>${esc(from)}</td><td>${esc(to)}</td><td class="num">${money(d.amount, bankCurrency(d.from_account_id))} ${bankCurrency(d.from_account_id)}</td>
      <td class="num">${d.to_amount ? `${money(d.to_amount, bankCurrency(d.to_account_id))} ${bankCurrency(d.to_account_id)}` : "same currency"}</td>
      <td class="num">${Number(d.fee_amount) ? money(d.fee_amount, bankCurrency(d.from_account_id)) : "—"}</td></tr></table>
    ${d.issues.map((i) => `<div class="warning">${esc(i.code)}: ${esc(i.message)}</div>`).join("")}${journalButton(d)}
    ${canEdit() ? `<p><button class="secondary" onclick="editTransfer('${d.id}')">Edit</button></p>` : ""}`;
  $("journal-dialog").showModal();
}

async function editTransfer(id) {
  $("journal-dialog").close();
  const transfer = (await api(`${ledgerUrl()}/transfers`)).find((t) => t.id === id);
  if (transfer) transferForm(transfer);
}

async function showStatement(kind, partyId, asOf) {
  try {
    const s = await api(`${ledgerUrl()}/reports/${kind}-statement?${kind}Id=${partyId}&from=${asOf.slice(0, 4)}-01-01&to=${asOf}`);
    $("journal-body").innerHTML = `<h3>${esc(s.supplier ?? s.customer)} · statement</h3>
      <p class="muted">${s.from} – ${s.to} · ${s.currency}</p>
      <table><tr><th>Date</th><th>Type</th><th>No.</th><th>Reference</th><th>Due</th><th class="num">Amount</th><th class="num">Balance</th></tr>
      <tr><td colspan="6" class="muted">Opening balance</td><td class="num">${s.opening_balance}</td></tr>
      ${s.items.map((i) => `<tr><td>${i.date}</td><td>${i.type.replaceAll("_", " ")}</td><td>${i.number}</td><td>${esc(i.reference ?? "")}</td><td>${i.due_date}</td>
        <td class="num">${i.amount}</td><td class="num">${i.balance}</td></tr>`).join("")}
      <tr class="total"><td colspan="6">Closing balance</td><td class="num">${s.closing_balance}</td></tr></table>`;
    $("journal-dialog").showModal();
  } catch (error) {
    showError(error);
  }
}

// The sidebar: groups of modules. A module with children opens into its own pages and stays open while
// one of them is shown; a module without children is a page itself.
const NAV = [
  { group: "Business", items: [
    { id: "sales", label: "Sales", children: [
      { id: "sales-invoices", label: "Invoices" },
      { id: "sales-credit-notes", label: "Credit notes" },
      { id: "sales-reports", label: "Reports" },
      { id: "sales-settings", label: "Settings" },
    ] },
    { id: "purchases", label: "Purchases", children: [
      { id: "purchases-invoices", label: "Invoices" },
      { id: "purchases-debit-notes", label: "Debit notes" },
      { id: "purchases-reports", label: "Reports" },
    ] },
    { id: "bank", label: "Cash and bank", children: [
      { id: "bank-transactions", label: "Transactions" },
      { id: "bank-statements", label: "Statements" },
    ] },
    { id: "payroll", label: "Payroll", children: [
      { id: "payroll-runs", label: "Pay runs" },
      { id: "payroll-employees", label: "Employees" },
    ] },
  ] },
  { group: "Stock and assets", items: [
    { id: "inventory", label: "Inventory", children: [
      { id: "inventory-stock", label: "Stock on hand" },
      { id: "inventory-adjustments", label: "Adjustments" },
      { id: "inventory-transfers", label: "Transfers" },
      { id: "inventory-assemblies", label: "Assemblies" },
    ] },
    { id: "assets", label: "Fixed assets", children: [
      { id: "assets-register", label: "Assets" },
      { id: "assets-runs", label: "Depreciation runs" },
    ] },
  ] },
  { group: "Records", items: [
    { id: "parties", label: "Business parties" },
  ] },
  { group: "Books", items: [
    { id: "journals", label: "Journals" },
    { id: "chart", label: "Chart of accounts" },
  ] },
  { group: "Reports", items: [
    { id: "pl", label: "Profit and loss" },
    { id: "bs", label: "Balance sheet" },
    { id: "tb", label: "Trial balance" },
    { id: "gl", label: "GL detail" },
  ] },
  { group: "Tax", items: [{ id: "tax", label: "Tax return" }] },
  { group: "Settings", items: [
    { id: "settings-ledger", label: "Ledger and periods" },
    { id: "settings-org", label: "Organization" },
  ] },
];

const allItems = () => NAV.flatMap((g) => g.items);
const moduleOf = (id) => allItems().find((item) => item.id === id || item.children?.some((c) => c.id === id));
const firstPageOf = (moduleId) => allItems().find((item) => item.id === moduleId)?.children?.[0]?.id ?? moduleId;

function buildNav() {
  const nav = $("sidenav");
  nav.innerHTML = NAV.map((g) => `
    <div class="nav-group">${esc(g.group)}</div>
    ${g.items.map((item) => (item.children
      ? `<div class="nav-module" data-module="${item.id}">
           <button class="module" data-module="${item.id}">${esc(item.label)}</button>
           <div class="nav-children">${item.children.map((c) => `<button data-tab="${c.id}">${esc(c.label)}</button>`).join("")}</div>
         </div>`
      : `<button data-tab="${item.id}">${esc(item.label)}</button>`)).join("")}`).join("");
  for (const b of nav.querySelectorAll("button[data-tab]")) b.onclick = () => showTab(b.dataset.tab);
  for (const b of nav.querySelectorAll("button.module")) b.onclick = () => showTab(firstPageOf(b.dataset.module));
}

// One register drives the sidebar, the page heading and what each page loads. A page renders the first
// time it is opened, so switching companies no longer fetches every list at once.
const PAGES = {
  "sales-invoices": { title: "Sales invoices", sub: "Sales · posted and draft invoices, and what they add up to", render: () => renderDocList("invoice") },
  "sales-credit-notes": { title: "Sales credit notes", sub: "Sales · credits issued, and the invoice each one reduces", render: () => renderDocList("credit_note") },
  "sales-reports": { title: "Sales reports", sub: "Sales · who owes us, how late, and their statements", render: () => renderAging("sales") },
  "sales-settings": { title: "Sales settings", sub: "Sales · the seller block and the numbers printed on invoices", render: () => renderSalesSettings() },
  "purchases-invoices": { title: "Purchase invoices", sub: "Purchases · posted and draft bills, and what we owe", render: () => renderDocList("purchase_invoice") },
  "purchases-debit-notes": { title: "Purchase debit notes", sub: "Purchases · debits raised against a bill", render: () => renderDocList("debit_note") },
  "purchases-reports": { title: "Purchases reports", sub: "Purchases · who we owe, how late, and their statements", render: () => renderAging("purchases") },
  "bank-transactions": { title: "Bank transactions", sub: "Cash and bank · payments, receipts and transfers for one account, with money in and out", render: () => renderDocList("transaction") },
  "bank-statements": { title: "Statements", sub: "Cash and bank · the bank's balance against the books, and the lines behind it", render: () => renderBankStatements() },
  "payroll-runs": { title: "Pay runs", sub: "Payroll · results received from the payroll system; Books does not calculate pay", render: () => renderDocList("pay_run") },
  "payroll-employees": { title: "Employees", sub: "Payroll · the people in the imported pay runs, with their pay history", render: () => renderEmployees() },
  "inventory-stock": { title: "Stock on hand", sub: "Inventory · quantities, items and locations", render: () => renderInventoryStock() },
  "inventory-adjustments": { title: "Stock adjustments", sub: "Inventory · write-offs, stock found and counts", render: () => renderDocList("stock_adjustment") },
  "inventory-transfers": { title: "Stock transfers", sub: "Inventory · quantities moved between locations", render: () => renderDocList("stock_transfer") },
  "inventory-assemblies": { title: "Assemblies", sub: "Inventory · items made from a bill of materials", render: () => renderDocList("assembly") },
  "assets-register": { title: "Fixed asset register", sub: "Fixed assets · cost, accumulated depreciation and net book value", render: () => renderAssets() },
  "assets-runs": { title: "Depreciation runs", sub: "Fixed assets · one posted journal for each month's depreciation", render: () => renderDocList("depreciation_run") },
  parties: { title: "Business parties", sub: "Records · customers and suppliers are one business party, with tax ID and payment terms", render: () => renderSimpleList("party") },
  journals: { title: "Journals", sub: "Every posting, with its revision and its source document", render: () => renderDocList("journal") },
  chart: { title: "Chart of accounts", sub: "Accounts carry the balances; a bank or cash account is an Assets account with bank details", render: () => renderChart() },
  pl: { title: "Profit and loss", sub: "Income and cost and expense over a period", render: () => runReport("pl") },
  bs: { title: "Balance sheet", sub: "Assets, liabilities and equity at a date", render: () => runReport("bs") },
  tb: { title: "Trial balance", sub: "Opening, period and closing balances by account", render: () => runReport("tb") },
  gl: { title: "GL detail", sub: "Every line in one account, with a running balance", render: () => runReport("gl") },
  tax: { title: "Tax return", sub: "The month's return on the GDT forms, the tax codes behind it, and filing", render: () => renderTax(state.taxPeriod ?? "2026-10") },
  "settings-ledger": { title: "Ledger and periods", sub: "Currency, fiscal year, close date and year-end revaluation", render: () => renderSettingsLedger() },
  "settings-org": { title: "Organization", sub: "The organization, its companies and the exchange rates they share", render: () => renderSettingsOrg() },
};

function runReport(id) {
  const form = $(`${id}-form`);
  if (form) form.requestSubmit();
}

// A page puts its primary actions in the header; the body holds the data. Actions are remembered per
// page so returning to a page that is already rendered restores them without rendering again.
const setActions = (html) => {
  const value = html ?? "";
  if (state.tab && PAGES[state.tab]) PAGES[state.tab].actions = value;
  $("page-actions").innerHTML = value;
};

async function showTab(id, force = false) {
  const page = PAGES[id];
  if (!page) return;
  state.tab = id;
  const module = moduleOf(id);
  const moduleId = module?.id;
  for (const wrap of document.querySelectorAll(".nav-module")) {
    const open = wrap.dataset.module === moduleId && !!module?.children;
    wrap.classList.toggle("open", open);
    wrap.querySelector("button.module")?.classList.toggle("active", open);
  }
  for (const b of document.querySelectorAll("#sidenav button[data-tab]")) b.classList.toggle("active", b.dataset.tab === id);
  for (const t of document.querySelectorAll(".tab")) t.classList.toggle("hidden", t.id !== `tab-${id}`);
  $("page-title").textContent = page.title;
  $("page-sub").textContent = page.sub;
  $("page-actions").innerHTML = page.actions ?? "";
  state.rendered ??= {};
  state.rendered ??= {};
  if (!force && state.rendered[id]) return;
  state.rendered[id] = true;
  try {
    await page.render();
  } catch (error) {
    state.rendered[id] = false;
    showError(error);
  }
}

// The column panel closes with Escape or a click outside it, and stays open while a column is moved.
const kindOfContainer = (container) => Object.keys(DOC_KINDS).find((key) => DOC_KINDS[key].container === container);

document.addEventListener("keydown", (event) => {
  if (event.key !== "Escape") return;
  const kind = kindOfContainer(state.tab);
  if (kind && colsOpen(kind)) { state.colsOpen[kind] = false; renderDocList(kind); }
});

document.addEventListener("mousedown", (event) => {
  const kind = kindOfContainer(state.tab);
  if (!kind || !colsOpen(kind)) return;
  if (event.target.closest(".popover") || event.target.closest(`#${DOC_KINDS[kind].container}-cols`)) return;
  state.colsOpen[kind] = false;
  renderDocList(kind);
});

loadUi();
buildNav();

// Every page names accounts, so groups, accounts and purposes load once per company.
async function loadAccounts() {
  const [groups, accounts, purposes] = await Promise.all([
    api(`${ledgerUrl()}/groups`), api(`${ledgerUrl()}/accounts`), api(`${ledgerUrl()}/purposes`),
  ]);
  state.groups = groups;
  state.accounts = accounts;
  state.purposes = purposes;
  state.purposeOf = Object.fromEntries(purposes.filter((p) => p.account_id).map((p) => [p.account_id, p.purpose.replaceAll("_", " ")]));
  $("gl-form").account.innerHTML = accounts.map((a) => `<option value="${a.id}">${esc(a.code)} ${esc(a.name)}</option>`).join("");
}

const TYPES = { assets: "Assets", liabilities: "Liabilities", equity: "Equity", income: "Income", cost_and_expense: "Cost & Expense" };

// The chart of accounts is also where a bank or cash account is set up: a bank account is an Assets
// account with bank details, so the chart shows those details and its form edits both in one place.
async function renderChart() {
  await loadAccounts();
  const banks = await api(`${ledgerUrl()}/bank-accounts`);
  state.banks = banks;
  const groups = state.groups ?? [];
  const accounts = state.accounts ?? [];
  const purposeOf = state.purposeOf ?? {};
  const bankOf = (id) => banks.find((b) => b.account_id === id);
  setActions(canEdit() ? `<button onclick="accountForm()">New account</button>` : "");
  const rows = [];
  for (const [type, label] of Object.entries(TYPES)) {
    const typeGroups = groups.filter((g) => g.type === type);
    const loose = accounts.filter((a) => a.type === type && !a.group_id);
    if (!typeGroups.length && !loose.length) continue;
    rows.push(`<tr class="group"><td colspan="6">${label}</td></tr>`);
    for (const group of typeGroups) {
      rows.push(`<tr><td colspan="6"><strong>${esc(group.name)}</strong> <span class="muted small">group, never posted to</span></td></tr>`);
      for (const a of accounts.filter((x) => x.group_id === group.id)) rows.push(accountRow(a, purposeOf, true, bankOf(a.id)));
    }
    for (const a of loose) rows.push(accountRow(a, purposeOf, false, bankOf(a.id)));
  }
  $("tab-chart").innerHTML = `<p class="muted small">Groups are headings that never take postings. Click an account to edit it, including its bank details; <b>GL</b> opens its detail.</p>
    <table class="list chart"><tr><th class="col-code">Code</th><th>Account</th><th class="col-status">Status</th><th class="col-purpose">System purpose</th><th class="col-bank">Bank details</th><th class="col-actions"></th></tr>${rows.join("")}</table>`;
  const year = new Date().getFullYear();
  for (const tr of $("tab-chart").querySelectorAll("tr[data-account]")) {
    tr.onclick = () => canEdit()
      ? openAccount(tr.dataset.account)
      : openGl(tr.dataset.account, `${year}-01-01`, `${year}-12-31`);
  }
  for (const button of $("tab-chart").querySelectorAll("button[data-gl]")) {
    button.onclick = (event) => {
      event.stopPropagation();
      openGl(button.dataset.gl, `${year}-01-01`, `${year}-12-31`);
    };
  }
}

function accountRow(a, purposeOf, indented, bank) {
  const status = [a.active ? "active" : "inactive", a.used ? "used" : null].filter(Boolean).join(", ");
  const bankDetails = bank
    ? `<div class="cell-main"><span class="pill posted">bank</span> <span class="cell-clip" title="${esc(`${bank.bank_name}${bank.account_number ? ` · ${bank.account_number}` : ""} · ${bank.currency}`)}">${esc(bank.bank_name)}</span></div>
      <div class="cell-sub">${bank.account_number ? `${esc(bank.account_number)} · ` : ""}${esc(bank.currency)}</div>`
    : a.is_bank ? `<span class="pill">bank</span>` : "";
  return `<tr class="click" data-account="${a.id}">
    <td class="col-code">${esc(a.code)}</td>
    <td class="col-name" style="padding-left:${indented ? 24 : 8}px">${esc(a.name)}</td>
    <td class="muted col-status">${status}</td>
    <td class="col-purpose">${esc(purposeOf[a.id] ?? "")}</td>
    <td class="col-bank">${bankDetails}</td>
    <td class="col-actions"><button type="button" class="secondary small" data-gl="${a.id}" title="GL detail">GL</button></td>
  </tr>`;
}

function openAccount(id) {
  const account = (state.accounts ?? []).find((a) => a.id === id);
  if (account) accountForm(account);
}

/** New or existing account, plus its bank details when it is an Assets account used as a bank. */
async function accountForm(a) {
  try {
    const groups = state.groups ?? [];
    const banks = state.banks ?? await api(`${ledgerUrl()}/bank-accounts`);
    const bank = a ? banks.find((b) => b.account_id === a.id) : null;
    const base = state.ledger.base_currency;
    const typeOptions = Object.entries(TYPES)
      .map(([value, label]) => `<option value="${value}" ${(a?.type ?? "assets") === value ? "selected" : ""}>${label}</option>`).join("");
    const form = openForm(a ? `Edit ${a.code} ${a.name}` : "New account", `
      <div class="grid">
        <label>Code <input name="code" value="${esc(a?.code ?? "")}" placeholder="1130" required></label>
        <label class="wide">Name <input name="name" value="${esc(a?.name ?? "")}" required></label>
        <label>Type <select name="type" ${bank ? "disabled" : ""}>${typeOptions}</select></label>
        <label>Group <select name="group_id"></select></label>
        ${a ? `<label class="check"><input type="checkbox" name="active" ${a.active ? "checked" : ""}> Active</label>` : ""}
      </div>
      <div id="account-bank-section">
        <h4>Bank or cash account</h4>
        <p class="muted small">An Assets account can also be a bank or cash account. Payments, receipts, transfers and statements use it. Its currency is fixed once set.</p>
        ${bank ? `<p class="muted small">Currency ${esc(bank.currency)} (fixed)</p>` : `<label class="check"><input type="checkbox" name="is_bank"> This is a bank or cash account</label>`}
        <div class="grid" id="account-bank-fields">
          <label class="wide">Bank <input name="bank_name" value="${esc(bank?.bank_name ?? "")}" placeholder="ABA Bank, Cash"></label>
          <label>Account number <input name="account_number" value="${esc(bank?.account_number ?? "")}"></label>
          ${bank ? "" : `<label>Currency <select name="bank_currency">${[...new Set([base, "USD", "KHR"])].map((c) => `<option>${c}</option>`).join("")}</select></label>`}
        </div>
      </div>
      <p class="actions"><span class="spacer"></span>
        <button type="button" class="secondary" data-cancel>Cancel</button>
        <button type="submit">Save</button></p>`);

    const fillGroups = (selected) => {
      const list = groups.filter((g) => g.type === form.elements.type.value);
      form.elements.group_id.innerHTML = `<option value="">No group</option>`
        + list.map((g) => `<option value="${g.id}" ${g.id === selected ? "selected" : ""}>${esc(g.name)}</option>`).join("");
    };
    const canHaveBank = () => !!bank || !!a?.is_bank || !a
      || (a.type === "assets" && a.postable && !a.is_control && !a.is_bank && (a.usages ?? []).length === 0);
    const toggleBank = () => {
      const assets = form.elements.type.value === "assets";
      const allowed = canHaveBank() && assets;
      const check = form.elements.is_bank;
      const wanted = !!bank || (allowed && !!check?.checked);
      form.querySelector("#account-bank-section").classList.toggle("hidden", !allowed);
      check?.closest("label")?.classList.toggle("hidden", !allowed || !!bank);
      form.querySelector("#account-bank-fields").classList.toggle("hidden", !wanted);
    };
    fillGroups(a?.group_id ?? "");
    form.elements.type.onchange = () => { fillGroups(""); toggleBank(); };
    if (form.elements.is_bank) form.elements.is_bank.onchange = toggleBank;
    toggleBank();

    form.onsubmit = (event) => {
      event.preventDefault();
      busy(form, async () => {
        const f = form.elements;
        const text = (v) => (v.trim() === "" ? null : v.trim());
        const code = f.code.value.trim();
        const name = f.name.value.trim();
        if (!code || !name) throw new Error("Code and name are required.");
        const type = bank ? a.type : f.type.value;
        const groupId = f.group_id.value || null;
        const wantsBank = !!bank || (canHaveBank() && type === "assets" && !!f.is_bank?.checked);
        const bankName = f.bank_name.value.trim();
        if (wantsBank && !bankName) throw new Error("Bank is required for a bank or cash account.");
        const accountUrl = `${ledgerUrl()}/accounts`;
        const bankUrl = `${ledgerUrl()}/bank-accounts`;
        if (a) {
          await write("PATCH", `${accountUrl}/${a.id}`, { code, name, type, group_id: groupId, active: f.active.checked }, a.version);
          if (bank) {
            await write("PUT", `${bankUrl}/${a.id}`, { bank_name: bankName, account_number: text(f.account_number.value) }, bank.version);
          } else if (wantsBank) {
            await write("POST", bankUrl, { account_id: a.id, bank_name: bankName, account_number: text(f.account_number.value), currency: f.bank_currency.value });
          }
        } else {
          const created = await write("POST", accountUrl, { code, name, type, group_id: groupId });
          if (wantsBank) {
            await write("POST", bankUrl, { account_id: created.data.id, bank_name: bankName, account_number: text(f.account_number.value), currency: f.bank_currency.value });
          }
        }
        $("form-dialog").close();
        await refreshLedger();
      });
    };
  } catch (error) {
    showError(error);
  }
}


const accountName = (id) => {
  const a = state.accounts.find((x) => x.id === id);
  return a ? `${a.code} ${a.name}` : id;
};

function showJournal(j) {
  $("journal-body").innerHTML = `<h3>Journal ${j.journal_no} <span class="pill ${j.status}">${j.status}</span></h3>
    ${j.source_kind ? `<p>From ${SOURCES[j.source_kind] ?? esc(j.source_kind)} · change it through the document, not the journal.
      <button class="secondary" onclick="openDocument('${j.source_kind}', '${j.source_id}')">Open ${SOURCES[j.source_kind] ?? "document"}</button></p>` : ""}
    <p class="muted">${j.accounting_date} · ${esc(j.reference ?? "")} · ${esc(j.description ?? "")} · revision ${j.revision_no}, version ${j.version}</p>
    <table><tr><th>Account</th><th class="num">Debit</th><th class="num">Credit</th></tr>
    ${j.lines.map((l) => `<tr><td>${esc(accountName(l.account_id))}</td><td class="num">${l.debit === "0.00" ? "" : l.debit}</td><td class="num">${l.credit === "0.00" ? "" : l.credit}</td></tr>`).join("")}</table>
    ${j.issues.map((i) => `<div class="warning">${esc(i.code)}: ${esc(i.message)}</div>`).join("")}
    ${canEdit() && !j.source_kind && j.status !== "deleted" ? `<p><button class="secondary" onclick="editJournal('${j.id}')">Edit</button></p>` : ""}`;
  $("journal-dialog").showModal();
}

const blank = (v) => (v === "0.00" || v === "0" ? "" : v);


$("tb-form").addEventListener("submit", async (event) => {
  event.preventDefault();
  try {
    const f = event.target;
    const tb = await api(`${ledgerUrl()}/reports/trial-balance?from=${f.from.value}&to=${f.to.value}`);
    const t = tb.totals;
    $("tb-out").innerHTML = `${tb.warnings.map((w) => `<div class="warning">${esc(w.message)}</div>`).join("")}
      <p>${tb.balanced ? `<span class="ok">Balanced</span>` : `<span class="error">Not balanced</span>`} · ${tb.currency}</p>
      <table><tr><th>Code</th><th>Account</th><th class="num">Opening Dr</th><th class="num">Opening Cr</th><th class="num">Period Dr</th>
        <th class="num">Period Cr</th><th class="num">Closing Dr</th><th class="num">Closing Cr</th></tr>
      ${tb.rows.map((r) => `<tr ${r.account_id ? `class="click" data-account="${r.account_id}"` : ""}><td>${esc(r.code ?? "")}</td><td>${esc(r.name)}</td><td class="num">${blank(r.opening_debit)}</td><td class="num">${blank(r.opening_credit)}</td>
        <td class="num">${blank(r.period_debit)}</td><td class="num">${blank(r.period_credit)}</td><td class="num">${blank(r.closing_debit)}</td><td class="num">${blank(r.closing_credit)}</td></tr>`).join("")}
      <tr class="total"><td></td><td>Total</td><td class="num">${t.opening_debit}</td><td class="num">${t.opening_credit}</td><td class="num">${t.period_debit}</td>
        <td class="num">${t.period_credit}</td><td class="num">${t.closing_debit}</td><td class="num">${t.closing_credit}</td></tr></table>
      <p class="muted small">Click an account for its GL detail.</p>`;
    for (const tr of $("tb-out").querySelectorAll("tr[data-account]")) tr.onclick = () => openGl(tr.dataset.account, f.from.value, f.to.value);
  } catch (error) {
    showError(error);
  }
});

$("gl-form").addEventListener("submit", async (event) => {
  event.preventDefault();
  try {
    const f = event.target;
    const gl = await api(`${ledgerUrl()}/reports/gl-detail?accountId=${f.account.value}&from=${f.from.value}&to=${f.to.value}`);
    $("gl-out").innerHTML = `<h3>${esc(gl.code)} ${esc(gl.name)}</h3>
      <table><tr><th>Date</th><th>No.</th><th>Reference</th><th>Description</th><th class="num">Debit</th><th class="num">Credit</th><th class="num">Balance</th></tr>
      <tr><td colspan="6" class="muted">Opening balance</td><td class="num">${gl.opening_balance}</td></tr>
      ${gl.lines.map((l) => `<tr class="click" data-journal="${l.journal_id}"><td>${l.date}</td><td>${l.journal_no}</td><td>${esc(l.reference)}</td><td>${esc(l.description)}</td>
        <td class="num">${blank(l.debit)}</td><td class="num">${blank(l.credit)}</td><td class="num">${l.balance}</td></tr>`).join("")}
      <tr class="total"><td colspan="4">Period totals / closing</td><td class="num">${gl.period_debit}</td><td class="num">${gl.period_credit}</td><td class="num">${gl.closing_balance}</td></tr></table>
      <p class="muted small">Click a line for its journal.</p>`;
    for (const tr of $("gl-out").querySelectorAll("tr[data-journal]")) tr.onclick = () => openJournal(tr.dataset.journal);
  } catch (error) {
    showError(error);
  }
});


// Who owes us or who we owe, how late, and their statements. A report, so it keeps its own as-of date
// instead of sharing the list filters.
async function renderAging(side) {
  const sales = side === "sales";
  state.agingAsOf ??= {};
  const asOf = state.agingAsOf[side] ?? today();
  state.agingAsOf[side] = asOf;
  const aging = await api(`${ledgerUrl()}/reports/${sales ? "ar" : "ap"}-aging?asOf=${asOf}`);
  const nameKey = sales ? "customer" : "supplier";
  const idKey = sales ? "customer_id" : "supplier_id";
  const partyLabel = sales ? "Customer" : "Supplier";
  const control = sales ? "receivable" : "payable";
  const container = sales ? "tab-sales-reports" : "tab-purchases-reports";
  const check = `${aging.control_balance} ${aging.currency}`;
  setActions("");

  $(container).innerHTML = `
    <form class="filter-bar" id="aging-form">
      <label>As of <input type="date" name="asOf" value="${asOf}"></label>
      <button type="submit">Run</button>
    </form>
    <p class="muted small">${aging.matches_control
      ? `<span class="ok">Equals the ${control} balance ${check}</span>`
      : `<span class="error">Does not equal the ${control} balance ${check}</span>`} · click a ${partyLabel.toLowerCase()} for their statement</p>
    ${aging.rows.length ? `<table><tr><th>${partyLabel}</th><th class="num">Not due</th><th class="num">1–30</th><th class="num">31–60</th><th class="num">61–90</th><th class="num">Over 90</th><th class="num">Total</th></tr>
      ${[...aging.rows, aging.totals].map((r) => `<tr class="${r === aging.totals ? "total" : "click"}" ${r === aging.totals ? "" : `data-${nameKey}="${r[idKey]}"`}>
        <td>${esc(r[nameKey])}</td><td class="num">${r.not_due}</td><td class="num">${r.days1_to30}</td><td class="num">${r.days31_to60}</td>
        <td class="num">${r.days61_to90}</td><td class="num">${r.over90}</td><td class="num">${r.total}</td></tr>`).join("")}</table>`
      : `<p class="muted">Nothing is owed at ${aging.as_of}.</p>`}`;

  $("aging-form").onsubmit = (event) => {
    event.preventDefault();
    state.agingAsOf[side] = event.target.asOf.value;
    renderAging(side);
  };
  for (const tr of $(container).querySelectorAll("tr[data-customer], tr[data-supplier]")) {
    const id = tr.dataset.customer ?? tr.dataset.supplier;
    tr.onclick = () => showStatement(sales ? "customer" : "supplier", id, aging.as_of);
  }
}
// What the customer sees on the paper: the seller block, the number series, and the rule that decides
// which of the three styles is printed.
async function renderSalesSettings() {
  const s = await api(`${ledgerUrl()}/invoice-settings`);
  setActions(isAdmin() ? `<button onclick="invoiceSettingsForm()">Edit seller details</button>` : "");
  $("tab-sales-settings").innerHTML = `
    <dl class="facts">
      <dt>Name in Khmer</dt><dd>${esc(s.name_khmer || "not set")}</dd>
      <dt>Name in English</dt><dd>${esc(s.name_english || "not set")}</dd>
      <dt>VATTIN</dt><dd>${esc(s.tax_id || "not set")}</dd>
      <dt>Phone</dt><dd>${esc(s.phone || "not set")}</dd>
      <dt>Email</dt><dd>${esc(s.email || "not set")}</dd>
      <dt>Address</dt><dd>${esc(s.address_english || "not set")}</dd>
      <dt>Invoice numbers</dt><dd>${esc(s.invoice_prefix ?? "")}00001 onwards</dd>
      <dt>Credit note numbers</dt><dd>${esc(s.credit_note_prefix ?? "")}00001 onwards</dd>
    </dl>
    <h3>What decides the printed style</h3>
    <p class="muted small">The lines' tax codes decide: any VAT above 0% prints a tax invoice, only 0% or non-VAT prints a zero or non-VAT invoice, and no codes print a commercial invoice. State Charge relabels the VAT line, a draft prints marked DRAFT, and the KHR total comes from the invoice's own rate snapshot rather than today's rate.</p>`;
}

function showSale(d, party) {
  const t = d.tax_currency;
  $("journal-body").innerHTML = `<h3>${d.type === "credit_note" ? "Credit note" : "Sales invoice"} ${d.invoice_no} <span class="pill ${d.status}">${d.status}</span></h3>
    <p class="muted">${esc(party(d.customer_id))} · ${d.date} · due ${d.due_date} · ${esc(d.reference ?? "")}</p>
    <table><tr><th>Account</th><th>Description</th><th class="num">Net</th><th class="num">VAT</th></tr>
    ${d.lines.map((l) => `<tr><td>${esc(accountName(l.account_id))}</td><td>${esc(l.description)}</td><td class="num">${l.net_amount}</td>
      <td class="num">${l.tax_amount}${l.tax_adjusted ? ` <span class="pill draft" title="${esc(l.tax_reason)}">adjusted</span>` : ""}</td></tr>`).join("")}
    <tr class="total"><td colspan="2">Gross ${d.gross_total}</td><td class="num">${d.net_total}</td><td class="num">${d.tax_total}</td></tr></table>
    ${t ? `<p>Total including VAT: <b>${Number(t.gross_total).toLocaleString()} ${t.currency}</b> (VAT ${Number(t.tax_total).toLocaleString()} ${t.currency})
      at ${t.rate} ${esc(t.rate_source)} of ${t.rate_date}${t.reason ? ` · ${esc(t.reason)}` : ""}</p>` : ""}
    ${d.issues.map((i) => `<div class="warning">${esc(i.code)}: ${esc(i.message)}</div>`).join("")}${warningsHtml(d)}${journalButton(d)}
    <p><button class="secondary" onclick="openPdf('${d.type === "credit_note" ? "credit-notes" : "sales-invoices"}/${d.id}/pdf')">Print PDF</button>
      ${canEdit() ? `<button class="secondary" onclick="editDocument('sale', '${d.type}', '${d.id}')">Edit</button>` : ""}</p>`;
  $("journal-dialog").showModal();
}

// The PDF needs the bearer token, so it is fetched and opened as a blob.
async function openPdf(path) {
  const response = await fetch(`${ledgerUrl()}/${path}`, { headers: { Authorization: `Bearer ${state.token}` } });
  if (!response.ok) return showError(new Error(`PDF failed: ${response.status}`));
  window.open(URL.createObjectURL(await response.blob()), "_blank");
}

// Statements and reconciliation: one bank account at a time, the bank's balance against the books, the
// postings still pending, and the imported statement lines. Clearing changes no posting, so it is safe
// for a person to do; the statement drafts are posted in the same page.
async function renderBankStatements() {
  setActions("");
  const container = "tab-bank-statements";
  const banks = await api(`${ledgerUrl()}/bank-accounts`);
  state.banks = banks;
  if (!banks.length) {
    $(container).innerHTML = `<p class="muted">No bank or cash accounts yet. Add one in <b>Books → Chart of accounts</b>; a bank account is an Assets account with the bank details that payments, receipts and statements use.</p>`;
    return;
  }
  const bank = banks.find((b) => b.account_id === state.statementAccount) ?? banks[0];
  state.statementAccount = bank.account_id;
  state.statementAsOf ??= {};
  state.statementBalance ??= {};
  const statements = await api(`${ledgerUrl()}/bank-accounts/${bank.account_id}/statements`);
  const latest = statements[0];
  const asOf = state.statementAsOf[bank.account_id] ?? latest?.to ?? today();
  const balance = state.statementBalance[bank.account_id] ?? latest?.closing_balance ?? "";
  const query = `asOf=${asOf}${balance !== "" ? `&statementBalance=${encodeURIComponent(balance)}` : ""}`;
  const r = await api(`${ledgerUrl()}/bank-accounts/${bank.account_id}/reconciliation?${query}`);
  const currency = r.currency ?? bank.currency;
  const amount = (value) => `${money(Number(value), currency)} ${currency}`;
  const figure = (label, value) => `<div class="figure"><span class="muted small">${label}</span><b>${value}</b></div>`;
  const cleared = r.reconciled === true
    ? `<span class="ok">Reconciled: the cleared balance equals the statement balance</span>`
    : r.reconciled === false
      ? `<span class="error">Difference ${amount(r.difference)}: the statement is ${Number(r.difference) > 0 ? "above" : "below"} the cleared balance</span>`
      : `<span class="muted">Enter the statement's closing balance to compare it with the cleared balance.</span>`;
  const visiblePending = r.pending.slice(-100);

  $(container).innerHTML = `
    <form class="filter-bar" id="statement-form">
      <label>Bank account <select name="account">
        ${banks.map((b) => `<option value="${b.account_id}" ${b.account_id === bank.account_id ? "selected" : ""}>${esc(b.code)} ${esc(b.name)} · ${esc(b.bank_name)} · ${esc(b.currency)}</option>`).join("")}
      </select></label>
      <label>As of <input type="date" name="asOf" value="${asOf}"></label>
      <label>Statement balance <input name="balance" class="num" inputmode="decimal" value="${esc(balance)}" placeholder="closing balance"></label>
      <button type="submit">Reconcile</button>
      <span class="spacer"></span>
      <span class="muted small">${statements.length ? `Latest statement ${statements[0].from} – ${statements[0].to}` : "No statement imported yet"}</span>
    </form>
    <div class="figures">
      ${figure("Book balance", amount(r.book_balance))}
      ${figure("Cleared", amount(r.cleared_balance))}
      ${figure("Statement", r.statement_balance == null ? "—" : amount(r.statement_balance))}
      ${figure("Difference", r.difference == null ? "—" : amount(r.difference))}
    </div>
    <p>${cleared}</p>

    <h3>Pending postings <span class="muted small">— cheques in transit, deposits not yet credited; clearing changes no posting</span></h3>
    ${r.pending.length
      ? `<div class="select-bar hidden" id="statement-bar"><b id="statement-bar-count">0</b> selected
          <span class="muted small" id="statement-bar-total"></span><span class="spacer"></span>
          ${canEdit() ? `<button type="button" class="small" data-clear-selected>Mark cleared on ${asOf}</button>` : ""}
        </div>
        <table class="list"><tr>${canEdit() ? `<th style="width:34px"></th>` : ""}<th>No.</th><th>Date</th><th>Source</th><th>Reference</th><th>Description</th><th class="num">Amount</th></tr>
        ${visiblePending.map((p) => `<tr class="click" data-journal="${p.journal_id}">
          ${canEdit() ? `<td><input type="checkbox" class="pending-check" data-clear-journal="${p.journal_id}" title="Select to clear"></td>` : ""}
          <td>No. ${p.journal_no}</td><td>${p.date}</td><td>${esc(p.source.replaceAll("_", " "))}</td><td>${esc(p.reference ?? "")}</td>
          <td>${esc(p.description ?? "")}</td><td class="num">${amount(p.amount)}</td></tr>`).join("")}</table>
        ${r.pending.length > visiblePending.length ? `<p class="muted small">${r.pending.length - visiblePending.length} earlier pending postings not shown.</p>` : ""}`
      : `<p class="muted">Every posting through ${asOf} is cleared.</p>`}

    <h3 style="margin-top:20px">Statements <span class="muted small">— imported through MCP; a line clears a matching posting or becomes a draft to Suspense</span></h3>
    ${statements.length
      ? statements.map((st) => `<div class="statement">
          <div class="statement-head">
            <div><b>${st.from} – ${st.to}</b>
              <span class="muted small">Opening ${amount(st.opening_balance)} · closing ${amount(st.closing_balance)} · ${st.matched} matched · ${st.drafted} to Suspense · ${st.duplicates} duplicates</span></div>
            ${canEdit() && st.drafted ? `<button type="button" class="secondary small" data-post-drafts="${st.id}">Post ${st.drafted} draft${st.drafted === 1 ? "" : "s"}</button>` : ""}
          </div>
          <table class="list"><tr><th>Date</th><th>Description</th><th>Reference</th><th class="num">Amount</th><th>Status</th></tr>
          ${st.lines.map((l) => `<tr class="${l.status === "duplicate" ? "" : "click"}" data-line-journal="${l.journal_id ?? ""}" data-line-payment="${l.payment_id ?? ""}" data-line-kind="${Number(l.amount) > 0 ? "receipt" : "payment"}">
            <td>${l.date}</td><td>${esc(l.description ?? "")}</td><td>${esc(l.reference ?? "")}</td>
            <td class="num">${amount(l.amount)}</td>
            <td><span class="pill ${l.status === "matched" ? "posted" : l.status === "drafted" ? "draft" : ""}">${l.status}</span></td></tr>`).join("")}</table>
        </div>`).join("")
      : `<p class="muted">No statement imported for ${esc(bank.code)} ${esc(bank.name)} yet. Bank statements arrive through MCP; their unmatched lines become drafts to Suspense for a person to post.</p>`}`;

  $("statement-form").onsubmit = (event) => {
    event.preventDefault();
    const form = event.target;
    state.statementAccount = form.account.value;
    state.statementAsOf[form.account.value] = form.asOf.value;
    state.statementBalance[form.account.value] = form.balance.value.trim();
    renderBankStatements();
  };
  $("statement-form").account.onchange = (event) => {
    state.statementAccount = event.target.value;
    renderBankStatements();
  };

  // Clearing: tick pending postings and mark them cleared on the statement date.
  const paintStatementBar = () => {
    const picked = [...$(container).querySelectorAll(".pending-check:checked")].map((box) => box.dataset.clearJournal);
    const bar = $("statement-bar");
    if (!bar) return;
    bar.classList.toggle("hidden", picked.length === 0);
    $("statement-bar-count").textContent = picked.length;
    $("statement-bar-total").textContent = picked.length
      ? amount(r.pending.filter((p) => picked.includes(p.journal_id)).reduce((s, p) => s + Number(p.amount), 0))
      : "";
  };
  for (const box of $(container).querySelectorAll(".pending-check")) {
    box.onchange = paintStatementBar;
    box.onclick = (event) => event.stopPropagation();
  }
  for (const tr of $(container).querySelectorAll("tr[data-journal]")) {
    if (tr.classList.contains("click")) tr.onclick = () => openJournal(tr.dataset.journal);
  }
  $(container).querySelector("[data-clear-selected]")?.addEventListener("click", async () => {
    const picked = [...$(container).querySelectorAll(".pending-check:checked")].map((box) => box.dataset.clearJournal);
    if (!picked.length) return;
    try {
      await write("POST", `${ledgerUrl()}/bank-accounts/${bank.account_id}/clearings`, { cleared_on: asOf, journal_ids: picked });
      await refreshLedger();
    } catch (error) {
      showError(error);
    }
  });
  paintStatementBar();

  // A statement's unmatched lines are drafts to Suspense; posting them is the last step of importing it.
  for (const button of $(container).querySelectorAll("[data-post-drafts]")) {
    button.onclick = async () => {
      try {
        await write("POST", `${ledgerUrl()}/bank-statements/${button.dataset.postDrafts}/post-drafts`);
        await refreshLedger();
      } catch (error) {
        showError(error);
      }
    };
  }
  for (const tr of $(container).querySelectorAll("tr[data-line-kind]")) {
    tr.onclick = () => tr.dataset.lineJournal
      ? openJournal(tr.dataset.lineJournal)
      : tr.dataset.linePayment && openDocument(tr.dataset.lineKind, tr.dataset.linePayment);
  }
}

// The tax return and the tax codes live together: the return is computed from the codes, so a change to
// a rate or account is made next to the figures it feeds.
function taxCodesSection(taxCodes) {
  const manage = isAdmin();
  return `<h3 style="margin-top:20px">Tax codes ${manage ? `<button class="secondary small" onclick="taxCodeForm()">New tax code</button>` : ""}</h3>
    <p class="muted small">A rate change for a period is a new code with effective dates, so earlier documents keep the rate they were posted with.</p>
    ${taxCodes.length ? `<table><tr><th>Name</th><th>Kind</th><th class="num">Rate %</th><th>Account</th><th>Return category</th><th>Effective</th></tr>
      ${taxCodes.map((t) => `<tr ${manage ? `class="click" data-tax="${t.id}"` : ""}><td>${esc(t.name)}${t.active ? "" : ` <span class="pill">inactive</span>`}</td><td>${t.kind.replaceAll("_", " ")}</td><td class="num">${t.rate}</td>
        <td>${t.account_id ? esc(accountName(t.account_id)) : ""}</td><td>${esc(t.return_category ?? "")}</td>
        <td>${t.effective_from ? `from ${t.effective_from}` : ""}${t.effective_to ? ` to ${t.effective_to}` : ""}</td></tr>`).join("")}</table>`
      : `<p class="muted small">No tax codes yet.</p>`}`;
}

async function renderTax(period) {
  state.taxPeriod = period;
  const taxCodes = await api(`${ledgerUrl()}/tax-codes`);
  const months = Array.from({ length: 12 }, (_, i) => `2026-${String(i + 1).padStart(2, "0")}`);
  let r;
  try {
    r = await api(`${ledgerUrl()}/tax-returns/${period}`);
  } catch (e) {
    $("tab-tax").innerHTML = `
      <p class="filter-bar"><label>Month <select id="tax-period">${months.map((m) => `<option ${m === period ? "selected" : ""}>${m}</option>`).join("")}</select></label></p>
      <p class="muted">${esc(e.message)}</p>
      ${taxCodesSection(taxCodes)}`;
    $("tax-period").onchange = (event) => renderTax(event.target.value);
    for (const tr of $("tab-tax").querySelectorAll("tr[data-tax]")) tr.onclick = () => taxCodeForm(taxCodes.find((t) => t.id === tr.dataset.tax));
    return;
  }
  const sections = [...new Set(r.boxes.map((b) => b.section))];
  $("tab-tax").innerHTML = `
    <p class="filter-bar"><label>Month <select id="tax-period">${months.map((m) => `<option ${m === period ? "selected" : ""}>${m}</option>`).join("")}</select></label>
      <a class="button" href="#" id="tax-xlsx">Excel</a>
      <span class="muted small">${esc(r.return_name)} · definition v${r.definition_version} · ${r.currency} / ${r.tax_currency ?? ""}
      ${r.tax_currency_basis === "override" ? `at ${r.override_rate}` : "from document snapshots"}</span></p>
    <p>${r.filed ? `<span class="pill posted">filed ${r.filed.filed_at.slice(0, 10)}</span> ${r.filed.differences.length ? `<span class="error">${r.filed.differences.length} boxes differ from the filed return</span>` : ""}` : `<span class="pill draft">not filed</span>`}</p>
    ${r.issues.map((i) => `<div class="warning">${esc(i.code)}: ${esc(i.message)}</div>`).join("")}
    ${sections.map((s) => `<h3>${esc(s)}</h3><table><tr><th>Box</th><th>Label</th><th class="num">${r.currency}</th><th class="num">${r.tax_currency ?? ""}</th></tr>
      ${r.boxes.filter((b) => b.section === s).map((b) => `<tr class="${b.lines ? "click" : "subtotal"}" data-box="${b.key}"><td>${b.key}</td><td>${esc(b.label)}${b.information ? " <span class='muted'>(information)</span>" : ""}</td>
        <td class="num">${b.amount}</td><td class="num">${b.tax_currency_amount ? Number(b.tax_currency_amount).toLocaleString() : ""}</td></tr>`).join("")}</table>`).join("")}
    <h3>Reconciliation with tax accounts</h3>
    <table><tr><th>Usage</th><th>Accounts</th><th class="num">Ledger</th><th class="num">Documents</th><th class="num">Difference</th><th>Other postings</th></tr>
    ${r.reconciliation.map((x) => `<tr><td>${esc(x.label)}</td><td>${x.accounts.join(", ")}</td><td class="num">${x.ledger_amount}</td><td class="num">${x.return_amount}</td>
      <td class="num ${x.matches ? "ok" : "error"}">${x.difference}</td><td>${x.other_postings.map((o) => `<a href="#" onclick="event.preventDefault(); openJournalNo(${o.journal_no})">No. ${o.journal_no}</a> ${esc(o.reference ?? "")} ${o.amount}`).join("; ")}</td></tr>`).join("")}</table>
    ${r.adjusted_lines.length ? `<h3>Lines with overridden VAT</h3><table><tr><th>Document</th><th>Party</th><th class="num">Calculated</th><th class="num">Entered</th><th>Reason</th></tr>
      ${r.adjusted_lines.map((l) => `<tr><td>${l.document_type} ${l.document_no}</td><td>${esc(l.party)}</td><td class="num">${l.tax_calculated}</td><td class="num">${l.tax_amount}</td><td>${esc(l.tax_reason ?? "")}</td></tr>`).join("")}</table>` : ""}
    ${taxCodesSection(taxCodes)}`;
  $("tax-period").onchange = (e) => renderTax(e.target.value);
  $("tax-xlsx").onclick = (e) => { e.preventDefault(); download(`${ledgerUrl()}/tax-returns/${period}.xlsx`, `tax-return-${period}.xlsx`); };
  for (const tr of $("tab-tax").querySelectorAll("tr[data-box].click")) {
    tr.onclick = async () => {
      const box = await api(`${ledgerUrl()}/tax-returns/${period}/boxes/${tr.dataset.box}`);
      $("journal-body").innerHTML = `<h3>${esc(box.label)} · ${box.period}</h3><p class="muted">${box.amount} · ${box.tax_currency_amount ?? ""}</p>
        <table><tr><th>Document</th><th>Date</th><th>Party</th><th>Line</th><th class="num">Amount</th><th class="num">Rate</th><th class="num">KHR</th></tr>
        ${box.lines.map((l) => `<tr class="click" onclick="openDocument('${l.document_type}', '${l.document_id}')"><td>${l.document_type.replaceAll("_", " ")} ${l.document_no}</td><td>${l.date}</td><td>${esc(l.party)}</td><td>${esc(l.description ?? "")}</td>
          <td class="num">${l.amount}</td><td class="num">${l.tax_currency_rate ?? ""}</td><td class="num">${l.tax_currency_amount ? Number(l.tax_currency_amount).toLocaleString() : ""}</td></tr>`).join("")}</table>`;
      $("journal-dialog").showModal();
    };
  }
  for (const tr of $("tab-tax").querySelectorAll("tr[data-tax]")) tr.onclick = () => taxCodeForm(taxCodes.find((t) => t.id === tr.dataset.tax));
}

function showPurchase(d, party) {
  $("journal-body").innerHTML = `<h3>${d.type === "debit_note" ? "Debit note" : "Purchase invoice"} ${d.invoice_no} <span class="pill ${d.status}">${d.status}</span></h3>
    <p class="muted">${esc(party(d.supplier_id))} · ${d.date} · due ${d.due_date} · ${esc(d.reference ?? "")}</p>
    <table><tr><th>Account</th><th>Description</th><th class="num">Net</th><th class="num">VAT</th><th>Tax invoice</th><th class="num">WHT</th></tr>
    ${d.lines.map((l) => `<tr><td>${esc(accountName(l.account_id))}</td><td>${esc(l.description)}</td><td class="num">${l.net_amount}</td>
      <td class="num">${l.tax_amount}${l.tax_adjusted ? ` <span class="pill draft" title="${esc(l.tax_reason)}">adjusted</span>` : ""}</td>
      <td>${l.tax_code_id ? (l.valid_tax_invoice ? "valid" : "missing: non-deductible") : ""}</td><td class="num">${l.withholding_amount}</td></tr>`).join("")}
    <tr class="total"><td colspan="2">Totals · gross ${d.gross_total} · payable ${d.payable_total}</td><td class="num">${d.net_total}</td><td class="num">${d.tax_total}</td><td></td><td class="num">${d.withholding_total}</td></tr></table>
    ${d.tax_currency ? `<p class="muted">${d.tax_currency.currency} at ${d.tax_currency.rate} (${esc(d.tax_currency.rate_source)}, ${d.tax_currency.rate_date})${d.tax_currency.reason ? ` · ${esc(d.tax_currency.reason)}` : ""}:
      gross ${d.tax_currency.gross_total} · VAT ${d.tax_currency.tax_total}</p>` : ""}
    ${d.issues.map((i) => `<div class="warning">${esc(i.code)}: ${esc(i.message)}</div>`).join("")}${warningsHtml(d)}${journalButton(d)}
    ${canEdit() ? `<p><button class="secondary" onclick="editDocument('purchase', '${d.type}', '${d.id}')">Edit</button></p>` : ""}`;
  $("journal-dialog").showModal();
}

// Statements: nested groups with a subtotal at every level.
function statementRows(lines, level) {
  return lines.map((line) => {
    const pad = `style="padding-left:${8 + level * 18}px"`;
    const name = esc(line.code ? `${line.code} ${line.name}` : line.name);
    if (line.kind !== "group") {
      const drill = line.kind === "account" && line.id ? `class="click" data-account="${line.id}"` : "";
      return `<tr ${drill}><td ${pad}>${line.kind === "calculated" ? `<em>${name}</em>` : name}</td><td class="num">${line.amount}</td></tr>`;
    }
    return `<tr><td ${pad}><strong>${name}</strong></td><td></td></tr>${statementRows(line.children, level + 1)}
      <tr><td ${pad}><strong>Total ${esc(line.name)}</strong></td><td class="num"><strong>${line.amount}</strong></td></tr>`;
  }).join("");
}

function statementTable(sections) {
  return sections.map((s) => `<tr class="group"><td colspan="2">${esc(s.title)}</td></tr>${statementRows(s.lines, 1)}
    <tr class="total"><td>Total ${esc(s.title)}</td><td class="num">${s.total}</td></tr>`).join("");
}

$("pl-form").addEventListener("submit", async (event) => {
  event.preventDefault();
  try {
    const f = event.target;
    const pl = await api(`${ledgerUrl()}/reports/profit-and-loss?from=${f.from.value}&to=${f.to.value}`);
    $("pl-out").innerHTML = `<table><tr><th>Account</th><th class="num">${pl.currency}</th></tr>${statementTable(pl.sections)}
      <tr class="total"><td>Net profit</td><td class="num">${pl.net_profit}</td></tr></table><p class="muted small">Click an account for its GL detail.</p>`;
    for (const tr of $("pl-out").querySelectorAll("tr[data-account]")) tr.onclick = () => openGl(tr.dataset.account, f.from.value, f.to.value);
  } catch (error) {
    showError(error);
  }
});

$("bs-form").addEventListener("submit", async (event) => {
  event.preventDefault();
  try {
    const bs = await api(`${ledgerUrl()}/reports/balance-sheet?asOf=${event.target.asOf.value}`);
    $("bs-out").innerHTML = `<p>${bs.balanced ? `<span class="ok">Balanced</span>` : `<span class="error">Not balanced</span>`}
      · assets ${bs.total_assets} = liabilities and equity ${bs.total_liabilities_and_equity}</p>
      <table><tr><th>Account</th><th class="num">${bs.currency}</th></tr>${statementTable(bs.sections)}</table><p class="muted small">Click an account for its GL detail.</p>`;
    const asOf = event.target.asOf.value;
    for (const tr of $("bs-out").querySelectorAll("tr[data-account]")) tr.onclick = () => openGl(tr.dataset.account, `${asOf.slice(0, 4)}-01-01`, asOf);
  } catch (error) {
    showError(error);
  }
});

async function download(url, name) {
  try {
    const response = await fetch(url, { headers: { Authorization: `Bearer ${state.token}` } });
    if (!response.ok) throw new Error(`Export failed (HTTP ${response.status}).`);
    const link = Object.assign(document.createElement("a"), { href: URL.createObjectURL(await response.blob()), download: name });
    link.click();
    URL.revokeObjectURL(link.href);
  } catch (error) {
    showError(error);
  }
}

// Excel downloads use the same query as the form next to the button.
for (const button of document.querySelectorAll("[data-export]")) {
  button.onclick = async () => {
    try {
      const form = button.closest("form");
      const query = new URLSearchParams();
      for (const [key, value] of new FormData(form)) query.set(key === "account" ? "accountId" : key, value);
      const response = await fetch(`${ledgerUrl()}/reports/${button.dataset.export}.xlsx?${query}`, { headers: { Authorization: `Bearer ${state.token}` } });
      if (!response.ok) throw new Error(`Export failed (HTTP ${response.status}).`);
      const name = /filename="?([^";]+)/.exec(response.headers.get("Content-Disposition") ?? "")?.[1] ?? `${button.dataset.export}.xlsx`;
      const link = Object.assign(document.createElement("a"), { href: URL.createObjectURL(await response.blob()), download: name });
      link.click();
      URL.revokeObjectURL(link.href);
    } catch (error) {
      showError(error);
    }
  };
}

start();