// Read-only preview of Books, for local testing only. Plain JavaScript, no build step.
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
  await renderPurchases();
  for (const id of ["tb-out", "gl-out", "pl-out", "bs-out"]) $(id).innerHTML = "";
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
}

function accountRow(a, purposeOf, indented) {
  const status = [a.active ? "active" : "inactive", a.used ? "used" : null].filter(Boolean).join(", ");
  return `<tr><td>${esc(a.code)}</td><td style="padding-left:${indented ? 24 : 8}px">${esc(a.name)}</td><td class="muted">${status}</td><td>${esc(purposeOf[a.id] ?? "")}</td></tr>`;
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
      ${tb.rows.map((r) => `<tr><td>${esc(r.code ?? "")}</td><td>${esc(r.name)}</td><td class="num">${blank(r.opening_debit)}</td><td class="num">${blank(r.opening_credit)}</td>
        <td class="num">${blank(r.period_debit)}</td><td class="num">${blank(r.period_credit)}</td><td class="num">${blank(r.closing_debit)}</td><td class="num">${blank(r.closing_credit)}</td></tr>`).join("")}
      <tr class="total"><td></td><td>Total</td><td class="num">${t.opening_debit}</td><td class="num">${t.opening_credit}</td><td class="num">${t.period_debit}</td>
        <td class="num">${t.period_credit}</td><td class="num">${t.closing_debit}</td><td class="num">${t.closing_credit}</td></tr></table>`;
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
      ${gl.lines.map((l) => `<tr><td>${l.date}</td><td>${l.journal_no}</td><td>${esc(l.reference)}</td><td>${esc(l.description)}</td>
        <td class="num">${blank(l.debit)}</td><td class="num">${blank(l.credit)}</td><td class="num">${l.balance}</td></tr>`).join("")}
      <tr class="total"><td colspan="4">Period totals / closing</td><td class="num">${gl.period_debit}</td><td class="num">${gl.period_credit}</td><td class="num">${gl.closing_balance}</td></tr></table>`;
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
    <h3>Parties</h3>
    <table><tr><th>Name</th><th>Role</th><th>Tax ID</th><th>Contact</th><th class="num">Terms (days)</th></tr>
    ${parties.map((p) => `<tr><td>${esc(p.name)}</td><td>${roles(p)}</td><td>${esc(p.tax_id)}</td><td>${esc(p.contact_name)}</td><td class="num">${p.payment_terms_days ?? ""}</td></tr>`).join("")}</table>
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
    ${docRows([...invoices, ...notes])}</table>
    <h3 style="margin-top:16px">AP aging at ${aging.as_of}</h3>
    <p>${aging.matches_control ? `<span class="ok">Equals the payable balance ${aging.control_balance}</span>` : `<span class="error">Does not equal the payable balance ${aging.control_balance}</span>`}</p>
    <table><tr><th>Supplier</th><th class="num">Not due</th><th class="num">1–30</th><th class="num">31–60</th><th class="num">61–90</th><th class="num">Over 90</th><th class="num">Total</th></tr>
    ${[...aging.rows, a].map((r) => `<tr class="${r === a ? "total" : ""}"><td>${esc(r.supplier)}</td><td class="num">${r.not_due}</td><td class="num">${r.days1_to30}</td>
      <td class="num">${r.days31_to60}</td><td class="num">${r.days61_to90}</td><td class="num">${r.over90}</td><td class="num">${r.total}</td></tr>`).join("")}</table>`;
  for (const tr of $("tab-purchases").querySelectorAll("tr[data-id]")) {
    tr.onclick = () => showPurchase([...invoices, ...notes].find((d) => d.id === tr.dataset.id), party);
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
    ${d.issues.map((i) => `<div class="warning">${esc(i.code)}: ${esc(i.message)}</div>`).join("")}`;
  $("journal-dialog").showModal();
}

// Statements: nested groups with a subtotal at every level.
function statementRows(lines, level) {
  return lines.map((line) => {
    const pad = `style="padding-left:${8 + level * 18}px"`;
    const name = esc(line.code ? `${line.code} ${line.name}` : line.name);
    if (line.kind !== "group") {
      return `<tr><td ${pad}>${line.kind === "calculated" ? `<em>${name}</em>` : name}</td><td class="num">${line.amount}</td></tr>`;
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
      <tr class="total"><td>Net profit</td><td class="num">${pl.net_profit}</td></tr></table>`;
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
      <table><tr><th>Account</th><th class="num">${bs.currency}</th></tr>${statementTable(bs.sections)}</table>`;
  } catch (error) {
    showError(error);
  }
});

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
