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
  const [parties, accounts, items, taxCodes, invoices, classes, locations] = await Promise.all([
    api(`${ledgerUrl()}/parties`), api(`${ledgerUrl()}/accounts`), api(`${ledgerUrl()}/items`), api(`${ledgerUrl()}/tax-codes`),
    api(`${ledgerUrl()}/${sale ? "sales-invoices" : "purchase-invoices"}`), api(`${ledgerUrl()}/classes`), api(`${ledgerUrl()}/locations`),
  ]);
  const usable = (a) => a.postable && a.active && !a.is_control && !a.is_bank;
  return {
    parties: parties.filter((p) => p.active && (sale ? p.is_customer : p.is_supplier)),
    accounts: accounts.filter((a) => usable(a) && (sale ? a.type === "income" : ["cost_and_expense", "assets"].includes(a.type))),
    items: items.filter((i) => i.active),
    vat: taxCodes.filter((t) => t.active && [sale ? "vat_output" : "vat_input", "vat_zero_rated", "non_vat"].includes(t.kind)),
    other: taxCodes.filter((t) => t.active && ["other_tax", "state_charge"].includes(t.kind)),
    wht: taxCodes.filter((t) => t.active && t.kind === "withholding"),
    invoices: invoices.filter((i) => i.status === "posted" && i.type === "invoice"),
    classes: classes.filter((c) => c.active),
    locations: locations.filter((l) => l.active),
  };
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

