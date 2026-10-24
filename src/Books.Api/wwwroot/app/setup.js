// Settings and fixed assets: customers and suppliers, tax codes, bank accounts, transfers, invoice settings,
// exchange rates, and the fixed asset register with depreciation. Administrator-only changes are offered to administrators.

const isAdmin = () => state.role === "admin";

async function renderSetup() {
  const [parties, taxCodes, banks, transfers, invoiceSettings, rates] = await Promise.all([
    api(`${ledgerUrl()}/parties`), api(`${ledgerUrl()}/tax-codes`), api(`${ledgerUrl()}/bank-accounts`), api(`${ledgerUrl()}/transfers`),
    api(`${ledgerUrl()}/invoice-settings`), api(`/api/v1/organizations/${state.org}/exchange-rates?from=${today().slice(0, 4)}-01-01&to=${today()}`),
  ]);
  const roles = (p) => [p.is_customer ? "customer" : null, p.is_supplier ? "supplier" : null].filter(Boolean).join(", ");
  const s = invoiceSettings;
  const shownTransfers = transfers.filter((t) => t.status !== "deleted");
  $("tab-setup").innerHTML = `
    <h3>Customers and suppliers ${canEdit() ? `<button class="secondary small" onclick="partyForm()">New</button>` : ""}</h3>
    <table><tr><th>Name</th><th>Role</th><th>Tax ID</th><th>Contact</th><th class="num">Terms (days)</th></tr>
    ${parties.map((p) => `<tr ${canEdit() ? `class="click" onclick="editParty('${p.id}')"` : ""}><td>${esc(p.name)}${p.active ? "" : ` <span class="pill">inactive</span>`}</td><td>${roles(p)}</td><td>${esc(p.tax_id)}</td><td>${esc(p.contact_name)}</td><td class="num">${p.payment_terms_days ?? ""}</td></tr>`).join("")}</table>

    <h3 style="margin-top:16px">Bank accounts ${canEdit() ? `<button class="secondary small" onclick="bankForm()">New</button>` : ""}</h3>
    <table><tr><th>Account</th><th>Bank</th><th>Number</th><th>Currency</th></tr>
    ${banks.map((b) => `<tr ${canEdit() ? `class="click" data-bank="${b.account_id}"` : ""}><td>${esc(b.code)} ${esc(b.name)}</td><td>${esc(b.bank_name)}</td><td>${esc(b.account_number)}</td><td>${b.currency}</td></tr>`).join("")}</table>

    <h3 style="margin-top:16px">Bank transfers ${canEdit() ? `<button class="secondary small" onclick="transferForm()">New</button>` : ""}</h3>
    <table><tr><th>No.</th><th>Date</th><th>From</th><th>To</th><th class="num">Amount</th><th class="num">Fee</th><th>Journal</th></tr>
    ${shownTransfers.map((t) => `<tr ${canEdit() ? `class="click" data-transfer="${t.id}"` : ""}><td>${t.transfer_no}</td><td>${t.date}</td><td>${esc(accountName(t.from_account_id))}</td><td>${esc(accountName(t.to_account_id))}</td>
      <td class="num">${t.amount}</td><td class="num">${t.fee_amount}</td>
      <td>${t.status === "posted" ? `posted, journal No. ${t.journal_no}` : `<span class="pill draft">${t.status}</span> ${t.issues.map((i) => esc(i.message)).join(" ")}`}</td></tr>`).join("")}</table>

    <h3 style="margin-top:16px">Tax codes ${isAdmin() ? `<button class="secondary small" onclick="taxCodeForm()">New</button>` : ""}</h3>
    <table><tr><th>Name</th><th>Kind</th><th class="num">Rate %</th><th>Account</th><th>Return category</th><th>Effective</th></tr>
    ${taxCodes.map((t) => `<tr ${isAdmin() ? `class="click" data-tax="${t.id}"` : ""}><td>${esc(t.name)}${t.active ? "" : ` <span class="pill">inactive</span>`}</td><td>${t.kind.replaceAll("_", " ")}</td><td class="num">${t.rate}</td>
      <td>${t.account_id ? esc(accountName(t.account_id)) : ""}</td><td>${esc(t.return_category ?? "")}</td>
      <td>${t.effective_from ? `from ${t.effective_from}` : ""}${t.effective_to ? ` to ${t.effective_to}` : ""}</td></tr>`).join("")}</table>

    <h3 style="margin-top:16px">Invoice printing ${isAdmin() ? `<button class="secondary small" onclick="invoiceSettingsForm()">Edit</button>` : ""}</h3>
    <p>${esc(s.name_khmer ?? "")} ${esc(s.name_english ?? "(company name)")} · VATTIN ${esc(s.tax_id ?? "not set")} · numbers ${esc(s.invoice_prefix)}00001 and ${esc(s.credit_note_prefix)}00001</p>

    <h3 style="margin-top:16px">Exchange rates this year ${isAdmin() ? `<button class="secondary small" onclick="rateForm()">Set a rate</button>` : ""}</h3>
    <p class="muted small">Shared by every company in the organization.</p>
    <table><tr><th>Date</th><th>From</th><th>To</th><th class="num">Rate</th><th>Source</th></tr>
    ${rates.slice(-12).reverse().map((r) => `<tr><td>${r.date}</td><td>${r.from_currency}</td><td>${r.to_currency}</td><td class="num">${r.rate}</td><td>${esc(r.source)}</td></tr>`).join("")}</table>`;
  for (const tr of $("tab-setup").querySelectorAll("tr[data-bank]")) tr.onclick = () => bankForm(banks.find((b) => b.account_id === tr.dataset.bank));
  for (const tr of $("tab-setup").querySelectorAll("tr[data-transfer]")) tr.onclick = () => transferForm(shownTransfers.find((t) => t.id === tr.dataset.transfer));
  for (const tr of $("tab-setup").querySelectorAll("tr[data-tax]")) tr.onclick = () => taxCodeForm(taxCodes.find((t) => t.id === tr.dataset.tax));
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

async function bankForm(b) {
  const accounts = await api(`${ledgerUrl()}/accounts`);
  const candidates = accounts.filter((a) => a.type === "assets" && a.postable && !a.is_control && !a.is_bank && a.usages.length === 0);
  simpleForm(b ? `Edit ${b.code} ${b.name}` : "New bank account", `
    ${b ? "" : `<label class="wide">Account <select name="account_id">${options(candidates, null, (a) => `${a.code} ${a.name}`, "Choose an asset account")}</select></label>`}
    <label>Bank <input name="bank_name" value="${esc(b?.bank_name ?? "")}" required></label>
    <label>Account number <input name="account_number" value="${esc(b?.account_number ?? "")}"></label>`,
  async (f) => {
    if (!orNull(f.bank_name.value)) throw new Error("Bank is required.");
    const url = `${ledgerUrl()}/bank-accounts`;
    if (b) await write("PUT", `${url}/${b.account_id}`, { bank_name: f.bank_name.value.trim(), account_number: orNull(f.account_number.value) }, b.version);
    else {
      if (!f.account_id.value) throw new Error("Choose the account; create it in the chart first if it does not exist.");
      await write("POST", url, { account_id: f.account_id.value, bank_name: f.bank_name.value.trim(), account_number: orNull(f.account_number.value) });
    }
  }, `<p class="muted small">A bank account is an asset account in the chart, marked as a bank so payments, receipts and statements can use it.</p>`);
}

async function transferForm(t) {
  try {
    const [banks, accounts] = await Promise.all([api(`${ledgerUrl()}/bank-accounts`), api(`${ledgerUrl()}/accounts`)]);
    const bankRows = banks.map((b) => ({ ...b, id: b.account_id }));
    const expense = accounts.filter((a) => a.type === "cost_and_expense" && a.postable && a.active);
    const form = openForm(t ? `Edit transfer ${t.transfer_no}` : "New bank transfer", `
      <div class="grid">
        <label>Date <input type="date" name="date" value="${t?.date ?? today()}"></label>
        <label class="wide">From <select name="from_account_id">${options(bankRows, t?.from_account_id, (b) => `${b.code} ${b.name}`)}</select></label>
        <label class="wide">To <select name="to_account_id">${options(bankRows, t?.to_account_id ?? bankRows[1]?.id, (b) => `${b.code} ${b.name}`)}</select></label>
        <label>Amount <input name="amount" class="num" inputmode="decimal" value="${t?.amount ?? ""}"></label>
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
    const url = `${ledgerUrl()}/transfers`;
    onSave(form, async (post) => {
      const f = form.elements;
      const body = {
        date: f.date.value, from_account_id: f.from_account_id.value, to_account_id: f.to_account_id.value, amount: orNull(f.amount.value) ?? "",
        fee_amount: orNull(f.fee_amount.value), fee_account_id: orNull(f.fee_account_id.value), reference: orNull(f.reference.value), description: orNull(f.description.value), post,
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

const TAX_KINDS = ["vat_output", "vat_input", "vat_zero_rated", "non_vat", "withholding", "other_tax"];

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
    const [register, runs] = await Promise.all([api(`${ledgerUrl()}/fixed-assets?asOf=${asOf}`), api(`${ledgerUrl()}/depreciation-runs`)]);
    $("tab-assets").innerHTML = `
      ${canEdit() ? `<p><button onclick="assetForm()">New asset</button> <button class="secondary" onclick="depreciationForm()">Post depreciation</button></p>` : ""}
      <form class="inline" id="assets-form"><label>As of <input type="date" name="asOf" value="${asOf}"></label><button type="submit">Run</button></form>
      <p>${register.matches_control ? `<span class="ok">Equals the asset accounts</span>` : `<span class="error">Does not equal the asset accounts (cost ${register.asset_accounts_balance}, accumulated ${register.accumulated_accounts_balance})</span>`}</p>
      <table><tr><th>Code</th><th>Asset</th><th>Acquired</th><th class="num">Cost</th><th class="num">Months</th><th class="num">Monthly</th><th class="num">Accumulated</th><th class="num">Net book value</th><th>Status</th></tr>
      ${register.assets.map((a) => `<tr ${canEdit() ? `class="click" data-asset="${a.id}"` : ""}><td>${esc(a.code)}</td><td>${esc(a.name)}</td><td>${a.acquisition_date}</td><td class="num">${a.cost}</td>
        <td class="num">${a.useful_life_months}</td><td class="num">${a.monthly}</td><td class="num">${a.accumulated}</td><td class="num">${a.net_book_value}</td>
        <td>${a.disposed_on ? `disposed ${a.disposed_on}` : ""}</td></tr>`).join("")}
      <tr class="total"><td colspan="3">Total ${register.currency}</td><td class="num">${register.cost}</td><td></td><td></td><td class="num">${register.accumulated}</td><td class="num">${register.net_book_value}</td><td></td></tr></table>
      <h3 style="margin-top:16px">Depreciation runs</h3>
      <table><tr><th>Period</th><th class="num">Total</th><th>Status</th><th></th></tr>
      ${runs.filter((r) => r.status !== "deleted").map((r) => `<tr><td>${r.period}</td><td class="num">${r.total}</td><td><span class="pill ${r.status}">${r.status}</span></td>
        <td>${r.journal_id ? `<button class="secondary small" onclick="openJournal('${r.journal_id}')">Journal</button>` : ""}
        ${canEdit() ? `<button class="secondary small" onclick="deleteRun('${r.id}', '${r.period}')">Delete</button>` : ""}</td></tr>`).join("")}</table>`;
    $("assets-form").onsubmit = (e) => { e.preventDefault(); renderAssets(e.target.asOf.value); };
    for (const tr of $("tab-assets").querySelectorAll("tr[data-asset]")) tr.onclick = () => assetForm(register.assets.find((a) => a.id === tr.dataset.asset));
  } catch (error) {
    $("tab-assets").innerHTML = `<p class="muted">${esc(error.message)}</p>`;
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

async function deleteRun(id, period) {
  if (!confirm(`Delete the depreciation run for ${period}? Its journal is removed too.`)) return;
  try {
    await write("DELETE", `${ledgerUrl()}/depreciation-runs/${id}`);
    await refreshLedger();
  } catch (error) {
    showError(error);
  }
}
