// Preview of Books, for local testing only. Plain JavaScript, no build step; data entry is in forms.js.
const state = { token: sessionStorage.getItem("books.token"), org: null, ledger: null, accounts: [] };
const $ = (id) => document.getElementById(id);
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
    { id: "purchases", label: "Purchases" },
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
  "sales-invoices": { title: "Sales invoices", sub: "Sales · posted and draft invoices, and what they add up to", render: () => renderSalesDocs("invoice") },
  "sales-credit-notes": { title: "Sales credit notes", sub: "Sales · credits issued, and the invoice each one reduces", render: () => renderSalesDocs("credit_note") },
  "sales-reports": { title: "Sales reports", sub: "Sales · who owes us, how late, and their statements", render: () => renderSalesReports() },
  "sales-settings": { title: "Sales settings", sub: "Sales · the seller block and the numbers printed on invoices", render: () => renderSalesSettings() },
  purchases: { title: "Purchases", sub: "Purchase invoices and debit notes, and what we owe suppliers", render: () => renderPurchases() },
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

// The sales pages. Invoices and credit notes share one renderer: filters, figures, a Posted / Draft
// pair of tabs, and a table. Drafts get their own tab rather than their own page: a draft is a
// document that did not post, and a draft dated in a month blocks filing that month.
const SALES_DOCS = {
  invoice: { path: "sales-invoices", noun: "invoice", plural: "invoices", tab: "sales-invoices" },
  credit_note: { path: "credit-notes", noun: "credit note", plural: "credit notes", tab: "sales-credit-notes" },
};

function salesFilters(kind) {
  state.salesFilters ??= {};
  state.salesFilters[kind] ??= { from: `${today().slice(0, 4)}-01-01`, to: today(), customer: "", q: "" };
  return state.salesFilters[kind];
}

const docTab = (kind) => ((state.docTab ??= {})[kind] ??= "posted");
const overdueDays = (d) => Math.floor((new Date(today()) - new Date(d.due_date)) / 86400000);

