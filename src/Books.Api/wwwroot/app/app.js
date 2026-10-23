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
  $("tb-out").innerHTML = "";
  $("gl-out").innerHTML = "";
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

start();
