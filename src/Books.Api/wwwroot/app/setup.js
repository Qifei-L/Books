// Settings pages, the fixed asset register and the tax code form. Administrator-only changes are offered
// to administrators only.

const isAdmin = () => state.role === "admin";

// Ledger facts and the year-end work: nothing here is a document.
async function renderSettingsLedger() {
  const revaluations = await api(`${ledgerUrl()}/fx-revaluations`);
  const months = ["", "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];
  const l = state.ledger;
  $("tab-settings-ledger").innerHTML = `
    <dl class="facts">
      <dt>Base currency</dt><dd>${esc(l.base_currency)}${l.first_posted_at ? ` <span class="muted small">fixed since the first posting on ${l.first_posted_at.slice(0, 10)}</span>` : ""}</dd>
      <dt>Fiscal year starts</dt><dd>${months[l.fy_start_month]} (periods are calendar months)</dd>
      <dt>Closed through</dt><dd>${l.closed_through ? `${l.closed_through} <span class="muted small">inclusive; drafts dated in this period block filing</span>` : `not closed`}</dd>
      <dt>Tax pack</dt><dd>${esc(l.tax_pack ?? "none applied")}</dd>
    </dl>
    <h3>Year-end exchange revaluation ${canEdit() ? `<button class="secondary small" onclick="revaluationForm()">Revalue…</button>` : ""}</h3>
    <p class="muted small">Restates bank accounts and open invoices in another currency at the closing rate; the unrealized difference reverses the next day. Revalue before closing the year.</p>
    ${revaluations.filter((r) => r.status !== "deleted").length ? `<table><tr><th>No.</th><th>Date</th><th>Rates</th><th class="num">Unrealized</th><th>Status</th><th></th></tr>
      ${revaluations.filter((r) => r.status !== "deleted").map((r) => `<tr><td>FXR ${r.revaluation_no}</td><td>${r.date}</td>
        <td>${Object.entries(r.rates).map(([c, v]) => `${c} ${v}`).join(", ")}</td><td class="num">${r.total}</td><td><span class="pill ${r.status}">${r.status}</span></td>
        <td><button class="secondary small" onclick="openJournal('${r.journal_id}')">Journal</button>
          ${canEdit() ? `<button class="secondary small" onclick="deleteRevaluation('${r.id}', '${r.version}', '${r.date}')">Delete</button>` : ""}</td></tr>`).join("")}</table>`
      : `<p class="muted small">No revaluation posted for this company.</p>`}`;
}


async function renderSettingsOrg() {
  const [org, companies, rates] = await Promise.all([
    api(`/api/v1/organizations/${state.org}`),
    api(`/api/v1/organizations/${state.org}/companies`),
    api(`/api/v1/organizations/${state.org}/exchange-rates?from=${today().slice(0, 4)}-01-01&to=${today()}`),
  ]);
  $("tab-settings-org").innerHTML = `
    <dl class="facts">
      <dt>Organization</dt><dd>${esc(org.name)}</dd>
      <dt>Your role</dt><dd>${esc(state.role ?? "")}</dd>
    </dl>
    <h3>Companies</h3>
    <p class="muted small">One ledger per company. The company is chosen in the bar above.</p>
    <table><tr><th>Company</th><th>Created</th></tr>
    ${companies.map((c) => `<tr><td>${esc(c.name)}${c.id === state.ledger.company_id ? ` <span class="pill posted">current</span>` : ""}</td><td>${String(c.created_at ?? "").slice(0, 10)}</td></tr>`).join("")}</table>
    <h3 style="margin-top:16px">Exchange rates this year ${isAdmin() ? `<button class="secondary small" onclick="rateForm()">Set a rate</button>` : ""}</h3>
    <p class="muted small">Shared by every company in the organization. Entered by hand; the Cambodia pack reads the rate of a document's date.</p>
    ${rates.length ? `<table><tr><th>Date</th><th>From</th><th>To</th><th class="num">Rate</th><th>Source</th></tr>
      ${rates.slice(-12).reverse().map((r) => `<tr><td>${r.date}</td><td>${r.from_currency}</td><td>${r.to_currency}</td><td class="num">${r.rate}</td><td>${esc(r.source)}</td></tr>`).join("")}</table>`
      : `<p class="muted small">No rates entered this year.</p>`}`;
}
/** A small form of labelled fields; save(values) runs on submit and the tab refreshes afterwards. */
function simpleForm(title, fields, save, extra = "") {
  const form = openForm(title, `<div class="grid">${fields}</div>${extra}
    <p class="actions"><span class="spacer"></span><button type="button" class="secondary" data-cancel>Cancel</button><button type="submit">Save</button></p>`);
  form.onsubmit = (event) => {
    event.preventDefault();
    busy(form, async () => {
      await save(form.elements);
      $("form-dialog").close();
      await refreshLedger();
    });
  };
  return form;
}

const orNull = (v) => (v.trim() === "" ? null : v.trim());

async function transferForm(t) {
  try {
    const [banks, accounts] = await Promise.all([api(`${ledgerUrl()}/bank-accounts`), api(`${ledgerUrl()}/accounts`)]);
    const bankRows = banks.map((b) => ({ ...b, id: b.account_id }));
    const expense = accounts.filter((a) => a.type === "cost_and_expense" && a.postable && a.active);
    const form = openForm(t ? `Edit transfer ${t.transfer_no}` : "New bank transfer", `
      <div class="grid">
        <label>Date <input type="date" name="date" value="${t?.date ?? today()}"></label>
        <label class="wide">From <select name="from_account_id">${options(bankRows, t?.from_account_id, (b) => `${b.code} ${b.name} · ${b.currency}`)}</select></label>
        <label class="wide">To <select name="to_account_id">${options(bankRows, t?.to_account_id ?? bankRows[1]?.id, (b) => `${b.code} ${b.name} · ${b.currency}`)}</select></label>
        <label>Amount sent <input name="amount" class="num" inputmode="decimal" value="${t?.amount ?? ""}"></label>
        <label data-fx>Amount received <input name="to_amount" class="num" inputmode="decimal" value="${t?.to_amount ?? ""}"></label>
        <label data-fx>Rate of sending account <input name="from_rate" class="num" inputmode="decimal" value="${t?.from_rate ?? ""}" placeholder="rate of the date"></label>
        <label>Bank fee <input name="fee_amount" class="num" inputmode="decimal" value="${t && t.fee_amount !== "0.00" ? t.fee_amount : ""}"></label>
        <label class="wide">Fee account <select name="fee_account_id">${options(expense, t?.fee_account_id, (a) => `${a.code} ${a.name}`, "None")}</select></label>
        <label>Reference <input name="reference" value="${esc(t?.reference ?? "")}"></label>
        <label class="wide">Description <input name="description" value="${esc(t?.description ?? "")}"></label>
      </div>
      <p class="actions">
        ${t ? `<button type="button" class="danger" data-delete>Delete</button>` : ""}
        <span class="spacer"></span>
        <button type="button" class="secondary" data-cancel>Cancel</button>
        <button type="submit" class="secondary" data-post="false">Save draft</button>
        <button type="submit" data-post="true">Save and post</button>
      </p>`);
    // Between currencies: what arrives is entered; the sending side converts at its rate (M12.2).
    const currencyOf = (id) => banks.find((b) => b.account_id === id)?.currency;
    const showFx = () => {
      const between = currencyOf(form.elements.from_account_id.value) !== currencyOf(form.elements.to_account_id.value);
      for (const l of form.querySelectorAll("[data-fx]")) l.classList.toggle("hidden", !between);
    };
    form.elements.from_account_id.onchange = showFx;
    form.elements.to_account_id.onchange = showFx;
    showFx();
    const url = `${ledgerUrl()}/transfers`;
    onSave(form, async (post) => {
      const f = form.elements;
      const between = currencyOf(f.from_account_id.value) !== currencyOf(f.to_account_id.value);
      const body = {
        date: f.date.value, from_account_id: f.from_account_id.value, to_account_id: f.to_account_id.value, amount: orNull(f.amount.value) ?? "",
        fee_amount: orNull(f.fee_amount.value), fee_account_id: orNull(f.fee_account_id.value), reference: orNull(f.reference.value), description: orNull(f.description.value), post,
        ...(between ? { to_amount: orNull(f.to_amount.value), from_rate: currencyOf(f.from_account_id.value) === state.ledger.base_currency ? null : orNull(f.from_rate.value) } : {}),
      };
      await (t ? write("PUT", `${url}/${t.id}`, body, t.version) : write("POST", url, body));
      $("form-dialog").close();
      await refreshLedger();
    });
    onDelete(form, t, `transfer ${t?.transfer_no}`, `${url}/${t?.id}`, t?.status === "posted" ? " Its journal is removed too." : "");
  } catch (error) {
    showError(error);
  }
}

const TAX_KINDS = ["vat_output", "vat_input", "vat_input_non_deductible", "vat_zero_rated", "non_vat", "withholding", "other_tax", "state_charge"];

async function taxCodeForm(t) {
  const [accounts, pack] = await Promise.all([api(`${ledgerUrl()}/accounts`), api(`${ledgerUrl()}/tax-pack`).catch(() => null)]);
  const postable = accounts.filter((a) => a.postable && a.active);
  const categories = [...(pack?.withholding_categories ?? []), ...(pack?.tax_categories ?? [])];
  simpleForm(t ? `Edit ${t.name}` : "New tax code", `
    <label class="wide">Name <input name="name" value="${esc(t?.name ?? "")}" required></label>
    <label>Kind <select name="kind" ${t ? "disabled" : ""}>${TAX_KINDS.map((k) => `<option value="${k}" ${k === t?.kind ? "selected" : ""}>${k.replaceAll("_", " ")}</option>`).join("")}</select></label>
    <label>Rate % <input name="rate" class="num" inputmode="decimal" value="${t?.rate ?? ""}" required></label>
    <label class="wide">Account <select name="account_id">${options(postable, t?.account_id, (a) => `${a.code} ${a.name}`, "None")}</select></label>
    <label class="wide">Non-deductible VAT account <select name="non_deductible_account_id">${options(postable, t?.non_deductible_account_id, (a) => `${a.code} ${a.name}`, "None")}</select></label>
    <label class="wide">Return category <select name="return_category">${options(categories.map((c) => ({ ...c, id: c.key })), t?.return_category, (c) => `${c.key}${c.label ? ` · ${c.label}` : ""}`, "None")}</select></label>
    <label>Effective from <input type="date" name="effective_from" value="${t?.effective_from ?? ""}"></label>
    <label>Effective to <input type="date" name="effective_to" value="${t?.effective_to ?? ""}"></label>
    ${t ? `<label class="check"><input type="checkbox" name="active" ${t.active ? "checked" : ""}> Active</label>` : ""}`,
  async (f) => {
    const body = {
      name: f.name.value.trim(), kind: t?.kind ?? f.kind.value, rate: f.rate.value.trim(), account_id: orNull(f.account_id.value),
      non_deductible_account_id: orNull(f.non_deductible_account_id.value), return_category: orNull(f.return_category.value),
      effective_from: orNull(f.effective_from.value), effective_to: orNull(f.effective_to.value), active: t ? f.active.checked : true,
    };
    const url = `${ledgerUrl()}/tax-codes`;
    await (t ? write("PUT", `${url}/${t.id}`, body, t.version) : write("POST", url, body));
  }, `<p class="muted small">Administrators only. A rate change for a period is a new code with effective dates, so earlier documents keep their rate.</p>`);
}

async function invoiceSettingsForm() {
  const s = await api(`${ledgerUrl()}/invoice-settings`);
  const v = (k) => esc(s[k] ?? "");
  simpleForm("Invoice printing", `
    <label class="wide">Name in Khmer <input name="name_khmer" value="${v("name_khmer")}"></label>
    <label class="wide">Name in English <input name="name_english" value="${v("name_english")}"></label>
    <label>VATTIN <input name="tax_id" value="${v("tax_id")}"></label>
    <label>Phone <input name="phone" value="${v("phone")}"></label>
    <label>Email <input name="email" value="${v("email")}"></label>
    <label class="wide">Address in Khmer <textarea name="address_khmer" rows="2">${v("address_khmer")}</textarea></label>
    <label class="wide">Address in English <textarea name="address_english" rows="2">${v("address_english")}</textarea></label>
    <label>Invoice prefix <input name="invoice_prefix" value="${v("invoice_prefix")}"></label>
    <label>Credit note prefix <input name="credit_note_prefix" value="${v("credit_note_prefix")}"></label>`,
  async (f) => {
    const body = Object.fromEntries(["name_khmer", "name_english", "tax_id", "phone", "email", "address_khmer", "address_english", "invoice_prefix", "credit_note_prefix"]
      .map((k) => [k, f[k].value]));
    // The version shown; before the first save it is 1 and not checked.
    await write("PUT", `${ledgerUrl()}/invoice-settings`, body, s.version);
  });
}

function rateForm() {
  simpleForm("Set an exchange rate", `
    <label>Date <input type="date" name="date" value="${today()}"></label>
    <label>From <input name="from_currency" value="USD" maxlength="3"></label>
    <label>To <input name="to_currency" value="KHR" maxlength="3"></label>
    <label>Rate <input name="rate" class="num" inputmode="decimal" required></label>
    <label>Source <input name="source" value="NBC"></label>`,
  async (f) => {
    await write("PUT", `/api/v1/organizations/${state.org}/exchange-rates`, {
      date: f.date.value, from_currency: f.from_currency.value.trim().toUpperCase(), to_currency: f.to_currency.value.trim().toUpperCase(),
      rate: f.rate.value.trim(), source: orNull(f.source.value),
    });
  }, `<p class="muted small">Administrators only. Units of the second currency per one unit of the first, for example 4,100 KHR per USD.</p>`);
}

// ---- Fixed assets ----

async function renderAssets(asOf = state.assetsAsOf ?? today()) {
  state.assetsAsOf = asOf;
  try {
    const register = await api(`${ledgerUrl()}/fixed-assets?asOf=${asOf}`);
    $("tab-assets-register").innerHTML = `
      ${canEdit() ? `<p><button onclick="assetForm()">New asset</button></p>` : ""}
      <form class="inline" id="assets-form"><label>As of <input type="date" name="asOf" value="${asOf}"></label><button type="submit">Run</button></form>
      <p>${register.matches_control ? `<span class="ok">Equals the asset accounts</span>` : `<span class="error">Does not equal the asset accounts (cost ${register.asset_accounts_balance}, accumulated ${register.accumulated_accounts_balance})</span>`}</p>
      <table><tr><th>Code</th><th>Asset</th><th>Acquired</th><th class="num">Cost</th><th class="num">Months</th><th class="num">Monthly</th><th class="num">Accumulated</th><th class="num">Net book value</th><th>Status</th></tr>
      ${register.assets.map((a) => `<tr ${canEdit() ? `class="click" data-asset="${a.id}"` : ""}><td>${esc(a.code)}</td><td>${esc(a.name)}</td><td>${a.acquisition_date}</td><td class="num">${a.cost}</td>
        <td class="num">${a.useful_life_months}</td><td class="num">${a.monthly}</td><td class="num">${a.accumulated}</td><td class="num">${a.net_book_value}</td>
        <td>${a.disposed_on ? `disposed ${a.disposed_on}` : ""}</td></tr>`).join("")}
      <tr class="total"><td colspan="3">Total ${register.currency}</td><td class="num">${register.cost}</td><td></td><td></td><td class="num">${register.accumulated}</td><td class="num">${register.net_book_value}</td><td></td></tr></table>
      <p class="muted small">Straight line by month, starting the month after acquisition. Depreciation runs are under Fixed assets → Depreciation runs.</p>`;
    $("assets-form").onsubmit = (e) => { e.preventDefault(); renderAssets(e.target.asOf.value); };
    for (const tr of $("tab-assets-register").querySelectorAll("tr[data-asset]")) tr.onclick = () => assetForm(register.assets.find((a) => a.id === tr.dataset.asset));
  } catch (error) {
    $("tab-assets-register").innerHTML = `<p class="muted">${esc(error.message)}</p>`;
  }
}

async function assetForm(a) {
  const accounts = await api(`${ledgerUrl()}/accounts`);
  const by = (usage) => accounts.filter((x) => x.usages.includes(usage));
  const form = simpleForm(a ? `Edit ${a.code} ${a.name}` : "New fixed asset", `
    <label>Code <input name="code" value="${esc(a?.code ?? "")}" required></label>
    <label class="wide">Name <input name="name" value="${esc(a?.name ?? "")}" required></label>
    <label>Acquired <input type="date" name="acquisition_date" value="${a?.acquisition_date ?? today()}"></label>
    <label>Cost <input name="cost" class="num" inputmode="decimal" value="${a?.cost ?? ""}" required></label>
    <label>Residual value <input name="residual_value" class="num" inputmode="decimal" value="${a && a.residual_value !== "0.00" ? a.residual_value : ""}"></label>
    <label>Useful life (months) <input name="useful_life_months" inputmode="numeric" value="${a?.useful_life_months ?? ""}" required></label>
    <label class="wide">Asset account <select name="asset_account_id">${options(by("fixed_asset"), a?.asset_account_id, (x) => `${x.code} ${x.name}`)}</select></label>
    <label class="wide">Accumulated depreciation <select name="accumulated_depreciation_account_id">${options(by("accumulated_depreciation"), a?.accumulated_depreciation_account_id, (x) => `${x.code} ${x.name}`)}</select></label>
    <label class="wide">Depreciation expense <select name="depreciation_expense_account_id">${options(by("depreciation_expense"), a?.depreciation_expense_account_id, (x) => `${x.code} ${x.name}`)}</select></label>`,
  async (f) => {
    const body = {
      code: f.code.value.trim(), name: f.name.value.trim(), acquisition_date: f.acquisition_date.value, cost: f.cost.value.trim(),
      residual_value: orNull(f.residual_value.value), useful_life_months: Number(f.useful_life_months.value),
      asset_account_id: orNull(f.asset_account_id.value), accumulated_depreciation_account_id: orNull(f.accumulated_depreciation_account_id.value),
      depreciation_expense_account_id: orNull(f.depreciation_expense_account_id.value),
    };
    const url = `${ledgerUrl()}/fixed-assets`;
    await (a ? write("PUT", `${url}/${a.id}`, body, a.version) : write("POST", url, body));
  }, `<p class="muted small">Straight line by month, starting the month after acquisition. The purchase itself is a purchase invoice or payment line on the asset account.</p>
    ${a && !a.disposed_on ? `<p><button type="button" class="danger" data-dispose>Dispose…</button></p>` : ""}`);
  form.querySelector("[data-dispose]")?.addEventListener("click", () => disposeForm(a, accounts));
}

function disposeForm(a, accounts) {
  const others = accounts.filter((x) => x.postable && x.active && !x.is_control && !x.is_bank);
  simpleForm(`Dispose of ${a.code} ${a.name}`, `
    <label>Date <input type="date" name="date" value="${today()}"></label>
    <label class="wide">Gain or loss account <select name="gain_or_loss_account_id">${options(others, null, (x) => `${x.code} ${x.name}`, "Choose")}</select></label>`,
  async (f) => {
    if (!f.gain_or_loss_account_id.value) throw new Error("Choose the gain or loss account.");
    await write("POST", `${ledgerUrl()}/fixed-assets/${a.id}/dispose`, { date: f.date.value, gain_or_loss_account_id: f.gain_or_loss_account_id.value }, a.version);
  }, `<p class="muted small">Removes the cost and accumulated depreciation; the net book value goes to the gain or loss account. Record any sale proceeds as a receipt.</p>`);
}

function depreciationForm() {
  const last = new Date();
  last.setDate(0);
  simpleForm("Post depreciation", `<label>Month <input type="month" name="period" value="${last.toISOString().slice(0, 7)}"></label>`,
    async (f) => { await write("POST", `${ledgerUrl()}/depreciation-runs`, { period: f.period.value }); },
    `<p class="muted small">Posts one journal for the month's depreciation of every asset, dated the month end.</p>`);
}

function showDepreciationRun(r) {
  $("journal-body").innerHTML = `<h3>Depreciation ${r.period} <span class="pill ${r.status}">${r.status}</span></h3>
    <p class="muted">${r.lines.length} ${r.lines.length === 1 ? "asset" : "assets"} · total ${r.total}</p>
    <table><tr><th>Asset</th><th>Code</th><th class="num">Amount</th></tr>
    ${r.lines.map((l) => `<tr><td>${esc(l.name)}</td><td>${esc(l.code)}</td><td class="num">${l.amount}</td></tr>`).join("")}
    <tr class="total"><td colspan="2">Total</td><td class="num">${r.total}</td></tr></table>
    ${r.journal_id ? journalButton(r) : ""}
    ${canEdit() ? `<p><button class="danger" onclick="deleteRun('${r.id}', '${r.period}')">Delete</button></p>` : ""}`;
  showDialog();
}

async function deleteRun(id, period) {
  if (!confirm(`Delete the depreciation run for ${period}? Its journal is removed too.`)) return;
  try {
    await write("DELETE", `${ledgerUrl()}/depreciation-runs/${id}`);
    if ($("journal-dialog").open) $("journal-dialog").close();
    await refreshLedger();
  } catch (error) {
    showError(error);
  }
}

// ---- Year-end exchange revaluation ----

async function revaluationForm() {
  // The end of the current fiscal year: the day before the next one starts.
  const year = Number(today().slice(0, 4));
  const fyStart = state.ledger.fy_start_month;
  const nextStartYear = Number(today().slice(5, 7)) >= fyStart ? year + 1 : year;
  const end = new Date(Date.UTC(nextStartYear, fyStart - 1, 0)).toISOString().slice(0, 10);
  const form = openForm("Year-end exchange revaluation", `
    <div class="grid">
      <label>Closing date <input type="date" name="date" value="${end}"></label>
      <label>KHR rate <input name="khr" class="num" inputmode="decimal" placeholder="rate table"></label>
    </div>
    <div id="revaluation-preview"><p class="muted">Loading…</p></div>
    <p class="actions"><span class="spacer"></span>
      <button type="button" class="secondary" data-cancel>Cancel</button>
      <button type="submit">Post revaluation and reversal</button>
    </p>`);
  // The preview is what would post; the server works it out again when posting.
  const preview = async () => {
    try {
      const f = form.elements;
      const p = await api(`${ledgerUrl()}/fx-revaluations/preview?date=${f.date.value}${orNull(f.khr.value) ? `&khrRate=${encodeURIComponent(f.khr.value.trim())}` : ""}`);
      $("revaluation-preview").innerHTML = p.lines.length === 0 ? `<p class="muted">Nothing to revalue on ${p.date}.</p>` : `
        <p class="muted small">Rates: ${Object.entries(p.rates).map(([c, v]) => `${c} ${v}`).join(", ")}</p>
        <table><tr><th>What</th><th class="num">Own currency</th><th class="num">Before</th><th class="num">After</th><th class="num">Difference</th></tr>
        ${p.lines.map((l) => `<tr><td>${esc(l.description ?? "")}</td><td class="num">${Number(l.foreign_amount).toLocaleString()} ${l.currency}</td>
          <td class="num">${l.before}</td><td class="num">${l.after}</td><td class="num">${l.adjustment}</td></tr>`).join("")}
        <tr class="total"><td colspan="4">Unrealized gain (+) or loss (−)</td><td class="num">${p.total}</td></tr></table>`;
    } catch (error) {
      $("revaluation-preview").innerHTML = `<p class="error">${esc(error.message)}</p>`;
    }
  };
  form.elements.date.onchange = preview;
  form.elements.khr.onchange = preview;
  await preview();
  form.onsubmit = (event) => {
    event.preventDefault();
    busy(form, async () => {
      const f = form.elements;
      await write("POST", `${ledgerUrl()}/fx-revaluations`, { date: f.date.value, ...(orNull(f.khr.value) ? { rates: { KHR: f.khr.value.trim() } } : {}) });
      $("form-dialog").close();
      await refreshLedger();
    });
  };
}

async function deleteRevaluation(id, version, date) {
  if (!confirm(`Delete the revaluation at ${date}? Its posting and reversal are removed.`)) return;
  try {
    await write("DELETE", `${ledgerUrl()}/fx-revaluations/${id}`, undefined, version);
    await refreshLedger();
  } catch (error) {
    showError(error);
  }
}