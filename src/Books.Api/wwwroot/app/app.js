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
    <table><tr><th>Line</th><th>Description</th><th class="num">Amount</th><th class="num">VAT</th><th class="num">WHT</th><th class="num">Total</th></tr>
    ${d.lines.map((l) => `<tr><td>${l.kind === "account" ? esc(accountName(l.account_id)) : `${esc(accountName(l.control_account_id))} · ${l.kind === "invoice" ? "settles an invoice" : "party balance"}`}</td>
      <td>${esc(l.description ?? "")}</td><td class="num">${l.amount}</td><td class="num">${blank(l.tax_amount)}</td><td class="num">${blank(l.withholding_amount)}</td><td class="num">${l.total}</td></tr>`).join("")}
    <tr class="total"><td colspan="5">Total</td><td class="num">${d.total}</td></tr></table>
    ${d.issues.map((i) => `<div class="warning">${esc(i.code)}: ${esc(i.message)}</div>`).join("")}${warningsHtml(d)}${journalButton(d)}
    ${canEdit() ? `<p><button class="secondary" onclick="editPayment('${d.type}', '${d.id}')">Edit</button></p>` : ""}`;
  $("journal-dialog").showModal();
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
    { id: "bank", label: "Cash and bank" },
    { id: "payroll", label: "Payroll" },
  ] },
  { group: "Stock and assets", items: [
    { id: "inventory", label: "Inventory" },
    { id: "assets", label: "Fixed assets" },
  ] },
  { group: "Records", items: [
    { id: "parties", label: "Customers and suppliers" },
    { id: "banks", label: "Bank accounts" },
    { id: "taxcodes", label: "Tax codes" },
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
  bank: { title: "Cash and bank", sub: "Payments, receipts, transfers, statements and reconciliation", render: () => renderBank() },
  payroll: { title: "Payroll", sub: "Pay runs received from the payroll system; Books does not calculate pay", render: () => renderPayroll() },
  inventory: { title: "Inventory", sub: "Stock on hand, adjustments, locations, transfers and assemblies", render: () => renderInventory() },
  assets: { title: "Fixed assets", sub: "The register and monthly depreciation", render: () => renderAssets() },
  parties: { title: "Customers and suppliers", sub: "One record for each party, with tax ID and payment terms", render: () => renderParties() },
  banks: { title: "Bank accounts", sub: "Cash and bank accounts in the chart of accounts", render: () => renderBanks() },
  taxcodes: { title: "Tax codes", sub: "Rates, tax accounts and the return category each code feeds", render: () => renderTaxCodes() },
  journals: { title: "Journals", sub: "Every posting, with its revision and its source document", render: () => renderJournals() },
  chart: { title: "Chart of accounts", sub: "Groups are headings that never take postings; accounts carry the balances", render: () => renderChart() },
  pl: { title: "Profit and loss", sub: "Income and cost and expense over a period", render: () => runReport("pl") },
  bs: { title: "Balance sheet", sub: "Assets, liabilities and equity at a date", render: () => runReport("bs") },
  tb: { title: "Trial balance", sub: "Opening, period and closing balances by account", render: () => runReport("tb") },
  gl: { title: "GL detail", sub: "Every line in one account, with a running balance", render: () => runReport("gl") },
  tax: { title: "Tax return", sub: "The month's return on the GDT forms, with filing", render: () => renderTax(state.taxPeriod ?? "2026-10") },
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

async function renderChart() {
  const groups = state.groups ?? [];
  const accounts = state.accounts ?? [];
  const purposeOf = state.purposeOf ?? {};
  const rows = [];
  for (const [type, label] of Object.entries(TYPES)) {
    const typeGroups = groups.filter((g) => g.type === type);
    const loose = accounts.filter((a) => a.type === type && !a.group_id);
    if (!typeGroups.length && !loose.length) continue;
    rows.push(`<tr class="group"><td colspan="4">${label}</td></tr>`);
    for (const group of typeGroups) {
      rows.push(`<tr><td colspan="4"><strong>${esc(group.name)}</strong> <span class="muted small">group, never posted to</span></td></tr>`);
      for (const a of accounts.filter((x) => x.group_id === group.id)) rows.push(accountRow(a, purposeOf, true));
    }
    for (const a of loose) rows.push(accountRow(a, purposeOf, false));
  }
  $("tab-chart").innerHTML = `<table><tr><th>Code</th><th>Account</th><th>Status</th><th>System purpose</th></tr>${rows.join("")}</table>`;
  const year = new Date().getFullYear();
  for (const tr of $("tab-chart").querySelectorAll("tr[data-account]")) tr.onclick = () => openGl(tr.dataset.account, `${year}-01-01`, `${year}-12-31`);
}

function accountRow(a, purposeOf, indented) {
  const status = [a.active ? "active" : "inactive", a.used ? "used" : null].filter(Boolean).join(", ");
  return `<tr class="click" data-account="${a.id}"><td>${esc(a.code)}</td><td style="padding-left:${indented ? 24 : 8}px">${esc(a.name)}</td><td class="muted">${status}</td><td>${esc(purposeOf[a.id] ?? "")}</td></tr>`;
}

const accountName = (id) => {
  const a = state.accounts.find((x) => x.id === id);
  return a ? `${a.code} ${a.name}` : id;
};

async function renderJournals() {
  const journals = (await api(`${ledgerUrl()}/journals`)).filter((j) => j.status !== "deleted");
  $("tab-journals").innerHTML = `${canEdit() ? `<p><button onclick="journalForm()">New journal</button></p>` : ""}
    <p class="muted small">Posted journals, plus your own drafts. Click a row for its lines.</p>
    <table><tr><th>No.</th><th>Date</th><th>Reference</th><th>Description</th><th>Status</th><th class="num">Amount</th></tr>
    ${journals.map((j) => `<tr class="click" data-id="${j.id}"><td>${j.journal_no}</td><td>${j.accounting_date}</td><td>${esc(j.reference)}</td>
      <td>${esc(j.description)}</td><td><span class="pill ${j.status}">${j.status}</span></td>
      <td class="num">${j.lines.reduce((s, l) => s + Number(l.debit), 0).toFixed(2)}</td></tr>`).join("")}</table>`;
  for (const tr of $("tab-journals").querySelectorAll("tr[data-id]")) tr.onclick = () => showJournal(journals.find((j) => j.id === tr.dataset.id));
}

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

// Document lists. The layout follows Manager.io for the toolbar and QuickBooks for the table: a two-line
// invoice cell, one amount column, a status badge, a row menu on hover, sticky header and totals, and a
// density switch. Sales and purchases share the renderer.
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
};

const DOC_PRESETS = ["this-month", "last-month", "this-quarter", "this-year", "last-year", "custom"];
const PRESET_LABELS = {
  "this-month": "This month", "last-month": "Last month", "this-quarter": "This quarter",
  "this-year": "This year", "last-year": "Last year", custom: "Custom…",
};

function docFilters(kind) {
  state.docFilters ??= {};
  state.docFilters[kind] ??= { preset: "this-year", from: "", to: "", party: "", q: "" };
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
const DOC_COLUMN_DEFAULTS = ["net_total", "tax_total", "other_tax_total", "description", "fx"];
const docColumns = (kind) => ((state.docCols ??= {})[kind] ??= new Set(DOC_COLUMN_DEFAULTS));
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

async function renderDocList(kind) {
  const k = DOC_KINDS[kind];
  const f = docFilters(kind);
  const tab = docTab(kind);
  const sort = docSort(kind);
  const range = presetRange(f.preset, f);
  // The data is fetched once per list and reused while the page stays open; a save or a delete clears
  // the cache through refreshLedger. Column and density changes then cost nothing.
  state.docData ??= {};
  if (!state.docData[kind]) {
    const [docs, counterparts, parties] = await Promise.all([
      api(`${ledgerUrl()}/${k.path}`),
      k.refPath ? api(`${ledgerUrl()}/${k.refPath}`) : Promise.resolve([]),
      api(`${ledgerUrl()}/parties`),
    ]);
    state.docData[kind] = { docs, counterparts, parties };
  }
  const { docs, counterparts, parties } = state.docData[kind];
  const nameOf = (id) => parties.find((p) => p.id === id)?.name ?? "";
  const partyName = (d) => nameOf(d[k.partyKey]);
  const counterpartNo = (id) => counterparts.find((c) => c.id === id)?.invoice_no ?? "";
  const khr = (d) => (d.tax_currency ? `${money(d.tax_currency.gross_total, "KHR")} ៛` : "");
  const sum = (list, key) => list.reduce((s, d) => s + Number(d[key] ?? 0), 0).toFixed(2);
  const ids = { filter: `${k.container}-filter`, tabs: `${k.container}-tabs`, party: `${k.container}-party`, list: `${k.container}-party-list`, search: `${k.container}-search`, cols: `${k.container}-cols`, colsMenu: `${k.container}-cols-menu`, density: `${k.container}-density`, group: `${k.container}-group`, bar: `${k.container}-bar`, selectAll: `${k.container}-selectall` };

  const inPeriod = docs.filter((d) => d.status !== "deleted");
  const inRange = inPeriod.filter((d) => d.date >= range.from && d.date <= range.to);
  const rows = inRange.filter((d) => (!f.party || d[k.partyKey] === f.party)
    && (!f.q || `${k.prefix} ${d.invoice_no} ${d.reference ?? ""} ${partyName(d)}`.toLowerCase().includes(f.q.toLowerCase())));
  const posted = rows.filter((d) => d.status === "posted");
  const drafts = rows.filter((d) => d.status !== "posted");
  const shown = tab === "posted" ? posted : drafts;
  const amountKey = k.amounts[k.amounts.length - 1].key;
  const amountLabel = k.amounts[k.amounts.length - 1].label;

  const sortValue = (d) => {
    const party = parties.find((p) => p.id === d[k.partyKey]);
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

  const late = posted.filter((d) => d.due_date && d.due_date < today());
  const currency = state.ledger.base_currency;
  const chosen = docColumns(kind);
  const partyOf = (d) => parties.find((p) => p.id === d[k.partyKey]);
  const fxOf = (d) => `${d.currency || currency}${d.exchange_rate ? ` @ ${d.exchange_rate}` : ""}`;

  // Every column the list can show, with its default place. `always` ones cannot be hidden; the order
  // of all of them can be changed, and both choices are kept per list kind for the session.
  const META = {
    party: { label: k.partyLabel, always: true, sortable: true },
    due: { label: "Due", always: true, sortable: true, w: "150px" },
    date: { label: "Date", sortable: true, w: "110px" },
    reference: { label: "Reference", sortable: true, w: "150px" },
    ...Object.fromEntries(k.amounts.slice(0, -1).map((a) => [a.key, { label: a.label, sortable: true, num: true, w: "104px" }])),
    other_tax_total: { label: "Other tax", sortable: true, num: true, w: "104px" },
    amount: { label: `${amountLabel} (${currency})`, always: true, sortable: true, num: true, w: "130px" },
    fx: { label: "Currency", sortable: true, w: "130px" },
    khr: { label: "KHR", sortable: true, num: true, w: "128px" },
    status: { label: "Status", always: true, sortable: true, w: "104px" },
    description: { label: "Description", sortable: true, w: "200px" },
    lines_count: { label: "Lines", sortable: true, num: true, w: "74px" },
    party_tax_id: { label: "Tax ID", sortable: true, w: "140px" },
    party_terms: { label: "Terms (days)", sortable: true, num: true, w: "110px" },
    journal: { label: "Journal No.", sortable: true, w: "110px" },
  };
  const order = docColumnOrder(kind, Object.keys(META));
  const columns = [{ key: "select", label: "", w: "34px" }]
    .concat(order
      .filter((key) => META[key] && (META[key].always || chosen.has(key)))
      .map((key) => ({ key, ...META[key] })))
    .concat([{ key: "menu", label: "", w: "46px" }]);

  const header = columns.map((c) => {
    const cls = [c.num ? "num" : "", c.sortable ? "sortable" : "", c.sortable && sort.key === c.key ? "sorted" : ""].filter(Boolean).join(" ");
    const caret = c.sortable && sort.key === c.key ? `<span class="caret">${sort.dir === "asc" ? "▲" : "▼"}</span>` : "";
    if (c.key === "select") return `<th style="width:${c.w}"><input type="checkbox" id="${ids.selectAll}" title="Select all on this page"></th>`;
    return `<th class="${cls}" ${c.sortable ? `data-sort="${c.key}"` : ""}${c.w ? ` style="width:${c.w}"` : ""}>${c.label}${caret}</th>`;
  }).join("");

  const cell = (d) => {
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
      amount: money(d[amountKey]),
      khr: khr(d),
      fx: fxOf(d),
      status: `<span class="pill ${d.status}">${d.status}</span>`,
      description: `<span class="cell-clip" title="${esc(d.description ?? "")}">${esc(d.description ?? "")}</span>`,
      lines_count: d.lines?.length ?? "",
      party_tax_id: `<span class="cell-clip" title="${esc(party?.tax_id ?? "")}">${esc(party?.tax_id ?? "")}</span>`,
      party_terms: party?.payment_terms_days ?? "",
      journal: d.journal_no ? `No. ${d.journal_no}` : "",
      other_tax_total: money(d.other_tax_total),
      menu: `<button class="row-menu-btn" data-menu="${d.id}" title="Actions">⋯</button>`,
      ...Object.fromEntries(k.amounts.slice(0, -1).map((a) => [a.key, money(d[a.key])])),
    };
  };

  // Grouping by month means whole months, so paging steps aside while it is on.
  const grouped = groupByMonth(kind);
  const pageSize = docPageSize();
  state.docPage ??= {};
  const pageCount = grouped ? 1 : Math.max(1, Math.ceil(sorted.length / pageSize));
  const page = grouped ? 1 : Math.min(state.docPage[kind] ?? 1, pageCount);
  state.docPage[kind] = page;
  const paged = grouped ? sorted : sorted.slice((page - 1) * pageSize, page * pageSize);

  const rowHtml = (d) => {
    const cells = cell(d);
    return `<tr class="click" data-id="${d.id}">${columns.map((c) => `<td class="${c.num ? "num" : ""}">${cells[c.key] ?? ""}</td>`).join("")}</tr>`;
  };
  const subtotalRow = (list, key) => `<tr class="subtotal">${columns.map((c) => {
    if (c.key === "select" || c.key === "menu") return `<td></td>`;
    if (c.key === "party") return `<td><span class="muted">Subtotal</span></td>`;
    if (c.key === "amount") return `<td class="num">${money(sum(list, amountKey))}</td>`;
    if (c.num && c.key !== "lines_count" && c.key !== "party_terms") return `<td class="num">${money(sum(list, c.key))}</td>`;
    if (c.key === "status") return `<td>${list.length} ${list.length === 1 ? k.noun : k.plural}</td>`;
    return `<td></td>`;
  }).join("")}</tr>`;

  const body = grouped
    ? (() => {
        const months = new Map();
        for (const d of sorted) {
          const key = d.date.slice(0, 7);
          if (!months.has(key)) months.set(key, []);
          months.get(key).push(d);
        }
        return [...months.entries()].map(([key, list]) => `<tr class="group-head"><td colspan="${columns.length}">${monthLabel(key)}</td></tr>${list.map(rowHtml).join("")}${subtotalRow(list, key)}`).join("");
      })()
    : paged.map(rowHtml).join("");

  const totals = `<tr class="total">${columns.map((c) => {
    if (c.key === "select" || c.key === "menu") return `<td></td>`;
    if (c.key === "party") return `<td>Total</td>`;
    if (c.key === "status") return `<td>${sorted.length} ${sorted.length === 1 ? k.noun : k.plural}</td>`;
    if (c.key === "amount") return `<td class="num">${money(sum(sorted, amountKey))}</td>`;
    if (c.num && c.key !== "lines_count" && c.key !== "party_terms") return `<td class="num">${money(sum(sorted, c.key))}</td>`;
    return `<td></td>`;
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
    : k.extra === "overdue"
      ? (Number(overdue) > 0 ? `Overdue ${money(overdue)} ${currency}` : "")
      : credited > 0
        ? `${credited} ${k.side === "sale" ? "invoice" : "bill"}${credited === 1 ? "" : "s"} ${k.side === "sale" ? "credited" : "debited"}`
        : "";

  const chips = [
    f.preset !== "this-year"
      ? `<span class="chip">${PRESET_LABELS[f.preset] ?? "This year"}${f.preset === "custom" ? ` · ${range.from} → ${range.to}` : ""}<button data-clear="preset" title="Back to the whole fiscal year">×</button></span>`
      : "",
    f.party ? `<span class="chip">${k.partyLabel}: ${esc(nameOf(f.party))}<button data-clear="party" title="Remove">×</button></span>` : "",
    f.q ? `<span class="chip">Search: “${esc(f.q)}”<button data-clear="q" title="Remove">×</button></span>` : "",
  ].filter(Boolean).join("");
  const filtered = f.party || f.q || f.preset !== "this-year";
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

  $(`tab-${k.container}`).innerHTML = `
    <form class="filter-bar" id="${ids.filter}" autocomplete="off">
      ${canEdit() ? `<button type="button" class="new-doc" onclick="documentForm('${k.formKey}')">+ ${k.newLabel}</button>` : ""}
      <label>Period <select id="${ids.filter}-preset" name="preset">
        ${DOC_PRESETS.map((p) => `<option value="${p}" ${f.preset === p ? "selected" : ""}>${PRESET_LABELS[p]}</option>`).join("")}
      </select></label>
      ${f.preset === "custom" ? `<label>From <input type="date" name="from" value="${range.from}"></label>
        <label>To <input type="date" name="to" value="${range.to}"></label>` : ""}
      <label class="combo-label">${k.partyLabel}
        <span class="combo">
          <input id="${ids.party}" name="party" placeholder="All ${k.partyLabel.toLowerCase()}s" value="${esc(f.party ? nameOf(f.party) : "")}">
          <div class="combo-list" id="${ids.list}" hidden></div>
        </span>
      </label>
      <label class="search">Search <input id="${ids.search}" name="q" value="${esc(f.q)}" placeholder="${k.prefix}, reference, ${k.partyLabel.toLowerCase()}"></label>
      <button type="button" class="secondary small" id="${ids.group}" title="Group by month">${grouped ? "Ungroup" : "Group by month"}</button>
      <button type="button" class="secondary small" id="${ids.cols}" title="Extra columns">Columns</button>
      <button type="button" class="secondary small" id="${ids.density}" title="Row height">${docDensity() === "compact" ? "Comfortable" : "Compact"}</button>
      ${columnsMenu}
    </form>
    ${chipRow}

    <div class="tabs" id="${ids.tabs}">
      <button data-doc-tab="posted" class="${tab === "posted" ? "active" : ""}">Posted <span class="count">${posted.length}</span></button>
      <button data-doc-tab="draft" class="${tab === "draft" ? "active" : ""}">Draft <span class="count">${drafts.length}</span></button>
      <span class="tabs-meta">${tab === "posted"
        ? `showing ${paged.length} of ${posted.length}${facts ? ` · <span class="${Number(overdue) > 0 && k.extra === "overdue" ? "error" : ""}">${facts}</span>` : ""}`
        : `showing ${paged.length} of ${drafts.length} ${drafts.length === 1 ? "draft" : "drafts"} · not in the books`}</span>
    </div>

    <div class="select-bar hidden" id="${ids.bar}">
      <b id="${ids.bar}-count">0</b> selected <span class="muted small" id="${ids.bar}-total"></span>
      <span class="spacer"></span>
      ${k.side === "sale" ? `<button type="button" class="secondary small" data-bulk="print">Download PDF</button>` : ""}
      <button type="button" class="danger small" data-bulk="delete">Delete</button>
      <button type="button" class="secondary small" data-bulk="clear">Clear</button>
    </div>
    ${tab === "draft" && drafts.length ? `<p class="warning">These are not posted, so they are not in the ledger and they block filing the months they are dated in. Fix and post each one, or delete it.</p>` : ""}
    ${sorted.length
      ? `<div class="table-scroll"><table class="list ${docDensity()}"><tr>${header}</tr>${body}${tab === "posted" ? totals : ""}</table></div>${grouped ? "" : pager}`
      : `<p class="muted">No ${tab === "posted" ? "posted" : "draft"} ${k.plural} in ${PRESET_LABELS[f.preset]?.toLowerCase() ?? "this period"}${f.party ? ` for ${esc(nameOf(f.party))}` : ""}${f.q ? ` matching “${esc(f.q)}”` : ""}.</p>`}`;

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
    state.docCols[kind] = new Set(DOC_COLUMN_DEFAULTS);
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
  $(ids.group).onclick = () => { state.groupByMonth[kind] = !grouped; renderDocList(kind); };
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
    $(`${ids.bar}-total`).textContent = picked.length ? `${money(sum(picked, amountKey))} ${currency}` : "";
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
        for (const d of picked) download(`${ledgerUrl()}/${k.path}/${d.id}/pdf`, `${k.prefix}-${d.invoice_no}.pdf`);
        return;
      }
      if (which === "delete") {
        const posted = picked.some((d) => d.status === "posted");
        if (!confirm(`Delete ${picked.length} ${picked.length === 1 ? k.noun : k.plural}?${posted ? " Posted ones lose their journals too." : ""}`)) return;
        try {
          for (const d of picked) await write("DELETE", `${ledgerUrl()}/${k.path}/${d.id}`, undefined, d.version);
          docSelection(kind).clear();
          await refreshLedger();
        } catch (error) {
          showError(error);
        }
      }
    };
  }
  paintBar();

  const partyInput = $(ids.party);
  const partyList = $(ids.list);
  const partyRows = parties.filter(k.partyFilter);
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

  for (const button of $(`tab-${k.container}`).querySelectorAll("[data-clear]")) {
    button.onclick = () => {
      const what = button.dataset.clear;
      if (what === "party") f.party = "";
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

  let typing;
  $(ids.search).addEventListener("input", (event) => {
    const value = event.target.value;
    clearTimeout(typing);
    typing = setTimeout(() => {
      f.q = value.trim();
      state.keepSearchFocus = true;
      state.docPage[kind] = 1;
      renderDocList(kind);
    }, 250);
  });
  if (state.keepSearchFocus) {
    state.keepSearchFocus = false;
    const field = $(ids.search);
    field.focus();
    field.setSelectionRange(field.value.length, field.value.length);
  }

  for (const b of $(ids.tabs).querySelectorAll("button")) {
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

  const openDoc = (doc) => (k.side === "sale" ? showSale(doc, nameOf) : showPurchase(doc, nameOf));
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
        ...(k.side === "sale" ? [{ label: "Print PDF", run: () => openPdf(`${k.path}/${doc.id}/pdf`) }] : []),
        ...(canEdit() ? [{ label: "Edit", run: () => editDocument(k.side, doc.type, doc.id) }] : []),
        ...(canEdit() ? [{
          label: "Delete", danger: true, run: async () => {
            if (!confirm(`Delete ${k.noun} ${doc.invoice_no}?${doc.status === "posted" ? " Its journal is removed too." : ""}`)) return;
            try {
              await write("DELETE", `${ledgerUrl()}/${k.path}/${doc.id}`, undefined, doc.version);
              await refreshLedger();
            } catch (error) { showError(error); }
          },
        }] : []),
      ]);
    };
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

