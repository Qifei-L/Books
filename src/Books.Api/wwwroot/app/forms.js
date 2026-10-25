// Data entry for people: forms that save through the same API, rules and audit as every other client.
// Every write sends a fresh Idempotency-Key, and changes to existing records send If-Match with the version shown.

const canEdit = () => state.ledger?.effective_access === "edit";
const today = () => new Date().toISOString().slice(0, 10);

/** Sends a write. Returns { status, data, version }; a document kept as a draft (422) is a result, not an error. */
async function write(method, path, body, version) {
  const headers = { "Content-Type": "application/json", Authorization: `Bearer ${state.token}`, "Idempotency-Key": crypto.randomUUID() };
  if (version) headers["If-Match"] = `"v${version}"`;
  const response = await fetch(path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  const json = response.status === 204 ? null : await response.json();
  if (response.ok || (response.status === 422 && json?.data)) return { status: response.status, data: json?.data };
  if (response.status === 401) signOut();
  if (response.status === 412) throw new Error("Someone else changed this record. Close the form and open it again.");
  const reasons = (json?.issues ?? []).map((i) => i.message).join(" ");
  throw new Error([json?.detail ?? json?.title ?? `HTTP ${response.status}`, reasons].filter(Boolean).join(" "));
}

/** Opens the form dialog with the given title and inner HTML; returns the form element. */
function openForm(title, html) {
  const dialog = $("form-dialog");
  dialog.innerHTML = `<form id="entry-form" novalidate><h3>${esc(title)}</h3>${html}<p id="form-error" class="error hidden"></p></form>`;
  dialog.showModal();
  const form = $("entry-form");
  form.querySelector("[data-cancel]")?.addEventListener("click", () => dialog.close());
  return form;
}

function formError(error) {
  $("form-error").textContent = error.message;
  $("form-error").classList.remove("hidden");
}

/** Runs a save with the buttons disabled, so a double click cannot send two writes. */
async function busy(form, action) {
  const buttons = [...form.querySelectorAll("button")];
  for (const b of buttons) b.disabled = true;
  try {
    await action();
  } catch (error) {
    formError(error);
  } finally {
    for (const b of buttons) b.disabled = false;
  }
}

const options = (rows, selected, label, empty) =>
  (empty === undefined ? "" : `<option value="">${esc(empty)}</option>`)
  + rows.map((r) => `<option value="${r.id}" ${r.id === selected ? "selected" : ""}>${esc(label(r))}</option>`).join("");

// Re-read the open page after a save. Other pages load again when they are opened, so a single
// document no longer pulls every list in the company.
async function refreshLedger() {
  state.rendered = {};
  state.docData = {};
  state.docSearchIndex = {};
  state.simpleData = {};
  state.simpleSearchIndex = {};
  state.simplePage = {};
  await showTab(state.tab ?? "sales-invoices", true);
}

// ---- Business parties (customers and suppliers) ----

function partyForm(p) {
  const v = (field) => esc(p?.[field] ?? "");
  const form = openForm(p ? `Edit ${p.name}` : "New business party", `
    <div class="grid">
      <label class="wide">Name <input name="name" value="${v("name")}" required></label>
      <label>VATTIN <input name="tax_id" value="${v("tax_id")}"></label>
      <label>Payment terms (days) <input name="payment_terms_days" inputmode="numeric" value="${p?.payment_terms_days ?? ""}"></label>
      <label class="check"><input type="checkbox" name="is_customer" ${!p || p.is_customer ? "checked" : ""}> Customer</label>
      <label class="check"><input type="checkbox" name="is_supplier" ${p?.is_supplier ? "checked" : ""}> Supplier</label>
      <label>Contact <input name="contact_name" value="${v("contact_name")}"></label>
      <label>Email <input name="email" type="email" value="${v("email")}"></label>
      <label>Phone <input name="phone" value="${v("phone")}"></label>
      <label class="wide">Address <textarea name="address" rows="2">${v("address")}</textarea></label>
      ${p ? `<label class="check"><input type="checkbox" name="active" ${p.active ? "checked" : ""}> Active</label>` : ""}
    </div>
    <p class="actions">
      ${p && !p.used ? `<button type="button" class="danger" data-delete>Delete</button>` : ""}
      <span class="spacer"></span>
      <button type="button" class="secondary" data-cancel>Cancel</button>
      <button type="submit">Save</button>
    </p>`);
  form.onsubmit = (event) => {
    event.preventDefault();
    busy(form, async () => {
      const f = form.elements;
      const terms = f.payment_terms_days.value.trim();
      const body = {
        name: f.name.value.trim(), tax_id: f.tax_id.value.trim(), is_customer: f.is_customer.checked, is_supplier: f.is_supplier.checked,
        contact_name: f.contact_name.value.trim(), email: f.email.value.trim(), phone: f.phone.value.trim(), address: f.address.value.trim(),
        ...(terms ? { payment_terms_days: Number(terms) } : {}), ...(p ? { active: f.active.checked } : {}),
      };
      if (!body.name) throw new Error("Name is required.");
      const url = `${ledgerUrl()}/parties`;
      await (p ? write("PATCH", `${url}/${p.id}`, body, p.version) : write("POST", url, body));
      $("form-dialog").close();
      await refreshLedger();
    });
  };
  form.querySelector("[data-delete]")?.addEventListener("click", () => busy(form, async () => {
    if (!confirm(`Delete ${p.name}?`)) return;
    await write("DELETE", `${ledgerUrl()}/parties/${p.id}`, undefined, p.version);
    $("form-dialog").close();
    await refreshLedger();
  }));
}

// ---- Line grid shared by documents, payments and journals ----

/**
 * A table of editable lines. Each column is { name, head, cell(line) → inner HTML, num? }. Returns helpers to add lines,
 * read them back and walk the rows; onChange runs after any edit, add or remove.
 */
function lineGrid(table, columns, onChange) {
  table.innerHTML = `<tr>${columns.map((c) => `<th class="${c.num ? "num" : ""}">${c.head}</th>`).join("")}<th></th></tr>`;
  const add = (line) => {
    const tr = table.insertRow();
    tr.dataset.adjusted = line?.tax_adjusted ? JSON.stringify({ tax_amount: line.tax_amount, tax_reason: line.tax_reason }) : "";
    tr.innerHTML = columns.map((c) => `<td>${c.cell(line)}</td>`).join("")
      + `<td><button type="button" class="link" title="Remove line" data-remove>×</button></td>`;
    tr.querySelector("[data-remove]").onclick = () => { tr.remove(); onChange(); };
    for (const c of columns) c.init?.(tr);
    return tr;
  };
  const rows = () => [...table.rows].slice(1);
  table.oninput = onChange;
  table.onchange = onChange;
  return { add, rows };
}

const field = (tr, name) => tr.querySelector(`[name=${name}]`);
const value = (tr, name) => {
  const el = field(tr, name);
  if (!el) return null;
  if (el.type === "checkbox") return el.checked;
  return el.value.trim() === "" ? null : el.value.trim();
};
const num = (v) => Number(v ?? 0) || 0;
// Amounts read the same everywhere: thousands separated, and as many decimals as the currency has.
// The API sends amounts as strings ("2100.00"), so parse before formatting.
const money = (x, currency = state.ledger?.base_currency) => {
  const n = Number(x);
  if (!Number.isFinite(n)) return String(x ?? "");
  const minor = currency === "KHR" ? 0 : 2;
  return n.toLocaleString("en-US", { minimumFractionDigits: minor, maximumFractionDigits: minor });
};
const rateOf = (id, codes) => num(codes.find((t) => t.id === id)?.rate) / 100;
const partyNames = async () => {
  const parties = await api(`${ledgerUrl()}/parties`);
  return (id) => parties.find((p) => p.id === id)?.name ?? "";
};

// ---- Sales and purchase documents ----

const DOCS = {
  invoice: { side: "sale", path: "sales-invoices", name: "invoice" },
  credit_note: { side: "sale", path: "credit-notes", name: "credit note", note: true },
  purchase_invoice: { side: "purchase", path: "purchase-invoices", name: "purchase invoice" },
  debit_note: { side: "purchase", path: "debit-notes", name: "debit note", note: true },
};

/** What a sales or purchase form offers: parties on that side, accounts, items, tax codes, and invoices a note may correct. */
async function documentChoices(side) {
  const sale = side === "sale";
  const [parties, accounts, items, taxCodes, invoices] = await Promise.all([
    api(`${ledgerUrl()}/parties`), api(`${ledgerUrl()}/accounts`), api(`${ledgerUrl()}/items`), api(`${ledgerUrl()}/tax-codes`),
    api(`${ledgerUrl()}/${sale ? "sales-invoices" : "purchase-invoices"}`),
  ]);
  const usable = (a) => a.postable && a.active && !a.is_control && !a.is_bank;
  return {
    parties: parties.filter((p) => p.active && (sale ? p.is_customer : p.is_supplier)),
    accounts: accounts.filter((a) => usable(a) && (sale ? a.type === "income" : ["cost_and_expense", "assets"].includes(a.type))),
    items: items.filter((i) => i.active),
    vat: taxCodes.filter((t) => t.active && [sale ? "vat_output" : "vat_input", "vat_zero_rated", "non_vat"].includes(t.kind)),
    other: taxCodes.filter((t) => t.active && t.kind === "other_tax"),
    wht: taxCodes.filter((t) => t.active && t.kind === "withholding"),
    invoices: invoices.filter((i) => i.status === "posted" && i.type === "invoice"),
  };
}

/** New or existing sales invoice, credit note, purchase invoice or debit note. */
async function documentForm(kind, doc) {
  try {
    const spec = DOCS[kind];
    const sale = spec.side === "sale";
    const c = await documentChoices(spec.side);
    const partyField = sale ? "customer_id" : "supplier_id";
    const base = state.ledger.base_currency;
    const currency = doc?.currency || base;
    const form = openForm(doc ? `Edit ${spec.name} ${doc.invoice_no}` : `New ${spec.name}`, `
      <div class="grid">
        <label class="wide">${sale ? "Customer" : "Supplier"} <select name="party" required>${options(c.parties, doc?.[partyField], (p) => p.name, `Choose a ${sale ? "customer" : "supplier"}`)}</select></label>
        <label>Date <input type="date" name="date" value="${doc?.date ?? today()}" required></label>
        ${spec.note
          ? `<label>Corrects invoice <select name="original_invoice_id">${options(c.invoices, doc?.original_invoice_id, (i) => `${sale ? "SI" : "PI"} ${i.invoice_no} · ${i.date} · ${i.gross_total}`, "None")}</select></label>`
          : `<label>Due date <input type="date" name="due_date" value="${doc?.due_date ?? ""}" title="Leave empty for the payment terms"></label>`}
        <label>${sale ? "Reference" : "Supplier's invoice no."} <input name="reference" value="${esc(doc?.reference ?? "")}"></label>
        <label>Currency <select name="currency">${[...new Set([base, "USD", "KHR"])].map((x) => `<option ${x === currency ? "selected" : ""}>${x}</option>`).join("")}</select></label>
        <label data-rate>Exchange rate <input name="exchange_rate" inputmode="decimal" value="${doc?.exchange_rate ?? ""}" placeholder="${base} rate of the date"></label>
        <label class="wide">Description <input name="doc_description" value="${esc(doc?.description ?? "")}"></label>
      </div>
      <table class="lines"></table>
      <p><button type="button" class="secondary" data-add>Add line</button></p>
      <p id="form-totals" class="totals"></p>
      <p class="actions">
        ${doc ? `<button type="button" class="danger" data-delete>Delete</button>` : ""}
        <span class="spacer"></span>
        <button type="button" class="secondary" data-cancel>Cancel</button>
        <button type="submit" class="secondary" data-post="false">Save draft</button>
        <button type="submit" data-post="true">Save and post</button>
      </p>`);

    const columns = [
      { name: "item_id", head: "Item", cell: (l) => `<select name="item_id">${options(c.items, l?.item_id, (i) => i.name, "")}</select>`,
        init: (tr) => { field(tr, "item_id").onchange = () => fillFromItem(tr, c.items, spec.side, form.elements.currency.value === state.ledger.base_currency); } },
      { name: "account_id", head: "Account", cell: (l) => `<select name="account_id">${options(c.accounts, l?.account_id, (a) => `${a.code} ${a.name}`, "Choose")}</select>` },
      { name: "description", head: "Description", cell: (l) => `<input name="description" value="${esc(l?.description ?? "")}">` },
      { name: "quantity", head: "Qty", num: true, cell: (l) => `<input name="quantity" class="num" inputmode="decimal" value="${l?.quantity ?? ""}">` },
      { name: "unit", head: "Unit", cell: (l) => `<select name="unit">${unitOptions(c.items.find((i) => i.id === l?.item_id), l?.unit)}</select>` },
      { name: "unit_price", head: "Unit price", num: true, cell: (l) => `<input name="unit_price" class="num" inputmode="decimal" value="${l?.unit_price ?? ""}">` },
      { name: "amount", head: "Amount", num: true, cell: (l) => `<input name="amount" class="num" inputmode="decimal" value="${l && !l.quantity ? l.net_amount : ""}" placeholder="qty × price">` },
      { name: "tax_code_id", head: "VAT", cell: (l) => `<select name="tax_code_id">${options(c.vat, l?.tax_code_id, (t) => t.name, "None")}</select>` },
      ...(sale
        ? [{ name: "other_tax_code_id", head: "Other tax", cell: (l) => `<select name="other_tax_code_id">${options(c.other, l?.other_tax_code_id, (t) => t.name, "None")}</select>` }]
        : [
          { name: "valid_tax_invoice", head: "Tax invoice", cell: (l) => `<input type="checkbox" name="valid_tax_invoice" title="A valid tax invoice was received; without one the VAT is not deductible" ${l?.valid_tax_invoice === false ? "" : "checked"}>` },
          { name: "withholding_code_id", head: "WHT", cell: (l) => `<select name="withholding_code_id">${options(c.wht, l?.withholding_code_id, (t) => t.name, "None")}</select>` },
        ]),
    ];
    // An estimate for the person entering; the server calculates and rounds the figures that are saved.
    const totals = () => {
      let net = 0, other = 0, vat = 0, wht = 0;
      for (const tr of grid.rows()) {
        const amount = value(tr, "amount") !== null ? num(value(tr, "amount")) : num(value(tr, "quantity")) * num(value(tr, "unit_price"));
        const o = amount * rateOf(value(tr, "other_tax_code_id"), c.other);
        net += amount; other += o; vat += (amount + o) * rateOf(value(tr, "tax_code_id"), c.vat); wht += amount * rateOf(value(tr, "withholding_code_id"), c.wht);
      }
      const cur = form.elements.currency.value;
      $("form-totals").innerHTML = `Net ${money(net, cur)}${other ? ` · other tax ${money(other, cur)}` : ""} · VAT ${money(vat, cur)}
        ${wht ? ` · WHT −${money(wht, cur)}` : ""} · <b>${sale ? "total" : "payable"} ${money(net + other + vat - wht, cur)} ${cur}</b>
        <span class="muted small">estimate; saved figures are calculated by the server</span>`;
    };
    const grid = lineGrid(form.querySelector("table.lines"), columns, totals);
    const showRate = () => form.querySelector("[data-rate]").classList.toggle("hidden", form.elements.currency.value === base);
    form.elements.currency.onchange = () => { showRate(); totals(); };
    form.querySelector("[data-add]").onclick = () => grid.add();
    for (const l of doc?.lines ?? [null, null]) grid.add(l);
    showRate();
    totals();

    const url = `${ledgerUrl()}/${spec.path}`;
    onSave(form, async (post) => {
      const f = form.elements;
      const text = (v) => (v.trim() === "" ? null : v.trim());
      const lines = grid.rows().map((tr) => ({
        ...Object.fromEntries(columns.map((col) => [col.name, value(tr, col.name)])),
        ...(tr.dataset.adjusted ? JSON.parse(tr.dataset.adjusted) : {}),
      })).filter((l) => l.account_id || l.amount || l.unit_price || l.description);
      if (!f.party.value) throw new Error(`Choose a ${sale ? "customer" : "supplier"}.`);
      if (!lines.length) throw new Error("Add at least one line.");
      if (lines.some((l) => !l.account_id)) throw new Error("Every line needs an account.");
      const body = {
        [partyField]: f.party.value, date: f.date.value, reference: text(f.reference.value), description: text(f.doc_description.value), lines, post,
        ...(spec.note ? { original_invoice_id: text(f.original_invoice_id.value) } : { due_date: text(f.due_date.value) }),
        ...(f.currency.value !== base ? { currency: f.currency.value, exchange_rate: text(f.exchange_rate.value) } : {}),
      };
      const result = await (doc ? write("PUT", `${url}/${doc.id}`, body, doc.version) : write("POST", url, body));
      $("form-dialog").close();
      await refreshLedger();
      (sale ? showSale : showPurchase)(result.data, await partyNames());
    });
    onDelete(form, doc, `${spec.name} ${doc?.invoice_no}`, `${url}/${doc?.id}`, doc?.status === "posted" ? " Its journal is removed too." : "");
  } catch (error) {
    showError(error);
  }
}

/** Starts a line from the item: its account, and the sales or purchase tax code, text and price (prices are in the ledger's currency). */
/** The item's base unit (empty value) and its other units; the line's quantity and price are in the one chosen. */
function unitOptions(item, selected) {
  if (!item) return `<option value=""></option>`;
  return `<option value="">${esc(item.unit ?? "")}</option>`
    + (item.units ?? []).map((u) => `<option value="${esc(u.name)}" ${u.name === selected ? "selected" : ""}>${esc(u.name)} (${u.factor} ${esc(item.unit ?? "")})</option>`).join("");
}

function fillFromItem(tr, items, side, baseCurrency) {
  const item = items.find((x) => x.id === value(tr, "item_id"));
  if (field(tr, "unit")) field(tr, "unit").innerHTML = unitOptions(item);
  if (!item) return;
  const sale = side === "sale";
  const pick = (name, id) => { if (id && [...field(tr, name).options].some((o) => o.value === id)) field(tr, name).value = id; };
  pick("account_id", (sale ? item.income_account_id : item.inventory_account_id) ?? item.default_account_id);
  pick("tax_code_id", (sale ? item.sales_tax_code_id : item.purchase_tax_code_id) ?? item.default_tax_code_id);
  if (!value(tr, "description")) field(tr, "description").value = (sale ? item.sales_description : item.purchase_description) ?? item.name;
  const price = sale ? item.sales_price : item.purchase_price;
  if (price && baseCurrency && !value(tr, "unit_price") && !value(tr, "amount")) field(tr, "unit_price").value = price;
  // A price per base unit becomes a price per box when the unit changes.
  if (field(tr, "unit")) field(tr, "unit").onchange = () => {
    const factor = Number((item.units ?? []).find((u) => u.name === value(tr, "unit"))?.factor ?? 1);
    if (price && baseCurrency && !value(tr, "amount")) field(tr, "unit_price").value = String(Math.round(Number(price) * factor * 10000) / 10000);
  };
}

/** Opens the form for a document from its dialog: side is sale or purchase, type the document's own type. */
async function editDocument(side, type, id) {
  $("journal-dialog").close();
  const kind = side === "sale" ? type : type === "debit_note" ? "debit_note" : "purchase_invoice";
  const doc = (await api(`${ledgerUrl()}/${DOCS[kind].path}`)).find((d) => d.id === id);
  if (doc) documentForm(kind, doc);
}

/** Wires Save draft / Save and post: the clicked button decides whether the document posts. */
function onSave(form, save) {
  let post = true;
  for (const b of form.querySelectorAll("[data-post]")) b.onclick = () => { post = b.dataset.post === "true"; };
  form.onsubmit = (event) => {
    event.preventDefault();
    busy(form, () => save(post));
  };
}

function onDelete(form, record, label, url, warning = "") {
  form.querySelector("[data-delete]")?.addEventListener("click", () => busy(form, async () => {
    if (!confirm(`Delete ${label}?${warning}`)) return;
    await write("DELETE", url, undefined, record.version);
    $("form-dialog").close();
    await refreshLedger();
  }));
}

// ---- Payments and receipts (Manager.io style) ----

/**
 * A payment or receipt from one bank account. Each line either posts to an account (with VAT and WHT where they
 * apply) or settles with the party: leave the account empty, or choose the Payable or Receivable account, and
 * optionally name the invoice. Settlement without an invoice takes the party's oldest open items.
 */
async function paymentForm(type, doc) {
  try {
    const receipt = type === "receipt";
    const path = receipt ? "receipts" : "payments";
    const [banks, parties, accounts, taxCodes, sales, purchases] = await Promise.all([
      api(`${ledgerUrl()}/bank-accounts`), api(`${ledgerUrl()}/parties`), api(`${ledgerUrl()}/accounts`), api(`${ledgerUrl()}/tax-codes`),
      api(`${ledgerUrl()}/sales-invoices`), api(`${ledgerUrl()}/purchase-invoices`),
    ]);
    const vat = taxCodes.filter((t) => t.active && [receipt ? "vat_output" : "vat_input", "vat_zero_rated", "non_vat"].includes(t.kind));
    const wht = taxCodes.filter((t) => t.active && t.kind === "withholding");
    const lineAccounts = accounts.filter((a) => a.postable && a.active && !a.is_bank);
    const invoices = [...sales.map((i) => ({ ...i, label: `SI ${i.invoice_no}`, party: i.customer_id })), ...purchases.map((i) => ({ ...i, label: `PI ${i.invoice_no}`, party: i.supplier_id, gross_total: i.payable_total }))]
      .filter((i) => i.status === "posted" && i.type === "invoice");
    const name = receipt ? "receipt" : "payment";
    const form = openForm(doc ? `Edit ${name} ${doc.payment_no}` : `New ${name}`, `
      <div class="grid">
        <label class="wide">${receipt ? "Received into" : "Paid from"} <select name="bank_account_id">${options(banks.map((b) => ({ ...b, id: b.account_id })), doc?.bank_account_id, (b) => `${b.code} ${b.name} · ${b.currency}`)}</select></label>
        <label class="wide">${receipt ? "Received from" : "Paid to"} <select name="party_id">${options(parties.filter((p) => p.active), doc?.party_id, (p) => p.name, "No party")}</select></label>
        <label>Date <input type="date" name="date" value="${doc?.date ?? today()}" required></label>
        <label>Reference <input name="reference" value="${esc(doc?.reference ?? "")}"></label>
        <label data-rate>Exchange rate <input name="exchange_rate" inputmode="decimal" value="${doc?.exchange_rate ?? ""}" placeholder="rate of the date"></label>
        <label class="wide">Description <input name="doc_description" value="${esc(doc?.description ?? "")}"></label>
      </div>
      <p class="muted small">The category is what the money was for: leave it empty (or choose Accounts payable / receivable) to settle with the party, or choose an account for a direct payment or fee. Name an invoice to settle that one, otherwise the oldest open items are settled first.</p>
      <table class="lines"></table>
      <p><button type="button" class="secondary" data-add>Add line</button></p>
      <p id="form-totals" class="totals"></p>
      <p class="actions">
        ${doc ? `<button type="button" class="danger" data-delete>Delete</button>` : ""}
        <span class="spacer"></span>
        <button type="button" class="secondary" data-cancel>Cancel</button>
        <button type="submit" class="secondary" data-post="false">Save draft</button>
        <button type="submit" data-post="true">Save and post</button>
      </p>`);
    const invoiceOptions = (selected) => options(invoices.filter((i) => i.party === form.elements.party_id.value), selected, (i) => `${i.label} · ${i.date} · ${i.gross_total}`, "Oldest first");
    const columns = [
      { name: "account_id", head: "Category", cell: (l) => `<select name="account_id">${options(lineAccounts, l?.kind === "account" ? l.account_id : l?.account_id, (a) => `${a.code} ${a.name}`, "Settle with party")}</select>` },
      { name: "invoice_id", head: "Invoice", cell: (l) => `<select name="invoice_id">${invoiceOptions(l?.invoice_id)}</select>` },
      { name: "description", head: "Description", cell: (l) => `<input name="description" value="${esc(l?.description ?? "")}">` },
      { name: "amount", head: "Amount", num: true, cell: (l) => `<input name="amount" class="num" inputmode="decimal" value="${l?.amount ?? ""}">` },
      { name: "settled_amount", head: "Settles (invoice currency)", num: true, cell: (l) => `<input name="settled_amount" class="num" inputmode="decimal" value="${l?.settled_amount ?? ""}" title="Only for an invoice in another currency than the bank's; leave empty to convert at the day's rate">` },
      { name: "tax_code_id", head: "VAT", cell: (l) => `<select name="tax_code_id">${options(vat, l?.tax_code_id, (t) => t.name, "None")}</select>` },
      ...(receipt ? [] : [{ name: "valid_tax_invoice", head: "Tax invoice", cell: (l) => `<input type="checkbox" name="valid_tax_invoice" ${l?.valid_tax_invoice === false ? "" : "checked"}>` }]),
      { name: "withholding_code_id", head: "WHT", cell: (l) => `<select name="withholding_code_id">${options(wht, l?.withholding_code_id, (t) => t.name, "None")}</select>` },
    ];
    const totals = () => {
      let total = 0;
      for (const tr of grid.rows()) {
        const amount = num(value(tr, "amount"));
        total += amount + amount * rateOf(value(tr, "tax_code_id"), vat) - amount * rateOf(value(tr, "withholding_code_id"), wht);
      }
      const bank = banks.find((b) => b.account_id === form.elements.bank_account_id.value);
      $("form-totals").innerHTML = `<b>${receipt ? "Received" : "Paid"} ${money(total, bank?.currency)} ${bank?.currency ?? ""}</b> <span class="muted small">estimate</span>`;
    };
    const grid = lineGrid(form.querySelector("table.lines"), columns, totals);
    form.elements.party_id.onchange = () => { for (const tr of grid.rows()) field(tr, "invoice_id").innerHTML = invoiceOptions(value(tr, "invoice_id")); };
    const showRate = () => form.querySelector("[data-rate]").classList.toggle("hidden",
      banks.find((b) => b.account_id === form.elements.bank_account_id.value)?.currency === state.ledger.base_currency);
    form.elements.bank_account_id.onchange = () => { showRate(); totals(); };
    showRate();
    form.querySelector("[data-add]").onclick = () => grid.add();
    for (const l of doc?.lines ?? [null]) grid.add(l);
    totals();

    const url = `${ledgerUrl()}/${path}`;
    onSave(form, async (post) => {
      const f = form.elements;
      const text = (v) => (v.trim() === "" ? null : v.trim());
      const lines = grid.rows().map((tr) => ({
        ...Object.fromEntries(columns.map((col) => [col.name, value(tr, col.name)])),
        ...(tr.dataset.adjusted ? JSON.parse(tr.dataset.adjusted) : {}),
      })).filter((l) => l.amount);
      if (!lines.length) throw new Error("Add at least one line with an amount.");
      const foreign = banks.find((b) => b.account_id === f.bank_account_id.value)?.currency !== state.ledger.base_currency;
      const body = {
        bank_account_id: f.bank_account_id.value, party_id: text(f.party_id.value), date: f.date.value,
        reference: text(f.reference.value), description: text(f.doc_description.value), lines, post,
        ...(foreign ? { exchange_rate: text(f.exchange_rate.value) } : {}),
      };
      const result = await (doc ? write("PUT", `${url}/${doc.id}`, body, doc.version) : write("POST", url, body));
      $("form-dialog").close();
      await refreshLedger();
      showPayment(result.data, await partyNames());
    });
    onDelete(form, doc, `${name} ${doc?.payment_no}`, `${url}/${doc?.id}`);
  } catch (error) {
    showError(error);
  }
}

async function editPayment(type, id) {
  $("journal-dialog").close();
  const doc = (await api(`${ledgerUrl()}/${type === "receipt" ? "receipts" : "payments"}`)).find((d) => d.id === id);
  if (doc) paymentForm(type, doc);
}

// ---- Manual journals ----

/** A journal entered by hand. Journals made by documents change only through their document. */
async function journalForm(j) {
  try {
    const [accounts, parties, classes, locations, taxCodes] = await Promise.all([
      api(`${ledgerUrl()}/accounts`), api(`${ledgerUrl()}/parties`), api(`${ledgerUrl()}/classes`),
      api(`${ledgerUrl()}/locations`), api(`${ledgerUrl()}/tax-codes`),
    ]);
    const activeClasses = classes.filter((c) => c.active);
    const activeLocations = locations.filter((l) => l.active);
    const manualTaxCodes = taxCodes.filter((t) => t.active && ["vat_output", "vat_input", "vat_input_non_deductible", "other_tax"].includes(t.kind));
    const postable = accounts.filter((a) => a.postable && a.active);
    const form = openForm(j ? `Edit journal ${j.journal_no}` : "New journal", `
      <div class="grid">
        <label>Date <input type="date" name="accounting_date" value="${j?.accounting_date ?? today()}" required></label>
        <label>Reference <input name="reference" value="${esc(j?.reference ?? "")}"></label>
        <label class="wide">Description <input name="doc_description" value="${esc(j?.description ?? "")}"></label>
      </div>
      <table class="lines"></table>
      <p class="muted small">Debit and credit include tax when a tax code is chosen: the server posts the net to this account and the tax to the code's tax account. Class, location and party are optional dimensions; a control account needs a party.</p>
      <p><button type="button" class="secondary" data-add>Add line</button></p>
      <p id="form-totals" class="totals"></p>
      <p class="actions">
        ${j ? `<button type="button" class="danger" data-delete>Delete</button>` : ""}
        <span class="spacer"></span>
        <button type="button" class="secondary" data-cancel>Cancel</button>
        <button type="submit" class="secondary" data-post="false">Save draft</button>
        <button type="submit" data-post="true">Save and post</button>
      </p>`);
    const amount = (v) => (v && Number(v) !== 0 ? v : "");
    // A tax-bearing line is entered gross; the API splits it into net + tax. On edit the line comes back net, so add
    // the signed tax back before showing debit/credit.
    const grossOf = (l) => (Number(l?.debit ?? 0) - Number(l?.credit ?? 0)) + Number(l?.tax_amount ?? 0);
    const debitOf = (l) => { const g = grossOf(l); return g > 0 ? g : ""; };
    const creditOf = (l) => { const g = grossOf(l); return g < 0 ? -g : ""; };
    const columns = [
      { name: "account_id", head: "Account", cell: (l) => `<select name="account_id">${options(postable, l?.account_id, (a) => `${a.code} ${a.name}`, "Choose")}</select>` },
      { name: "description", head: "Description", cell: (l) => `<input name="description" value="${esc(l?.description ?? "")}">` },
      { name: "debit", head: "Debit", num: true, cell: (l) => `<input name="debit" class="num" inputmode="decimal" value="${amount(debitOf(l))}" title="Gross when a tax code is chosen">` },
      { name: "credit", head: "Credit", num: true, cell: (l) => `<input name="credit" class="num" inputmode="decimal" value="${amount(creditOf(l))}" title="Gross when a tax code is chosen">` },
      { name: "party_id", head: "Party", cell: (l) => `<select name="party_id">${options(parties.filter((p) => p.active), l?.party_id, (p) => p.name, "")}</select>` },
      { name: "class_id", head: "Class", cell: (l) => `<select name="class_id">${options(activeClasses, l?.class_id, (c) => c.name, "")}</select>` },
      { name: "location_id", head: "Location", cell: (l) => `<select name="location_id">${options(activeLocations, l?.location_id, (l2) => l2.name, "")}</select>` },
      { name: "tax_code_id", head: "Tax", cell: (l) => `<select name="tax_code_id">${options(manualTaxCodes, l?.tax_code_id, (t) => `${t.name} ${t.rate}%`, "")}</select>` },
      { name: "tax_direction", head: "Tax direction", cell: (l) => `<select name="tax_direction">${options([{ id: "sale", label: "Sale" }, { id: "sale_adjustment", label: "Sale adjustment" }, { id: "purchase", label: "Purchase" }, { id: "purchase_adjustment", label: "Purchase adjustment" }], l?.tax_direction, (d) => d.label, "Auto")}</select>` },
      { name: "currency_amount", head: "Bank currency", num: true, cell: (l) => `<input name="currency_amount" class="num" inputmode="decimal" value="${l?.currency_amount ? String(Math.abs(Number(l.currency_amount))) : ""}" title="Lines on a bank account in another currency: the amount in that currency">` },
    ];
    const totals = () => {
      let dr = 0, cr = 0;
      for (const tr of grid.rows()) { dr += num(value(tr, "debit")); cr += num(value(tr, "credit")); }
      const diff = Math.round((dr - cr) * 100) / 100;
      $("form-totals").innerHTML = `Debit ${money(dr)} · credit ${money(cr)} · ${diff === 0 ? `<span class="ok">balanced</span>` : `<span class="error">difference ${money(diff)}</span>`}`;
    };
    const grid = lineGrid(form.querySelector("table.lines"), columns, totals);
    form.querySelector("[data-add]").onclick = () => grid.add();
    for (const l of j?.lines ?? [null, null]) grid.add(l);
    totals();

    const url = `${ledgerUrl()}/journals`;
    onSave(form, async (post) => {
      const f = form.elements;
      const text = (v) => (v.trim() === "" ? null : v.trim());
      const lines = grid.rows().map((tr) => Object.fromEntries(columns.map((col) => [col.name, value(tr, col.name)])))
        .filter((l) => l.account_id || l.debit || l.credit);
      if (lines.some((l) => !l.account_id)) throw new Error("Every line needs an account.");
      const body = { accounting_date: f.accounting_date.value, reference: text(f.reference.value), description: text(f.doc_description.value), lines, post };
      const result = await (j ? write("PUT", `${url}/${j.id}`, body, j.version) : write("POST", url, body));
      $("form-dialog").close();
      await refreshLedger();
      showJournal(result.data);
    });
    onDelete(form, j, `journal ${j?.journal_no}`, `${url}/${j?.id}`);
  } catch (error) {
    showError(error);
  }
}

async function editJournal(id) {
  $("journal-dialog").close();
  journalForm(await api(`${ledgerUrl()}/journals/${id}`));
}
