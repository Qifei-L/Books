// Payroll (read only): the pay run list is a document list in list.js; this file holds the inspector under it,
// with the employees a page at a time and search, since a pay run may have thousands. Entry is through the API
// and MCP (AI drafts, people post).

const PAGE = 100;

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