async function renderBank() {
  const [banks, payments, receipts, parties, transfers] = await Promise.all([
    api(`${ledgerUrl()}/bank-accounts`), api(`${ledgerUrl()}/payments`), api(`${ledgerUrl()}/receipts`), api(`${ledgerUrl()}/parties`),
    api(`${ledgerUrl()}/transfers`),
  ]);
  if (!banks.length) {
    $("tab-bank").innerHTML = `<p class="muted">No bank accounts.</p>`;
    return;
  }
  const bank = banks.find((b) => b.id === state.bankId) ?? banks[0];
  const statements = await api(`${ledgerUrl()}/bank-accounts/${bank.account_id ?? bank.id}/statements`);
  const latest = statements[0];
  const r = await api(`${ledgerUrl()}/bank-accounts/${bank.account_id ?? bank.id}/reconciliation?asOf=${latest?.to ?? "2026-10-31"}${latest ? `&statementBalance=${latest.closing_balance}` : ""}`);
  const party = (id) => parties.find((p) => p.id === id)?.name ?? "";
  const docs = [...payments, ...receipts].filter((d) => d.bank_account_id === (bank.account_id ?? bank.id) && d.status !== "deleted").sort((a, b) => a.date.localeCompare(b.date));
  const shownTransfers = transfers.filter((t) => t.status !== "deleted");
  $("tab-bank").innerHTML = `
    ${canEdit() ? `<p><button onclick="paymentForm('payment')">New payment</button> <button class="secondary" onclick="paymentForm('receipt')">New receipt</button></p>` : ""}
    <p><label>Account <select id="bank-pick">${banks.map((b) => `<option value="${b.account_id ?? b.id}" ${b === bank ? "selected" : ""}>${esc(b.code)} ${esc(b.name)} · ${esc(b.bank_name)}</option>`).join("")}</select></label></p>
    <h3>Payments and receipts</h3>
    <table><tr><th>No.</th><th>Date</th><th>Party</th><th>Reference</th><th>Lines</th><th class="num">In</th><th class="num">Out</th><th>Status</th></tr>
    ${docs.map((d) => `<tr class="click" data-pay="${d.id}"><td>${d.type === "receipt" ? "RC" : "PM"} ${d.payment_no}</td><td>${d.date}</td><td>${esc(party(d.party_id))}</td><td>${esc(d.reference ?? "")}</td>
      <td>${d.lines.map((l) => `${l.kind === "account" ? esc(accountName(l.account_id)) : l.kind === "invoice" ? "invoice" : "party balance"} ${l.amount}`).join("; ")}</td>
      <td class="num">${d.type === "receipt" ? d.total : ""}</td><td class="num">${d.type === "payment" ? d.total : ""}</td>
      <td>${d.status === "posted" ? `<span class="pill posted">posted</span>` : `<span class="pill draft">${d.status}</span> ${d.issues.map((i) => esc(i.message)).join(" ")}`}</td></tr>`).join("")}</table>
    <h3 style="margin-top:16px">Bank transfers ${canEdit() ? `<button class="secondary small" onclick="transferForm()">New</button>` : ""}</h3>
    ${shownTransfers.length ? `<table><tr><th>No.</th><th>Date</th><th>From</th><th>To</th><th class="num">Amount</th><th class="num">Fee</th><th>Journal</th></tr>
      ${shownTransfers.map((t) => `<tr ${canEdit() ? `class="click" data-transfer="${t.id}"` : ""}><td>${t.transfer_no}</td><td>${t.date}</td><td>${esc(accountName(t.from_account_id))}</td><td>${esc(accountName(t.to_account_id))}</td>
        <td class="num">${t.amount}${t.to_amount ? ` → ${t.to_amount}` : ""}</td><td class="num">${t.fee_amount}</td>
        <td>${t.status === "posted" ? `posted, journal No. ${t.journal_no}` : `<span class="pill draft">${t.status}</span> ${t.issues.map((i) => esc(i.message)).join(" ")}`}</td></tr>`).join("")}</table>`
      : `<p class="muted small">No transfers yet. A transfer moves money between two bank accounts, in one journal.</p>`}
    <h3 style="margin-top:16px">Reconciliation at ${r.as_of}</h3>
    <p>Book ${r.book_balance} · cleared ${r.cleared_balance}${r.statement_balance ? ` · statement ${r.statement_balance} · ${r.reconciled ? `<span class="ok">reconciled</span>` : `<span class="error">difference ${r.difference}</span>`}` : ""}</p>
    <table><tr><th>Pending</th><th>Date</th><th>Source</th><th>Reference</th><th class="num">Amount</th></tr>
    ${r.pending.slice(-15).map((p) => `<tr class="click" data-journal="${p.journal_id}"><td>No. ${p.journal_no}</td><td>${p.date}</td><td>${p.source}</td><td>${esc(p.reference ?? "")}</td><td class="num">${p.amount}</td></tr>`).join("")}</table>
    ${r.pending.length > 15 ? `<p class="muted">${r.pending.length - 15} earlier pending postings not shown.</p>` : ""}
    ${statements.map((st) => `<h3 style="margin-top:16px">Statement ${st.from} – ${st.to}</h3>
      <p class="muted">Opening ${st.opening_balance} · closing ${st.closing_balance} · ${st.matched} matched · ${st.drafted} to Suspense (drafts) · ${st.duplicates} duplicates</p>
      <table><tr><th>Date</th><th>Description</th><th class="num">Amount</th><th>Status</th></tr>
      ${st.lines.map((l) => `<tr class="${l.status === "duplicate" ? "" : "click"}" data-line-journal="${l.journal_id ?? ""}" data-line-payment="${l.payment_id ?? ""}" data-line-kind="${Number(l.amount) > 0 ? "receipt" : "payment"}"><td>${l.date}</td><td>${esc(l.description ?? "")}</td><td class="num">${l.amount}</td><td><span class="pill ${l.status === "matched" ? "posted" : "draft"}">${l.status}</span></td></tr>`).join("")}</table>`).join("")}`;
  $("bank-pick").onchange = (e) => { state.bankId = e.target.value; renderBank(); };
  for (const tr of $("tab-bank").querySelectorAll("tr[data-pay]")) tr.onclick = () => showPayment(docs.find((d) => d.id === tr.dataset.pay), party);
  for (const tr of $("tab-bank").querySelectorAll("tr[data-transfer]")) tr.onclick = () => transferForm(shownTransfers.find((t) => t.id === tr.dataset.transfer));
  for (const tr of $("tab-bank").querySelectorAll("tr[data-journal]")) tr.onclick = () => openJournal(tr.dataset.journal);
  for (const tr of $("tab-bank").querySelectorAll("tr[data-line-kind]")) {
    tr.onclick = () => tr.dataset.lineJournal ? openJournal(tr.dataset.lineJournal) : tr.dataset.linePayment && openDocument(tr.dataset.lineKind, tr.dataset.linePayment);
  }
}

