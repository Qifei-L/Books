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
  throw new Error(json?.detail ?? json?.title ?? `HTTP ${response.status}`);
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

async function refreshLedger() {
  await Promise.all([renderSales(), renderSetup(), renderJournals()]);
}

// ---- Customers and suppliers ----

function partyForm(p) {
  const v = (field) => esc(p?.[field] ?? "");
  const form = openForm(p ? `Edit ${p.name}` : "New customer or supplier", `
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

async function editParty(id) {
  const p = (await api(`${ledgerUrl()}/parties`)).find((x) => x.id === id);
  if (p) partyForm(p);
}

// ---- Sales invoices and credit notes ----

/** Loads what the sales form offers: customers, income accounts, items, sales tax codes, and invoices a credit note may correct. */
async function salesChoices() {
  const [parties, accounts, items, taxCodes, invoices] = await Promise.all([
    api(`${ledgerUrl()}/parties`), api(`${ledgerUrl()}/accounts`), api(`${ledgerUrl()}/items`), api(`${ledgerUrl()}/tax-codes`),
    api(`${ledgerUrl()}/sales-invoices`),
  ]);
  return {
    customers: parties.filter((p) => p.is_customer && p.active),
    accounts: accounts.filter((a) => a.type === "income" && a.postable && a.active),
    items: items.filter((i) => i.active),
    vat: taxCodes.filter((t) => t.active && ["vat_output", "vat_zero_rated", "non_vat"].includes(t.kind)),
    other: taxCodes.filter((t) => t.active && t.kind === "other_tax"),
    invoices: invoices.filter((i) => i.status === "posted"),
  };
}

async function salesForm(kind, doc) {
  try {
    const c = await salesChoices();
    const creditNote = kind === "credit_note";
    const path = creditNote ? "credit-notes" : "sales-invoices";
    const name = creditNote ? "credit note" : "invoice";
    const base = state.ledger.base_currency;
    const currency = doc?.currency || base;
    const form = openForm(doc ? `Edit ${name} ${doc.invoice_no}` : `New ${name}`, `
      <div class="grid">
        <label class="wide">Customer <select name="customer_id" required>${options(c.customers, doc?.customer_id, (p) => p.name, "Choose a customer")}</select></label>
        <label>Date <input type="date" name="date" value="${doc?.date ?? today()}" required></label>
        ${creditNote
          ? `<label>Corrects invoice <select name="original_invoice_id">${options(c.invoices, doc?.original_invoice_id, (i) => `SI ${i.invoice_no} · ${i.date} · ${i.gross_total}`, "None")}</select></label>`
          : `<label>Due date <input type="date" name="due_date" value="${doc?.due_date ?? ""}" title="Leave empty for the customer's payment terms"></label>`}
        <label>Reference <input name="reference" value="${esc(doc?.reference ?? "")}"></label>
        <label>Currency <select name="currency">${[...new Set([base, "USD", "KHR"])].map((x) => `<option ${x === currency ? "selected" : ""}>${x}</option>`).join("")}</select></label>
        <label data-rate>Exchange rate <input name="exchange_rate" inputmode="decimal" value="${doc?.exchange_rate ?? ""}" placeholder="${base} rate of the date"></label>
        <label class="wide">Description <input name="description" value="${esc(doc?.description ?? "")}"></label>
      </div>
      <table class="lines">
        <tr><th>Item</th><th>Account</th><th>Description</th><th class="num">Qty</th><th class="num">Unit price</th><th class="num">Amount</th><th>VAT</th><th>Other tax</th><th></th></tr>
      </table>
      <p><button type="button" class="secondary" data-add>Add line</button></p>
      <p id="sales-totals" class="totals"></p>
      <p class="actions">
        ${doc ? `<button type="button" class="danger" data-delete>Delete</button>` : ""}
        <span class="spacer"></span>
        <button type="button" class="secondary" data-cancel>Cancel</button>
        <button type="submit" class="secondary" data-post="false">Save draft</button>
        <button type="submit" data-post="true">Save and post</button>
      </p>`);
    const table = form.querySelector("table.lines");
    const addLine = (l) => {
      const tr = table.insertRow();
      tr.dataset.adjusted = l?.tax_adjusted ? JSON.stringify({ tax_amount: l.tax_amount, tax_reason: l.tax_reason }) : "";
      tr.innerHTML = `
        <td><select name="item_id">${options(c.items, l?.item_id, (i) => i.name, "")}</select></td>
        <td><select name="account_id">${options(c.accounts, l?.account_id, (a) => `${a.code} ${a.name}`, "Choose")}</select></td>
        <td><input name="description" value="${esc(l?.description ?? "")}"></td>
        <td><input name="quantity" class="num" inputmode="decimal" value="${l?.quantity ?? ""}"></td>
        <td><input name="unit_price" class="num" inputmode="decimal" value="${l?.unit_price ?? ""}"></td>
        <td><input name="amount" class="num" inputmode="decimal" value="${l && !l.quantity ? l.net_amount : ""}" placeholder="qty × price"></td>
        <td><select name="tax_code_id">${options(c.vat, l?.tax_code_id, (t) => t.name, "None")}</select></td>
        <td><select name="other_tax_code_id">${options(c.other, l?.other_tax_code_id, (t) => t.name, "None")}</select></td>
        <td><button type="button" class="link" title="Remove line" data-remove>×</button></td>`;
      const item = tr.querySelector("[name=item_id]");
      item.onchange = () => {
        const i = c.items.find((x) => x.id === item.value);
        if (!i) return;
        if (i.default_account_id) tr.querySelector("[name=account_id]").value = i.default_account_id;
        if (i.default_tax_code_id) tr.querySelector("[name=tax_code_id]").value = i.default_tax_code_id;
        const description = tr.querySelector("[name=description]");
        if (!description.value) description.value = i.name;
        totals();
      };
      tr.querySelector("[data-remove]").onclick = () => { tr.remove(); totals(); };
    };
    const rows = () => [...table.rows].slice(1);
    const rate = (id, list) => Number(list.find((t) => t.id === id)?.rate ?? 0) / 100;
    const net = (tr) => {
      const v = (n) => tr.querySelector(`[name=${n}]`).value.trim();
      return v("amount") !== "" ? Number(v("amount")) : Number(v("quantity") || 0) * Number(v("unit_price") || 0);
    };
    // An estimate for the person entering; the server calculates and rounds the figures that are saved.
    const totals = () => {
      let n = 0, other = 0, vat = 0;
      for (const tr of rows()) {
        const amount = net(tr);
        const o = amount * rate(tr.querySelector("[name=other_tax_code_id]").value, c.other);
        n += amount; other += o; vat += (amount + o) * rate(tr.querySelector("[name=tax_code_id]").value, c.vat);
      }
      const cur = form.elements.currency.value;
      const fmt = (x) => x.toLocaleString(undefined, { minimumFractionDigits: cur === "KHR" ? 0 : 2, maximumFractionDigits: cur === "KHR" ? 0 : 2 });
      $("sales-totals").innerHTML = `Net ${fmt(n)}${other ? ` · other tax ${fmt(other)}` : ""} · VAT ${fmt(vat)} · <b>total ${fmt(n + other + vat)} ${cur}</b>
        <span class="muted small">estimate; saved figures are calculated by the server</span>`;
    };
    const showRate = () => form.querySelector("[data-rate]").classList.toggle("hidden", form.elements.currency.value === base);
    form.elements.currency.onchange = () => { showRate(); totals(); };
    table.oninput = totals;
    table.onchange = totals;
    form.querySelector("[data-add]").onclick = () => addLine();
    for (const l of doc?.lines ?? [null, null]) addLine(l);
    showRate();
    totals();

    let post = true;
    for (const b of form.querySelectorAll("[data-post]")) b.onclick = () => { post = b.dataset.post === "true"; };
    form.onsubmit = (event) => {
      event.preventDefault();
      busy(form, async () => {
        const f = form.elements;
        const text = (v) => (v.trim() === "" ? null : v.trim());
        const lines = rows().map((tr) => {
          const get = (n) => text(tr.querySelector(`[name=${n}]`).value);
          return {
            account_id: get("account_id"), item_id: get("item_id"), description: get("description"),
            quantity: get("quantity"), unit_price: get("unit_price"), amount: get("amount"),
            tax_code_id: get("tax_code_id"), other_tax_code_id: get("other_tax_code_id"),
            ...(tr.dataset.adjusted ? JSON.parse(tr.dataset.adjusted) : {}),
          };
        }).filter((l) => l.account_id || l.amount || l.unit_price || l.description);
        if (!f.customer_id.value) throw new Error("Choose a customer.");
        if (!lines.length) throw new Error("Add at least one line.");
        if (lines.some((l) => !l.account_id)) throw new Error("Every line needs an account.");
        const body = {
          customer_id: f.customer_id.value, date: f.date.value, reference: text(f.reference.value), description: text(f.description.value), lines, post,
          ...(creditNote ? { original_invoice_id: text(f.original_invoice_id.value) } : { due_date: text(f.due_date.value) }),
          ...(f.currency.value !== base ? { currency: f.currency.value, exchange_rate: text(f.exchange_rate.value) } : {}),
        };
        const url = `${ledgerUrl()}/${path}`;
        const result = await (doc ? write("PUT", `${url}/${doc.id}`, body, doc.version) : write("POST", url, body));
        $("form-dialog").close();
        await refreshLedger();
        const parties = await api(`${ledgerUrl()}/parties`);
        showSale(result.data, (id) => parties.find((p) => p.id === id)?.name ?? "");
      });
    };
    form.querySelector("[data-delete]")?.addEventListener("click", () => busy(form, async () => {
      if (!confirm(`Delete ${name} ${doc.invoice_no}?${doc.status === "posted" ? " Its journal is removed too." : ""}`)) return;
      await write("DELETE", `${ledgerUrl()}/${path}/${doc.id}`, undefined, doc.version);
      $("form-dialog").close();
      await refreshLedger();
    }));
  } catch (error) {
    showError(error);
  }
}

async function editSale(kind, id) {
  $("journal-dialog").close();
  const doc = (await api(`${ledgerUrl()}/${kind === "credit_note" ? "credit-notes" : "sales-invoices"}`)).find((d) => d.id === id);
  if (doc) salesForm(kind, doc);
}