async function renderSalesDocs(kind) {
  const meta = SALES_DOCS[kind];
  const f = salesFilters(kind);
  const tab = docTab(kind);
  const [docs, invoices, parties] = await Promise.all([
    api(`${ledgerUrl()}/${meta.path}`), api(`${ledgerUrl()}/sales-invoices`), api(`${ledgerUrl()}/parties`),
  ]);
  const party = (id) => parties.find((p) => p.id === id)?.name ?? "";
  const invoiceNo = (id) => invoices.find((i) => i.id === id)?.invoice_no ?? "";
  const khr = (d) => (d.tax_currency ? `${Number(d.tax_currency.gross_total).toLocaleString()} ៛` : "");
  const rows = docs.filter((d) => d.status !== "deleted"
    && d.date >= f.from && d.date <= f.to
    && (!f.customer || d.customer_id === f.customer)
    && (!f.q || `${d.invoice_no} ${d.reference ?? ""} ${party(d.customer_id)}`.toLowerCase().includes(f.q.toLowerCase())));
  const outside = docs.filter((d) => d.status !== "deleted" && (d.date < f.from || d.date > f.to)).length;
  const posted = rows.filter((d) => d.status === "posted");
  const drafts = rows.filter((d) => d.status !== "posted");
  const shown = tab === "posted" ? posted : drafts;
  const sum = (list, key) => list.reduce((s, d) => s + Number(d[key]), 0).toFixed(2);
  const late = posted.filter((d) => d.due_date && d.due_date < today());
  const currency = state.ledger.base_currency;
  const container = meta.tab;

  setActions(canEdit() ? `<button onclick="documentForm('${kind}')">New ${meta.noun}</button>` : "");

  $(`tab-${container}`).innerHTML = `
    <form class="filter-bar" id="docs-filter">
      <label>From <input type="date" name="from" value="${f.from}"></label>
      <label>To <input type="date" name="to" value="${f.to}"></label>
      <label>Customer <select name="customer">${options(parties.filter((p) => p.is_customer), f.customer, (p) => p.name, "All customers")}</select></label>
      <label>Search <input name="q" value="${esc(f.q)}" placeholder="Number, reference, customer"></label>
      <button type="submit">Apply</button>
      <button type="button" class="secondary" id="docs-reset">Reset</button>
    </form>

    <div class="tabs" id="docs-tabs">
      <button data-doc-tab="posted" class="${tab === "posted" ? "active" : ""}">Posted <span class="count">${posted.length}</span></button>
      <button data-doc-tab="draft" class="${tab === "draft" ? "active" : ""}">Draft <span class="count">${drafts.length}</span></button>
    </div>

    ${outside ? `<p class="muted small">${outside} more ${outside === 1 ? meta.noun : meta.plural} dated outside this period. <button class="link" id="docs-widen">Show to end of year</button></p>` : ""}
    <div class="figures">
      <div class="figure"><span class="muted small">${tab === "posted" ? "Posted" : "Draft"} ${meta.plural}</span><b>${shown.length}</b></div>
      <div class="figure"><span class="muted small">Net</span><b>${sum(shown, "net_total")} ${currency}</b></div>
      <div class="figure"><span class="muted small">VAT</span><b>${sum(shown, "tax_total")} ${currency}</b></div>
      <div class="figure"><span class="muted small">Gross</span><b>${sum(shown, "gross_total")} ${currency}</b></div>
      ${tab === "draft"
        ? `<div class="figure"><span class="muted small">Drafts block filing the month</span><b>${drafts.length} to fix</b></div>`
        : kind === "credit_note"
          ? `<div class="figure"><span class="muted small">Invoices credited</span><b>${new Set(shown.map((d) => d.original_invoice_id).filter(Boolean)).size}</b></div>`
          : `<div class="figure"><span class="muted small">Overdue</span><b class="${late.length ? "error" : ""}">${sum(late, "gross_total")} ${currency}</b></div>`}
    </div>

    ${tab === "draft" && drafts.length ? `<p class="warning">These are not posted, so they are not in the ledger and they block filing the months they are dated in. Fix and post each one, or delete it.</p>` : ""}
    ${shown.length ? `<table>
      <tr><th>No.</th><th>Date</th>${kind === "invoice" ? "<th>Due</th>" : "<th>Credits</th>"}<th>Customer</th><th>Reference</th>
        <th class="num">Net</th><th class="num">VAT</th><th class="num">Gross</th><th class="num">KHR</th><th>Status</th></tr>
      ${shown.map((d) => {
        const isLate = d.status === "posted" && d.due_date && d.due_date < today();
        const third = kind === "invoice"
          ? `<td>${d.due_date ?? ""}${isLate ? ` <span class="pill draft" title="${overdueDays(d)} days overdue">overdue</span>` : ""}</td>`
          : `<td>${d.original_invoice_id ? `SI ${invoiceNo(d.original_invoice_id)}` : ""}</td>`;
        return `<tr class="click" data-id="${d.id}"><td>${kind === "credit_note" ? "CN" : "SI"} ${d.invoice_no}</td><td>${d.date}</td>${third}
          <td>${esc(party(d.customer_id))}</td><td>${esc(d.reference ?? "")}</td>
          <td class="num">${d.net_total}</td><td class="num">${d.tax_total}</td><td class="num">${d.gross_total}</td><td class="num">${khr(d)}</td>
          <td>${d.status === "posted" ? `<span class="pill posted">posted</span> No. ${d.journal_no ?? ""}` : `<span class="pill ${d.status}">${d.status}</span> ${d.issues.map((i) => esc(i.message)).join(" ")}`}</td></tr>`;
      }).join("")}
      <tr class="total"><td colspan="4">${shown.length} ${tab === "posted" ? "posted" : "draft"} ${shown.length === 1 ? meta.noun : meta.plural}</td>
        <td></td><td class="num">${sum(shown, "net_total")}</td><td class="num">${sum(shown, "tax_total")}</td><td class="num">${sum(shown, "gross_total")}</td><td></td><td></td></tr></table>`
      : `<p class="muted">No ${tab === "posted" ? "posted" : "draft"} ${meta.plural} in this period.</p>`}`;

  $("docs-filter").onsubmit = (event) => {
    event.preventDefault();
    const form = event.target;
    state.salesFilters[kind] = { from: form.from.value, to: form.to.value, customer: form.customer.value, q: form.q.value.trim() };
    renderSalesDocs(kind);
  };
  $("docs-reset").onclick = () => { state.salesFilters[kind] = undefined; renderSalesDocs(kind); };
  $("docs-widen")?.addEventListener("click", () => { f.to = `${today().slice(0, 4)}-12-31`; renderSalesDocs(kind); });
  for (const b of $("docs-tabs").querySelectorAll("button")) {
    b.onclick = () => { state.docTab[kind] = b.dataset.docTab; renderSalesDocs(kind); };
  }
  for (const tr of $(`tab-${container}`).querySelectorAll("tr[data-id]")) {
    tr.onclick = () => showSale(shown.find((d) => d.id === tr.dataset.id), party);
  }
}

