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
    $("demo").textContent = "Create demo organization";
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

async function loadOrganizations(select) {
  const orgs = await api("/api/v1/me/organizations");
  $("orgs").innerHTML = orgs.length
    ? orgs.map((o) => `<li data-id="${o.id}">${esc(o.name)} <span class="pill">${esc(o.role)}</span></li>`).join("")
    : `<li class="muted">No organizations yet.</li>`;
  for (const li of $("orgs").querySelectorAll("li[data-id]")) li.onclick = () => chooseOrganization(li.dataset.id);
  const first = select ?? orgs[0]?.id;
  if (first) await chooseOrganization(first);
}

async function chooseOrganization(orgId) {
  state.org = orgId;
  for (const li of $("orgs").querySelectorAll("li")) li.classList.toggle("selected", li.dataset.id === orgId);
  const ledgers = await api(`/api/v1/organizations/${orgId}/ledgers`);
  $("ledgers").innerHTML = ledgers.length
    ? ledgers.map((l) => `<li data-id="${l.id}">${esc(l.company_name)} <span class="pill">${esc(l.base_currency)} · ${esc(l.effective_access)}</span></li>`).join("")
    : `<li class="muted">No ledgers you can see.</li>`;
  for (const li of $("ledgers").querySelectorAll("li[data-id]")) li.onclick = () => chooseLedger(ledgers.find((l) => l.id === li.dataset.id));
  if (ledgers[0]) await chooseLedger(ledgers[0]);
  else $("ledger").classList.add("hidden");
}

const ledgerUrl = () => `/api/v1/organizations/${state.org}/ledgers/${state.ledger.id}`;

async function chooseLedger(ledger) {
  state.ledger = ledger;
  for (const li of $("ledgers").querySelectorAll("li")) li.classList.toggle("selected", li.dataset.id === ledger.id);
  $("ledger").classList.remove("hidden");
  $("ledger-title").textContent = ledger.company_name;
  const months = ["", "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];
  $("ledger-meta").textContent = `Base currency ${ledger.base_currency} · fiscal year starts in ${months[ledger.fy_start_month]} · `
    + (ledger.closed_through ? `closed through ${ledger.closed_through}` : "not closed");
  await renderChart();
  await renderJournals();
  await renderSetup();
  await renderSales();
  await renderPurchases();
  await renderBank();
  await renderTax("2026-10");
  for (const id of ["tb-out", "gl-out", "pl-out", "bs-out"]) $(id).innerHTML = "";
  for (const id of ["tb-form", "pl-form", "bs-form"]) $(id).requestSubmit();
}

// Drilldown: a report's account opens its GL detail for the same period; a GL line opens its journal.
function openGl(accountId, from, to) {
  const f = $("gl-form");
  f.account.value = accountId;
  f.from.value = from;
  f.to.value = to;
  document.querySelector('.tabs button[data-tab="gl"]').click();
  f.requestSubmit();
}

const SOURCES = {
  purchase_invoice: "Purchase invoice", purchase_debit_note: "Debit note", sales_invoice: "Sales invoice", sales_credit_note: "Credit note",
  payment: "Payment", receipt: "Receipt", bank_transfer: "Bank transfer", pay_run: "Pay run",
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
  payment: "payments", receipt: "receipts", pay_run: "pay-runs", bank_transfer: "transfers",
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
    else if (doc.journal_id) openJournal(doc.journal_id);
  } catch (error) {
    showError(error);
  }
}

const journalButton = (d) => d.journal_id ? `<p><button class="secondary" onclick="openJournal('${d.journal_id}')">Journal ${d.journal_no ?? ""}</button></p>` : "";