async function renderTax(period) {
  let r;
  try {
    r = await api(`${ledgerUrl()}/tax-returns/${period}`);
  } catch (e) {
    $("tab-tax").innerHTML = `<p class="muted">${esc(e.message)}</p>`;
    return;
  }
  const sections = [...new Set(r.boxes.map((b) => b.section))];
  const months = Array.from({ length: 12 }, (_, i) => `2026-${String(i + 1).padStart(2, "0")}`);
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
      ${r.adjusted_lines.map((l) => `<tr><td>${l.document_type} ${l.document_no}</td><td>${esc(l.party)}</td><td class="num">${l.tax_calculated}</td><td class="num">${l.tax_amount}</td><td>${esc(l.tax_reason ?? "")}</td></tr>`).join("")}</table>` : ""}`;
  $("tax-period").onchange = (e) => renderTax(e.target.value);
  $("tax-xlsx").onclick = (e) => { e.preventDefault(); download(`${ledgerUrl()}/tax-returns/${period}.xlsx`, `tax-return-${period}.xlsx`); };
  for (const tr of $("tab-tax").querySelectorAll("tr.click")) {
    tr.onclick = async () => {
      const box = await api(`${ledgerUrl()}/tax-returns/${period}/boxes/${tr.dataset.box}`);
      $("journal-body").innerHTML = `<h3>${esc(box.label)} · ${box.period}</h3><p class="muted">${box.amount} · ${box.tax_currency_amount ?? ""}</p>
        <table><tr><th>Document</th><th>Date</th><th>Party</th><th>Line</th><th class="num">Amount</th><th class="num">Rate</th><th class="num">KHR</th></tr>
        ${box.lines.map((l) => `<tr class="click" onclick="openDocument('${l.document_type}', '${l.document_id}')"><td>${l.document_type.replaceAll("_", " ")} ${l.document_no}</td><td>${l.date}</td><td>${esc(l.party)}</td><td>${esc(l.description ?? "")}</td>
          <td class="num">${l.amount}</td><td class="num">${l.tax_currency_rate ?? ""}</td><td class="num">${l.tax_currency_amount ? Number(l.tax_currency_amount).toLocaleString() : ""}</td></tr>`).join("")}</table>`;
      $("journal-dialog").showModal();
    };
  }
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