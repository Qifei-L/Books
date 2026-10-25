// Payroll (read only): the pay run list is a document list in list.js; this file holds the inspector under it,
// with the employees a page at a time and search, since a pay run may have thousands. Entry is through the API
// and MCP (AI drafts, people post).

const PAGE = 100;
const EMPLOYEE_PAGE = 500;

async function allPayRunLines(run) {
  const lines = [];
  let offset = 0;
  while (true) {
    const page = await api(`${ledgerUrl()}/pay-runs/${run.id}/lines?offset=${offset}&limit=${EMPLOYEE_PAGE}`);
    lines.push(...page.lines);
    offset += page.lines.length;
    if (!page.lines.length || offset >= page.total) return lines;
  }
}

// Employees are not a separate master record: they are the people in the imported pay runs. One row per
// employee code, with the latest name/resident status and the latest pay figures; the detail is the history.
async function loadEmployees() {
  const runs = (await api(`${ledgerUrl()}/pay-runs`)).filter((r) => r.status !== "deleted");
  const groups = await Promise.all(runs.map(async (run) => ({ run, lines: await allPayRunLines(run) })));
  const employees = new Map();
  for (const { run, lines } of groups) {
    for (const line of lines) {
      const key = line.employee_code || line.name;
      let employee = employees.get(key);
      if (!employee) {
        employee = { code: line.employee_code, name: line.name, resident: line.resident, runs: [] };
        employees.set(key, employee);
      }
      employee.runs.push({
        name: line.name, resident: line.resident, period: run.period, date: run.date, currency: run.currency,
        gross: line.gross, tax: line.salary_tax, nssfEmployee: line.nssf_employee, nssfEmployer: line.nssf_employer, net: line.net,
      });
    }
  }
  return [...employees.values()].map((e) => {
    e.runs.sort((a, b) => b.date.localeCompare(a.date));
    e.last = e.runs[0];
    e.name = e.last.name || e.name;
    e.resident = e.last.resident;
    return e;
  }).sort((a, b) => a.name.localeCompare(b.name) || String(a.code).localeCompare(String(b.code)));
}

async function renderEmployees() {
  const box = $("tab-payroll-employees");
  try {
    box.innerHTML = `<p class="muted small">Loading employees from the imported pay runs…</p>`;
    const employees = await loadEmployees();
    if (!employees.length) {
      box.innerHTML = `<p class="muted">No employees yet. A pay run imported from the payroll system supplies them; there is no separate employee record to enter here.</p>`;
      return;
    }
    const row = (e, i) => {
      const search = `${e.name} ${e.code}`.toLowerCase();
      return `<tr class="click" data-employee="${i}" data-search="${esc(search)}">
        <td><div class="cell-main"><span class="cell-clip" title="${esc(e.name)}">${esc(e.name)}</span></div><div class="cell-sub">${esc(e.code ?? "")}</div></td>
        <td>${e.resident ? "yes" : "no"}</td>
        <td class="num">${e.runs.length}</td>
        <td>${e.last.period}</td>
        <td class="num">${money(Number(e.last.gross), e.last.currency)}</td>
        <td class="num">${money(Number(e.last.net), e.last.currency)}</td>
        <td>${e.last.currency}</td></tr>`;
    };
    box.innerHTML = `
      <p class="muted small">Employees are the people in the imported pay runs; a separate employee master record is not kept here. Search by code or name, or open a row for the pay history.</p>
      <form class="filter-bar" id="employee-filter" autocomplete="off">
        <label class="search">Search <input id="employee-search" placeholder="code or name"></label>
      </form>
      <div class="list-meta"><span id="employee-count">${employees.length} ${employees.length === 1 ? "employee" : "employees"}</span><span class="muted small">Click a row for the pay history</span></div>
      <div class="table-scroll"><table class="list employees"><tr><th>Employee</th><th>Resident</th><th class="num">Pay runs</th><th>Last period</th><th class="num">Last gross</th><th class="num">Last net</th><th>Currency</th></tr>
        ${employees.map(row).join("")}</table></div>`;
    const search = $("employee-search");
    search.form.onsubmit = (event) => event.preventDefault();
    search.oninput = () => {
      const needle = search.value.trim().toLowerCase();
      let shown = 0;
      for (const tr of box.querySelectorAll("tr[data-employee]")) {
        const match = !needle || tr.dataset.search.includes(needle);
        tr.hidden = !match;
        if (match) shown++;
      }
      $("employee-count").textContent = needle ? `${shown} of ${employees.length} employees` : `${employees.length} ${employees.length === 1 ? "employee" : "employees"}`;
    };
    for (const tr of box.querySelectorAll("tr[data-employee]")) tr.onclick = () => showEmployee(employees[Number(tr.dataset.employee)]);
  } catch (error) {
    box.innerHTML = `<p class="error">${esc(error.message)}</p>`;
  }
}