function showPayment(d, party) {
  $("journal-body").innerHTML = `<h3>${d.type === "receipt" ? "Receipt" : "Payment"} ${d.payment_no} <span class="pill ${d.status}">${d.status}</span></h3>
    <p class="muted">${esc(accountName(d.bank_account_id))} · ${d.date} · ${esc(party(d.party_id))} · ${esc(d.reference ?? "")} · ${esc(d.description ?? "")}</p>
    <table><tr><th>Line</th><th>Description</th><th class="num">Amount</th><th class="num">VAT</th><th class="num">WHT</th><th class="num">Total</th></tr>
    ${d.lines.map((l) => `<tr><td>${l.kind === "account" ? esc(accountName(l.account_id)) : `${esc(accountName(l.control_account_id))} · ${l.kind === "invoice" ? "settles an invoice" : "party balance"}`}</td>
      <td>${esc(l.description ?? "")}</td><td class="num">${l.amount}</td><td class="num">${blank(l.tax_amount)}</td><td class="num">${blank(l.withholding_amount)}</td><td class="num">${l.total}</td></tr>`).join("")}
    <tr class="total"><td colspan="5">Total</td><td class="num">${d.total}</td></tr></table>
    ${d.issues.map((i) => `<div class="warning">${esc(i.code)}: ${esc(i.message)}</div>`).join("")}${journalButton(d)}`;
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

for (const button of document.querySelectorAll(".tabs button")) {
  button.onclick = () => {
    for (const b of document.querySelectorAll(".tabs button")) b.classList.toggle("active", b === button);
    for (const tab of document.querySelectorAll(".tab")) tab.classList.toggle("hidden", tab.id !== `tab-${button.dataset.tab}`);
  };
}

const TYPES = { assets: "Assets", liabilities: "Liabilities", equity: "Equity", income: "Income", cost_and_expense: "Cost & Expense" };

async function renderChart() {
  const [groups, accounts, purposes] = await Promise.all([
    api(`${ledgerUrl()}/groups`), api(`${ledgerUrl()}/accounts`), api(`${ledgerUrl()}/purposes`),
  ]);
  state.accounts = accounts;
  const purposeOf = Object.fromEntries(purposes.filter((p) => p.account_id).map((p) => [p.account_id, p.purpose.replaceAll("_", " ")]));
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
  $("gl-form").account.innerHTML = accounts.map((a) => `<option value="${a.id}">${esc(a.code)} ${esc(a.name)}</option>`).join("");
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
  const journals = await api(`${ledgerUrl()}/journals`);
  $("tab-journals").innerHTML = `<p class="muted small">Posted journals, plus your own drafts. Click a row for its lines.</p>
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
    ${j.issues.map((i) => `<div class="warning">${esc(i.code)}: ${esc(i.message)}</div>`).join("")}`;
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

async function renderSetup() {
  const [parties, taxCodes, banks, items, transfers] = await Promise.all([
    api(`${ledgerUrl()}/parties`), api(`${ledgerUrl()}/tax-codes`), api(`${ledgerUrl()}/bank-accounts`),
    api(`${ledgerUrl()}/items`), api(`${ledgerUrl()}/transfers`),
  ]);
  const roles = (p) => [p.is_customer ? "customer" : null, p.is_supplier ? "supplier" : null].filter(Boolean).join(", ");
  const taxName = (id) => taxCodes.find((t) => t.id === id)?.name ?? "";
  $("tab-setup").innerHTML = `
    <h3>Customers and suppliers ${canEdit() ? `<button class="secondary small" onclick="partyForm()">New</button>` : ""}</h3>
    <table><tr><th>Name</th><th>Role</th><th>Tax ID</th><th>Contact</th><th class="num">Terms (days)</th></tr>
    ${parties.map((p) => `<tr ${canEdit() ? `class="click" onclick="editParty('${p.id}')"` : ""}><td>${esc(p.name)}${p.active ? "" : ` <span class="pill">inactive</span>`}</td><td>${roles(p)}</td><td>${esc(p.tax_id)}</td><td>${esc(p.contact_name)}</td><td class="num">${p.payment_terms_days ?? ""}</td></tr>`).join("")}</table>
    <h3 style="margin-top:16px">Tax codes</h3>
    <table><tr><th>Name</th><th>Kind</th><th class="num">Rate %</th><th>Account</th><th>Effective</th></tr>
    ${taxCodes.map((t) => `<tr><td>${esc(t.name)}</td><td>${t.kind.replaceAll("_", " ")}</td><td class="num">${t.rate}</td><td>${t.account_id ? esc(accountName(t.account_id)) : ""}</td>
      <td>${t.effective_from ? `from ${t.effective_from}` : ""}${t.effective_to ? ` to ${t.effective_to}` : ""}</td></tr>`).join("")}</table>
    <h3 style="margin-top:16px">Bank accounts</h3>
    <table><tr><th>Account</th><th>Bank</th><th>Number</th><th>Currency</th></tr>
    ${banks.map((b) => `<tr><td>${esc(b.code)} ${esc(b.name)}</td><td>${esc(b.bank_name)}</td><td>${esc(b.account_number)}</td><td>${b.currency}</td></tr>`).join("")}</table>
    <h3 style="margin-top:16px">Bank transfers</h3>
    <table><tr><th>No.</th><th>Date</th><th>From</th><th>To</th><th class="num">Amount</th><th class="num">Fee</th><th>Journal</th></tr>
    ${transfers.map((t) => `<tr><td>${t.transfer_no}</td><td>${t.date}</td><td>${esc(accountName(t.from_account_id))}</td><td>${esc(accountName(t.to_account_id))}</td>
      <td class="num">${t.amount}</td><td class="num">${t.fee_amount}</td>
      <td>${t.status === "posted" ? `posted, journal No. ${t.journal_no}` : `<span class="pill draft">${t.status}</span> ${t.issues.map((i) => esc(i.message)).join(" ")}`}</td></tr>`).join("")}</table>
    <h3 style="margin-top:16px">Items</h3>
    <table><tr><th>Item</th><th>Unit</th><th>Suggests account</th><th>Suggests tax</th></tr>
    ${items.map((i) => `<tr><td>${esc(i.name)}</td><td>${esc(i.unit)}</td><td>${i.default_account_id ? esc(accountName(i.default_account_id)) : ""}</td><td>${esc(taxName(i.default_tax_code_id))}</td></tr>`).join("")}</table>`;
}

async function renderSales() {
  const [invoices, notes, parties, aging] = await Promise.all([
    api(`${ledgerUrl()}/sales-invoices`), api(`${ledgerUrl()}/credit-notes`), api(`${ledgerUrl()}/parties`),
    api(`${ledgerUrl()}/reports/ar-aging?asOf=2026-10-31`),
  ]);
  const party = (id) => parties.find((p) => p.id === id)?.name ?? "";
  const docs = [...invoices, ...notes].filter((d) => d.status !== "deleted");
  const khr = (d) => (d.tax_currency ? `${Number(d.tax_currency.gross_total).toLocaleString()} ៛` : "");
  const a = aging.totals;
  $("tab-sales").innerHTML = `
    ${canEdit() ? `<p><button onclick="salesForm('invoice')">New invoice</button> <button class="secondary" onclick="salesForm('credit_note')">New credit note</button></p>` : ""}
    <h3>Sales invoices and credit notes</h3>
    <table><tr><th>No.</th><th>Date</th><th>Customer</th><th>Reference</th><th class="num">Net</th><th class="num">VAT</th><th class="num">Gross</th><th class="num">Gross KHR</th><th>Status</th></tr>
    ${docs.map((d) => `<tr class="click" data-id="${d.id}"><td>${d.type === "credit_note" ? "CN" : "SI"} ${d.invoice_no}</td><td>${d.date}</td>
      <td>${esc(party(d.customer_id))}</td><td>${esc(d.reference)}</td><td class="num">${d.net_total}</td><td class="num">${d.tax_total}</td>
      <td class="num">${d.gross_total}</td><td class="num">${khr(d)}</td>
      <td>${d.status === "posted" ? `<span class="pill posted">posted</span> No. ${d.journal_no}` : `<span class="pill draft">${d.status}</span> ${d.issues.map((i) => esc(i.message)).join(" ")}`}</td></tr>`).join("")}</table>
    <h3 style="margin-top:16px">AR aging at ${aging.as_of}</h3>
    <p>${aging.matches_control ? `<span class="ok">Equals the receivable balance ${aging.control_balance}</span>` : `<span class="error">Does not equal the receivable balance ${aging.control_balance}</span>`}</p>
    <table><tr><th>Customer</th><th class="num">Not due</th><th class="num">1–30</th><th class="num">31–60</th><th class="num">61–90</th><th class="num">Over 90</th><th class="num">Total</th></tr>
    ${[...aging.rows, a].map((r) => `<tr class="${r === a ? "total" : "click"}" ${r === a ? "" : `data-customer="${r.customer_id}"`}><td>${esc(r.customer)}</td><td class="num">${r.not_due}</td><td class="num">${r.days1_to30}</td>
      <td class="num">${r.days31_to60}</td><td class="num">${r.days61_to90}</td><td class="num">${r.over90}</td><td class="num">${r.total}</td></tr>`).join("")}</table>`;
  for (const tr of $("tab-sales").querySelectorAll("tr[data-id]")) {
    tr.onclick = () => showSale(docs.find((d) => d.id === tr.dataset.id), party);
  }
  for (const tr of $("tab-sales").querySelectorAll("tr[data-customer]")) tr.onclick = () => showStatement("customer", tr.dataset.customer, aging.as_of);
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
    ${d.issues.map((i) => `<div class="warning">${esc(i.code)}: ${esc(i.message)}</div>`).join("")}${journalButton(d)}
    <p><button class="secondary" onclick="openPdf('${d.type === "credit_note" ? "credit-notes" : "sales-invoices"}/${d.id}/pdf')">Print PDF</button>
      ${canEdit() ? `<button class="secondary" onclick="editSale('${d.type}', '${d.id}')">Edit</button>` : ""}</p>`;
  $("journal-dialog").showModal();
}

// The PDF needs the bearer token, so it is fetched and opened as a blob.
async function openPdf(path) {
  const response = await fetch(`${ledgerUrl()}/${path}`, { headers: { Authorization: `Bearer ${state.token}` } });
  if (!response.ok) return showError(new Error(`PDF failed: ${response.status}`));
  window.open(URL.createObjectURL(await response.blob()), "_blank");
}

async function renderBank() {
  const [banks, payments, receipts, parties] = await Promise.all([
    api(`${ledgerUrl()}/bank-accounts`), api(`${ledgerUrl()}/payments`), api(`${ledgerUrl()}/receipts`), api(`${ledgerUrl()}/parties`),
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
  const docs = [...payments, ...receipts].filter((d) => d.bank_account_id === (bank.account_id ?? bank.id)).sort((a, b) => a.date.localeCompare(b.date));
  $("tab-bank").innerHTML = `
    <p><label>Account <select id="bank-pick">${banks.map((b) => `<option value="${b.account_id ?? b.id}" ${b === bank ? "selected" : ""}>${esc(b.code)} ${esc(b.name)} · ${esc(b.bank_name)}</option>`).join("")}</select></label></p>
    <h3>Payments and receipts</h3>
    <table><tr><th>No.</th><th>Date</th><th>Party</th><th>Reference</th><th>Lines</th><th class="num">In</th><th class="num">Out</th><th>Status</th></tr>
    ${docs.map((d) => `<tr class="click" data-pay="${d.id}"><td>${d.type === "receipt" ? "RC" : "PM"} ${d.payment_no}</td><td>${d.date}</td><td>${esc(party(d.party_id))}</td><td>${esc(d.reference ?? "")}</td>
      <td>${d.lines.map((l) => `${l.kind === "account" ? esc(accountName(l.account_id)) : l.kind === "invoice" ? "invoice" : "party balance"} ${l.amount}`).join("; ")}</td>
      <td class="num">${d.type === "receipt" ? d.total : ""}</td><td class="num">${d.type === "payment" ? d.total : ""}</td>
      <td>${d.status === "posted" ? `<span class="pill posted">posted</span>` : `<span class="pill draft">${d.status}</span> ${d.issues.map((i) => esc(i.message)).join(" ")}`}</td></tr>`).join("")}</table>
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
    <p><label>Month <select id="tax-period">${months.map((m) => `<option ${m === period ? "selected" : ""}>${m}</option>`).join("")}</select></label>
      <a class="button" href="#" id="tax-xlsx">Excel</a>
      <span class="muted">${esc(r.return_name)} · definition v${r.definition_version} · ${r.currency} / ${r.tax_currency ?? ""}
      ${r.tax_currency_basis === "override" ? `at ${r.override_rate}` : "from document snapshots"}</span></p>
    <p>${r.filed ? `<span class="pill posted">filed ${r.filed.filed_at.slice(0, 10)}</span> ${r.filed.differences.length ? `<span class="error">${r.filed.differences.length} boxes differ from the filed return</span>` : ""}` : `<span class="pill draft">not filed</span>`}</p>
    ${r.issues.map((i) => `<div class="warning">${esc(i.code)}: ${esc(i.message)}</div>`).join("")}
    ${sections.map((s) => `<h3>${esc(s)}</h3><table><tr><th>Box</th><th>Label</th><th class="num">${r.currency}</th><th class="num">${r.tax_currency ?? ""}</th></tr>
      ${r.boxes.filter((b) => b.section === s).map((b) => `<tr class="${b.lines ? "click" : "total"}" data-box="${b.key}"><td>${b.key}</td><td>${esc(b.label)}${b.information ? " <span class='muted'>(information)</span>" : ""}</td>
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
    ${d.issues.map((i) => `<div class="warning">${esc(i.code)}: ${esc(i.message)}</div>`).join("")}${journalButton(d)}`;
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