// Who owes us, how late, and their statements. A report, not a document list, so it keeps its own
// as-of date instead of sharing the list filters.
async function renderSalesReports() {
  const asOf = state.agingAsOf ?? today();
  const aging = await api(`${ledgerUrl()}/reports/ar-aging?asOf=${asOf}`);
  setActions("");
  $("tab-sales-reports").innerHTML = `
    <form class="filter-bar" id="aging-form">
      <label>As of <input type="date" name="asOf" value="${asOf}"></label>
      <button type="submit">Run</button>
    </form>
    <p class="muted small">${aging.matches_control ? `<span class="ok">Equals the receivable balance ${aging.control_balance} ${aging.currency}</span>` : `<span class="error">Does not equal the receivable balance ${aging.control_balance} ${aging.currency}</span>`} · click a customer for their statement</p>
    ${aging.rows.length ? `<table><tr><th>Customer</th><th class="num">Not due</th><th class="num">1–30</th><th class="num">31–60</th><th class="num">61–90</th><th class="num">Over 90</th><th class="num">Total</th></tr>
      ${[...aging.rows, aging.totals].map((r) => `<tr class="${r === aging.totals ? "total" : "click"}" ${r === aging.totals ? "" : `data-customer="${r.customer_id}"`}><td>${esc(r.customer)}</td>
        <td class="num">${r.not_due}</td><td class="num">${r.days1_to30}</td><td class="num">${r.days31_to60}</td><td class="num">${r.days61_to90}</td><td class="num">${r.over90}</td><td class="num">${r.total}</td></tr>`).join("")}</table>`
      : `<p class="muted">Nothing is owed at ${aging.as_of}.</p>`}`;
  $("aging-form").onsubmit = (event) => { event.preventDefault(); state.agingAsOf = event.target.asOf.value; renderSalesReports(); };
  for (const tr of $("tab-sales-reports").querySelectorAll("tr[data-customer]")) {
    tr.onclick = () => showStatement("customer", tr.dataset.customer, aging.as_of);
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

async function renderPurchases() {
  const [invoices, notes, parties, aging] = await Promise.all([
    api(`${ledgerUrl()}/purchase-invoices`), api(`${ledgerUrl()}/debit-notes`), api(`${ledgerUrl()}/parties`),
    api(`${ledgerUrl()}/reports/ap-aging?asOf=2026-10-31`),
  ]);
  const party = (id) => parties.find((p) => p.id === id)?.name ?? "";
  const docRows = (docs) => docs.map((d) => `<tr class="click" data-id="${d.id}"><td>${d.type === "debit_note" ? "DN" : "PI"} ${d.invoice_no}</td><td>${d.date}</td>
    <td>${esc(party(d.supplier_id))}</td><td>${esc(d.reference)}</td><td class="num">${d.net_total}</td><td class="num">${d.tax_total}</td>
    <td class="num">${d.withholding_total}</td><td class="num">${d.payable_total}</td>
    <td>${d.status === "posted" ? `<span class="pill posted">posted</span> No. ${d.journal_no}` : `<span class="pill draft">${d.status}</span> ${d.issues.map((i) => esc(i.message)).join(" ")}`}</td></tr>`).join("");
  const a = aging.totals;
  $("tab-purchases").innerHTML = `
    ${canEdit() ? `<p><button onclick="documentForm('purchase_invoice')">New purchase invoice</button> <button class="secondary" onclick="documentForm('debit_note')">New debit note</button></p>` : ""}
    <h3>Purchase invoices and debit notes</h3>
    <table><tr><th>No.</th><th>Date</th><th>Supplier</th><th>Reference</th><th class="num">Net</th><th class="num">VAT</th><th class="num">WHT</th><th class="num">Payable</th><th>Status</th></tr>
    ${docRows([...invoices, ...notes].filter((d) => d.status !== "deleted"))}</table>
    <h3 style="margin-top:16px">AP aging at ${aging.as_of}</h3>
    <p>${aging.matches_control ? `<span class="ok">Equals the payable balance ${aging.control_balance}</span>` : `<span class="error">Does not equal the payable balance ${aging.control_balance}</span>`}</p>
    <table><tr><th>Supplier</th><th class="num">Not due</th><th class="num">1–30</th><th class="num">31–60</th><th class="num">61–90</th><th class="num">Over 90</th><th class="num">Total</th></tr>
    ${[...aging.rows, a].map((r) => `<tr class="${r === a ? "total" : "click"}" ${r === a ? "" : `data-supplier="${r.supplier_id}"`}><td>${esc(r.supplier)}</td><td class="num">${r.not_due}</td><td class="num">${r.days1_to30}</td>
      <td class="num">${r.days31_to60}</td><td class="num">${r.days61_to90}</td><td class="num">${r.over90}</td><td class="num">${r.total}</td></tr>`).join("")}</table>`;
  for (const tr of $("tab-purchases").querySelectorAll("tr[data-id]")) {
    tr.onclick = () => showPurchase([...invoices, ...notes].find((d) => d.id === tr.dataset.id), party);
  }
  for (const tr of $("tab-purchases").querySelectorAll("tr[data-supplier]")) tr.onclick = () => showStatement("supplier", tr.dataset.supplier, aging.as_of);
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