function showEmployee(e) {
  $("journal-body").innerHTML = `<h3>${esc(e.name)} <span class="muted small">${esc(e.code ?? "")}</span></h3>
    <p class="muted">${e.resident ? "Resident" : "Non-resident"} · ${e.runs.length} ${e.runs.length === 1 ? "pay run" : "pay runs"}</p>
    <table><tr><th>Period</th><th>Date</th><th>Currency</th><th class="num">Gross</th><th class="num">Tax on salary</th><th class="num">NSSF</th><th class="num">Net</th></tr>
    ${e.runs.map((r) => `<tr><td>${r.period}</td><td>${r.date}</td><td>${r.currency}</td>
      <td class="num">${money(Number(r.gross), r.currency)}</td><td class="num">${money(Number(r.tax), r.currency)}</td>
      <td class="num">${money(Number(r.nssfEmployee) + Number(r.nssfEmployer), r.currency)}</td><td class="num">${money(Number(r.net), r.currency)}</td></tr>`).join("")}</table>`;
  showDialog();
}

function showPayRun(r) {
  const t = r.totals;
  const c = r.currency;
  const m = (v) => money(Number(v), c);
  const figure = (label, value) => `<div class="figure"><span class="muted small">${label}</span><b>${value}</b></div>`;
  const accounts = [["Salary expense", r.accounts.salary_expense], ["NSSF expense", r.accounts.nssf_expense], ["Salaries payable", r.accounts.salaries_payable],
    ["Tax on salary payable", r.accounts.salary_tax_payable], ["NSSF payable", r.accounts.nssf_payable], ["Other deductions", r.accounts.other_deductions]]
    .filter(([, id]) => id);
  $("payroll-run").innerHTML = `
    <h3 style="margin-top:20px">Pay run ${r.pay_run_no} · ${r.period} <span class="pill ${r.status}">${r.status}</span></h3>
    <p class="muted">${r.date} · ${esc(r.reference ?? "")} ${esc(r.description ?? "")} · amounts in ${c}${r.rate ? `, posted at ${r.rate} ${c} per ${state.ledger.base_currency}` : ""}
      ${r.journal_id ? ` · <a href="#" onclick="event.preventDefault(); openJournal('${r.journal_id}')">journal No. ${r.journal_no}</a>` : ""}</p>
    <div class="figures">
      ${figure("Employees", t.employees.toLocaleString())}${figure("Gross", m(t.gross))}${figure("Fringe benefits", m(t.fringe_benefits))}
      ${figure("Tax on salary", m(t.salary_tax))}${figure("Fringe benefit tax", m(t.fringe_benefit_tax))}${figure("NSSF employee", m(t.nssf_employee))}
      ${figure("NSSF employer", m(t.nssf_employer))}${figure("Other deductions", m(t.other_deductions))}${figure("Net pay", m(t.net))}
    </div>
    <p class="muted small">Accounts: ${accounts.map(([label, id]) => `${label} ${esc(accountName(id))}`).join(" · ")}</p>
    <form class="inline" id="payroll-search"><label>Employee <input name="search" placeholder="code or name"></label><button type="submit">Search</button></form>
    <div id="payroll-lines"></div>`;
  $("payroll-search").onsubmit = (e) => { e.preventDefault(); payRunLines(r, e.target.search.value, 0); };
  payRunLines(r, "", 0);
  $("payroll-run").scrollIntoView({ behavior: "smooth" });
}

async function payRunLines(r, search, offset) {
  try {
    const page = await api(`${ledgerUrl()}/pay-runs/${r.id}/lines?offset=${offset}&limit=${PAGE}${search ? `&search=${encodeURIComponent(search)}` : ""}`);
    const m = (v) => money(Number(v), page.currency);
    const last = Math.min(offset + page.lines.length, page.total);
    $("payroll-lines").innerHTML = `
      <table><tr><th>No.</th><th>Code</th><th>Employee</th><th>Resident</th><th class="num">Gross</th><th class="num">Fringe</th><th class="num">Tax on salary</th>
        <th class="num">FBT</th><th class="num">NSSF employee</th><th class="num">NSSF employer</th><th class="num">Other</th><th class="num">Net</th></tr>
      ${page.lines.map((l) => `<tr><td>${l.line_no}</td><td>${esc(l.employee_code)}</td><td>${esc(l.name)}</td><td>${l.resident ? "yes" : "no"}</td>
        <td class="num">${m(l.gross)}</td><td class="num">${m(l.fringe_benefits)}</td><td class="num">${m(l.salary_tax)}</td><td class="num">${m(l.fringe_benefit_tax)}</td>
        <td class="num">${m(l.nssf_employee)}</td><td class="num">${m(l.nssf_employer)}</td><td class="num">${m(l.other_deductions)}</td><td class="num">${m(l.net)}</td></tr>`).join("")
        || `<tr><td colspan="12" class="muted">No employees match.</td></tr>`}</table>
      <p class="pager">${page.total ? `${offset + 1}–${last} of ${page.total.toLocaleString()}` : ""}
        <button class="secondary small" data-page="prev" ${offset === 0 ? "disabled" : ""}>Previous</button>
        <button class="secondary small" data-page="next" ${last >= page.total ? "disabled" : ""}>Next</button></p>`;
    $("payroll-lines").querySelector("[data-page=prev]").onclick = () => payRunLines(r, search, Math.max(0, offset - PAGE));
    $("payroll-lines").querySelector("[data-page=next]").onclick = () => payRunLines(r, search, offset + PAGE);
  } catch (error) {
    $("payroll-lines").innerHTML = `<p class="error">${esc(error.message)}</p>`;
  }
}
