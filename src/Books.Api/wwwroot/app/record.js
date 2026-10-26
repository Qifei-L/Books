// Full-page record surface (owner decision C+A): one page for view, edit and new, with the same header,
// line grid and action bar. Journal is migrated first, then sales, purchases and payments. The URL is the
// state: #/ledger/{ledger}/journals/{id}, .../new, and reports keep their filters in the query (G).

const RECORD_ROUTES = {
  journals: { type: "journal", label: "Journal" },
  "sales-invoices": { type: "sale", docKind: "invoice", label: "Sales invoice" },
  "credit-notes": { type: "sale", docKind: "credit_note", label: "Credit note" },
  "purchase-invoices": { type: "purchase", docKind: "purchase_invoice", label: "Purchase invoice" },
  "debit-notes": { type: "purchase", docKind: "debit_note", label: "Debit note" },
  payments: { type: "payment", label: "Payment" },
  receipts: { type: "payment", label: "Receipt" },
};

function parseRecordRoute(hash = location.hash) {
  const match = /^#\/ledger\/([0-9a-fA-F-]+)\/([a-z-]+)\/(new|[0-9a-fA-F-]+|[a-z]+)(?:\?(.*))?$/.exec(hash);
  if (!match) return null;
  return { ledger: match[1], segment: match[2], id: match[3], query: new URLSearchParams(match[4] ?? "") };
}

function openRecordPage(segment, id) {
  if (!state.ledger) return;
  state.recordReturnTab = state.tab ?? state.recordReturnTab;
  if (state.recordReturnTab) sessionStorage.setItem("books.recordReturn", state.recordReturnTab);
  location.hash = `#/ledger/${state.ledger.id}/${segment}/${id ?? "new"}`;
}

function defaultReturnTab(segment) {
  return ({ journals: "journals", "sales-invoices": "sales-invoices", "credit-notes": "sales-credit-notes",
    "purchase-invoices": "purchases-invoices", "debit-notes": "purchases-debit-notes",
    payments: "bank-transactions", receipts: "bank-transactions" })[segment] ?? state.tab ?? "journals";
}

// The page a record returns to: whatever list or report was open before it.
function leaveRecord(refresh = true) {
  if (!canLeaveRecord()) return;
  state.recordDirty = false;
  state.record = null;
  $("record-page").classList.add("hidden");
  $("home").classList.remove("hidden");
  if (location.hash) history.replaceState(null, "", location.pathname + location.search);
  if (refresh) showTab(state.recordReturnTab ?? state.tab ?? "journals", true);
}

function canLeaveRecord() {
  if (!state.recordDirty) return true;
  if (!confirm("Discard the unsaved changes?")) return false;
  state.recordDirty = false;
  return true;
}

function markRecordDirty() { state.recordDirty = true; }

function recordError(error) {
  const target = $("record-error");
  target.textContent = error.message;
  target.classList.remove("hidden");
}

function clearRecordError() { $("record-error").classList.add("hidden"); }

function showRecordShell({ title, status, subtitle, actions, body, footer, editing }) {
  $("home").classList.add("hidden");
  $("record-page").classList.remove("hidden");
  $("record-title").textContent = title;
  $("record-status").innerHTML = status ? `<span class="pill ${status}">${esc(status)}</span>` : "";
  $("record-sub").textContent = subtitle ?? "";
  $("record-actions").innerHTML = actions ?? "";
  $("record-body").innerHTML = `<p id="record-error" class="error hidden"></p>${body}`;
  $("record-bar").classList.toggle("hidden", !editing);
  if (editing) $("record-totals").innerHTML = footer ?? "";
  clearRecordError();
}

// Resolve a ledger id from a deep link before rendering it.
async function ensureRecordLedger(ledgerId) {
  if (state.ledger?.id === ledgerId) return true;
  const orgs = await api("/api/v1/me/organizations");
  for (const o of orgs) {
    const ledgers = await api(`/api/v1/organizations/${o.id}/ledgers`);
    const ledger = ledgers.find((l) => l.id === ledgerId);
    if (ledger) {
      state.org = o.id;
      state.role = o.role;
      $("org-pick").value = o.id;
      $("ledger-pick").innerHTML = ledgers.map((l) => `<option value="${l.id}">${esc(l.company_name)}</option>`).join("");
      $("ledger-pick").onchange = (e) => chooseLedger(ledgers.find((l) => l.id === e.target.value));
      await chooseLedger(ledger);
      return true;
    }
  }
  return false;
}

async function applyRoute() {
  const route = parseRecordRoute();
  if (!route) {
    if (state.record) leaveRecord();
    return;
  }
  if (!(await ensureRecordLedger(route.ledger))) {
    showError(new Error("That ledger is not available."));
    return;
  }
  if (route.segment === "reports") {
    await renderReportRoute(route);
    return;
  }
  state.record = route;
  state.recordHash = location.hash;
  state.recordReturnTab = state.recordReturnTab ?? sessionStorage.getItem("books.recordReturn") ?? defaultReturnTab(route.segment);
  state.recordDirty = false;
  try {
    if (route.segment === "journals") await renderJournalRecord(route.id);
    else if (route.segment === "payments" || route.segment === "receipts") await renderPaymentRecord(route);
    else if (RECORD_ROUTES[route.segment]) await renderDocumentRecord(route);
    else {
      leaveRecord(false);
      showError(new Error("Unknown record address."));
    }
  } catch (error) {
    showError(error);
  }
}

window.addEventListener("hashchange", async () => {
  if (state.record && !canLeaveRecord()) {
    history.replaceState(null, "", state.recordHash ?? location.href);
    return;
  }
  await applyRoute();
});

window.addEventListener("beforeunload", (event) => {
  if (!state.recordDirty) return;
  event.preventDefault();
  event.returnValue = "";
});

$("record-back").onclick = () => leaveRecord();
document.addEventListener("keydown", (event) => {
  if (event.key !== "Escape" || !state.record || document.querySelector("dialog[open]")) return;
  leaveRecord();
});

// ---- Shared form helpers ----

function recordField(label, name, value, type = "text", extra = "") {
  return `<label>${label} <input type="${type}" name="${name}" value="${esc(value ?? "")}" ${extra}></label>`;
}

function recordSelect(label, name, rows, selected, labelOf, empty, wide = false) {
  return `<label class="${wide ? "wide" : ""}">${label} <select name="${name}">${options(rows, selected, labelOf, empty)}</select></label>`;
}

function wireRecordDirty() {
  const root = $("record-page");
  root.oninput = markRecordDirty;
  root.onchange = markRecordDirty;
}

function wireRecordFooter({ onDraft, onPost, onCancel }) {
  const guard = (action) => async () => {
    if (state.recordSaving) return;
    state.recordSaving = true;
    try {
      await action();
    } finally {
      state.recordSaving = false;
    }
  };
  $("record-cancel").onclick = () => {
    // Cancel discards the draft without a second confirmation; Back still asks.
    state.recordDirty = false;
    if (onCancel) onCancel();
    else leaveRecord();
  };
  $("record-save-draft").onclick = guard(onDraft);
  $("record-post").onclick = guard(onPost);
}

function recordTotals(html) { $("record-totals").innerHTML = html; }

function recordActions(html) { $("record-actions").innerHTML = html; }

function saveThenOpen(segment, id) {
  state.recordDirty = false;
  state.rendered = {};
  state.docData = {};
  const target = `#/ledger/${state.ledger.id}/${segment}/${id}`;
  if (location.hash === target) {
    history.replaceState(null, "", target);
    applyRoute();
  } else {
    location.hash = target;
  }
}

/** Cancel returns to the record's view: the same hash needs an explicit re-render. */
function cancelRecordTo(segment, id) {
  state.recordDirty = false;
  const target = `#/ledger/${state.ledger.id}/${segment}/${id}`;
  if (location.hash === target) {
    history.replaceState(null, "", target);
    applyRoute();
  } else {
    openRecordPage(segment, id);
  }
}

// ---- Journals ----

async function renderJournalRecord(id) {
  if (id === "new") {
    await journalRecordForm(null);
    return;
  }
  const journal = await api(`${ledgerUrl()}/journals/${id}`);
  const parties = await api(`${ledgerUrl()}/parties`);
  state._parties = parties;
  const lines = journal.lines ?? [];
  const actions = [
    journal.source_kind
      ? `<button type="button" class="secondary small" onclick="openDocument('${journal.source_kind}', '${journal.source_id}')">Open document</button>`
      : canEdit() && journal.status !== "deleted"
        ? `<button type="button" class="secondary small" onclick="journalRecordFormCurrent('${journal.id}')">Edit</button>`
        : "",
    canEdit() && !journal.source_kind && journal.status !== "deleted"
      ? `<button type="button" class="danger small" onclick="deleteJournalRecord('${journal.id}', ${journal.version}, ${journal.journal_no})">Delete</button>`
      : "",
  ].join(" ");
  showRecordShell({
    title: `Journal ${journal.journal_no}`,
    status: journal.status,
    subtitle: `${journal.accounting_date} · revision ${journal.revision_no} · version ${journal.version}`,
    actions,
    body: `<dl class="facts">
        <dt>Date</dt><dd>${journal.accounting_date}</dd>
        <dt>Reference</dt><dd>${esc(journal.reference ?? "—")}</dd>
        <dt>Description</dt><dd>${esc(journal.description ?? "—")}</dd>
        <dt>Source</dt><dd>${journal.source_kind ? esc(SOURCES[journal.source_kind] ?? journal.source_kind) : "Manual journal"}</dd>
      </dl>
      <table><tr><th>Account</th><th>Description</th><th>Class</th><th>Location</th><th>Party</th><th class="num">Debit</th><th class="num">Credit</th></tr>
      ${lines.map((l) => `<tr><td>${esc(accountName(l.account_id))}</td><td>${esc(l.description ?? "")}</td><td>${esc(dimensionName("class", l.class_id))}</td>
        <td>${esc(dimensionName("location", l.location_id))}</td><td>${esc(partyNameOf(l.party_id))}</td>
        <td class="num">${blank(l.debit)}</td><td class="num">${blank(l.credit)}</td></tr>`).join("")}
      <tr class="total"><td colspan="5">Total</td><td class="num">${sumOf(lines, "debit")}</td><td class="num">${sumOf(lines, "credit")}</td></tr></table>
      ${journal.issues.map((i) => `<div class="warning">${esc(i.code)}: ${esc(i.message)}</div>`).join("")}
      ${attachmentsPanel("journal", journal.id)}`,
    editing: false,
  });
  wireAttachments($("record-body"));
}

async function journalRecordFormCurrent(id) {
  if (!canLeaveRecord()) return;
  await journalRecordForm(await api(`${ledgerUrl()}/journals/${id}`));
}

async function journalRecordForm(journal) {
  try {
    const [accounts, parties, classes, locations, taxCodes] = await Promise.all([
      api(`${ledgerUrl()}/accounts`), api(`${ledgerUrl()}/parties`), api(`${ledgerUrl()}/classes`),
      api(`${ledgerUrl()}/locations`), api(`${ledgerUrl()}/tax-codes`),
    ]);
    const activeClasses = classes.filter((c) => c.active);
    const activeLocations = locations.filter((l) => l.active);
    const manualTaxCodes = taxCodes.filter((t) => t.active && ["vat_output", "vat_input", "vat_input_non_deductible", "other_tax"].includes(t.kind));
    const postable = accounts.filter((a) => a.postable && a.active && (isAdmin() || !a.is_control));
    showRecordShell({
      title: journal ? `Journal ${journal.journal_no}` : "New journal",
      status: journal?.status ?? "draft",
      subtitle: "Manual journals are saved as draft or posted in one action; the journal and its lines change together.",
      body: `<div class="grid">
          ${recordField("Date", "accounting_date", journal?.accounting_date ?? today(), "date", "required")}
          ${recordField("Reference", "reference", journal?.reference)}
          <label class="wide">Description <input name="description" value="${esc(journal?.description ?? "")}"></label>
        </div>
        <table class="lines"></table>
        <p class="muted small">Debit and credit include tax when a tax code is chosen: the server posts the net to this account and the tax to the code's tax account. Class, location and party are optional dimensions; a control account needs a party.</p>
        <p><button type="button" class="secondary" data-add>Add line</button></p>`,
      editing: true,
      footer: "Debit 0.00 · credit 0.00",
    });

    const amount = (v) => (v && Number(v) !== 0 ? v : "");
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
      { name: "location_id", head: "Location", cell: (l) => `<select name="location_id">${options(activeLocations, l?.location_id, (x) => x.name, "")}</select>` },
      { name: "tax_code_id", head: "Tax", cell: (l) => `<select name="tax_code_id">${options(manualTaxCodes, l?.tax_code_id, (t) => `${t.name} ${t.rate}%`, "")}</select>` },
      { name: "tax_direction", head: "Tax direction", cell: (l) => `<select name="tax_direction">${options([{ id: "sale", label: "Sale" }, { id: "sale_adjustment", label: "Sale adjustment" }, { id: "purchase", label: "Purchase" }, { id: "purchase_adjustment", label: "Purchase adjustment" }], l?.tax_direction, (d) => d.label, "Auto")}</select>` },
      { name: "currency_amount", head: "Bank currency", num: true, cell: (l) => `<input name="currency_amount" class="num" inputmode="decimal" value="${l?.currency_amount ? String(Math.abs(Number(l.currency_amount))) : ""}" title="Lines on a bank account in another currency: the amount in that currency">` },
    ];
    const totals = () => {
      let dr = 0, cr = 0;
      for (const tr of grid.rows()) { dr += num(value(tr, "debit")); cr += num(value(tr, "credit")); }
      const diff = Math.round((dr - cr) * 100) / 100;
      recordTotals(`Debit ${money(dr)} · credit ${money(cr)} · ${diff === 0 ? `<span class="ok">balanced</span>` : `<span class="error">difference ${money(diff)}</span>`}`);
    };
    const grid = lineGrid($("record-body").querySelector("table.lines"), columns, totals);
    $("record-body").querySelector("[data-add]").onclick = () => grid.add();
    for (const l of journal?.lines ?? [null, null]) if (!l?.tax_line) grid.add(l);
    totals();
    wireRecordDirty();
    wireRecordFooter({
      onDraft: () => saveJournal(false, grid, columns),
      onPost: () => saveJournal(true, grid, columns),
      onCancel: () => (journal ? cancelRecordTo("journals", journal.id) : leaveRecord()),
    });

    async function saveJournal(post, rows, cols) {
      clearRecordError();
      try {
        const text = (v) => (v.trim() === "" ? null : v.trim());
        const lines = rows.rows().map((tr) => Object.fromEntries(cols.map((col) => [col.name, value(tr, col.name)])))
          .filter((l) => l.account_id || l.debit || l.credit);
        if (lines.some((l) => !l.account_id)) throw new Error("Every line needs an account.");
        const body = {
          accounting_date: $("record-body").querySelector('[name="accounting_date"]').value,
          reference: text($("record-body").querySelector('[name="reference"]').value),
          description: text($("record-body").querySelector('[name="description"]').value),
          lines, post,
        };
        const result = await (journal ? write("PUT", `${ledgerUrl()}/journals/${journal.id}`, body, journal.version) : write("POST", `${ledgerUrl()}/journals`, body));
        saveThenOpen("journals", result.data.id);
      } catch (error) {
        recordError(error);
      }
    }
  } catch (error) {
    showError(error);
  }
}

async function deleteJournalRecord(id, version, no) {
  if (!confirm(`Delete journal ${no}? Its posting is removed.`)) return;
  try {
    await write("DELETE", `${ledgerUrl()}/journals/${id}`, undefined, version);
    leaveRecord();
  } catch (error) {
    showError(error);
  }
}

// ---- Sales and purchase documents ----

function dimensionName(kind, id) {
  const rows = kind === "class" ? state.classes : state.locations;
  return rows?.find((r) => r.id === id)?.name ?? "";
}

function partyNameOf(id) {
  return state._parties?.find((p) => p.id === id)?.name ?? "";
}

function sumOf(rows, key) {
  return money(rows.reduce((s, r) => s + Number(r[key] ?? 0), 0));
}

async function renderDocumentRecord(route) {
  const spec = RECORD_ROUTES[route.segment];
  const path = route.segment;
  if (route.id === "new") {
    await documentRecordForm(route.segment, spec.docKind, null);
    return;
  }
  const docs = await api(`${ledgerUrl()}/${path}`);
  const doc = docs.find((d) => d.id === route.id);
  if (!doc) throw new Error("That document was not found.");
  const parties = await api(`${ledgerUrl()}/parties`);
  state._parties = parties;
  const party = parties.find((p) => p.id === (spec.type === "sale" ? doc.customer_id : doc.supplier_id))?.name ?? "";
  const noun = spec.label;
  const actions = [
    doc.journal_id ? `<button type="button" class="secondary small" onclick="openJournal('${doc.journal_id}')">Journal ${doc.journal_no ?? ""}</button>` : "",
    spec.type === "sale" ? `<button type="button" class="secondary small" onclick="openPdf('${path}/${doc.id}/pdf')">Print PDF</button>` : "",
    canEdit() ? `<button type="button" class="secondary small" onclick="documentRecordForm('${path}', '${spec.docKind}', '${doc.id}')">Edit</button>` : "",
    canEdit() ? `<button type="button" class="danger small" onclick="deleteDocumentRecord('${path}', '${doc.id}', ${doc.version}, '${doc.invoice_no}')">Delete</button>` : "",
  ].join(" ");
  const taxCurrency = doc.tax_currency
    ? `<p class="muted">${doc.tax_currency.currency} at ${doc.tax_currency.rate} (${esc(doc.tax_currency.rate_source)}, ${doc.tax_currency.rate_date})${doc.tax_currency.reason ? ` · ${esc(doc.tax_currency.reason)}` : ""}:
        gross ${doc.tax_currency.gross_total} · VAT ${doc.tax_currency.tax_total}</p>` : "";
  showRecordShell({
    title: `${noun} ${doc.invoice_no}`,
    status: doc.status,
    subtitle: `${party} · ${doc.date} · due ${doc.due_date} · ${esc(doc.reference ?? "")}`,
    actions,
    body: `<table><tr><th>Account</th><th>Description</th><th>Class</th><th>Location</th><th class="num">Net</th><th class="num">VAT</th>${spec.type === "sale" ? '<th class="num">Other tax</th>' : '<th class="num">WHT</th>'}</tr>
      ${doc.lines.map((l) => `<tr><td>${esc(accountName(l.account_id))}</td><td>${esc(l.description)}</td>
        <td>${esc(dimensionName("class", l.class_id))}</td><td>${esc(dimensionName("location", l.location_id))}</td>
        <td class="num">${l.net_amount}</td><td class="num">${l.tax_amount}${l.tax_adjusted ? ` <span class="pill draft">adjusted</span>` : ""}</td>
        <td class="num">${spec.type === "sale" ? l.other_tax_amount : l.withholding_amount}</td></tr>`).join("")}
      <tr class="total"><td colspan="4">Gross ${doc.gross_total}${spec.type === "purchase" ? ` · payable ${doc.payable_total}` : ""}</td>
        <td class="num">${doc.net_total}</td><td class="num">${doc.tax_total}</td><td class="num">${spec.type === "sale" ? doc.other_tax_total : doc.withholding_total}</td></tr></table>
      ${taxCurrency}
      ${doc.issues.map((i) => `<div class="warning">${esc(i.code)}: ${esc(i.message)}</div>`).join("")}
      ${(doc.warnings ?? []).map((w) => `<div class="warning">${esc(w)}</div>`).join("")}
      ${attachmentsPanel(spec.type === "sale" ? (spec.docKind === "credit_note" ? "sales_credit_note" : "sales_invoice")
        : (spec.docKind === "debit_note" ? "purchase_debit_note" : "purchase_invoice"), doc.id)}`,
    editing: false,
  });
  wireAttachments($("record-body"));
}

async function documentRecordForm(segment, kind, id) {
  if (state.recordDirty && !canLeaveRecord()) return;
  const doc = id ? (await api(`${ledgerUrl()}/${segment}`)).find((d) => d.id === id) : null;
  const spec = DOCS[kind];
  const sale = spec.side === "sale";
  try {
    const c = await documentChoices(spec.side);
    const base = state.ledger.base_currency;
    const currency = doc?.currency || base;
    showRecordShell({
      title: doc ? `Edit ${spec.name} ${doc.invoice_no}` : `New ${spec.name}`,
      status: doc?.status ?? "draft",
      subtitle: "Save draft keeps it out of the books; Save and post writes the journal in the same transaction.",
      body: `<div class="grid">
          ${recordSelect(sale ? "Customer" : "Supplier", "party", c.parties, doc?.[sale ? "customer_id" : "supplier_id"], (p) => p.name, `Choose a ${sale ? "customer" : "supplier"}`, true)}
          ${recordField("Date", "date", doc?.date ?? today(), "date", "required")}
          ${spec.note
            ? recordSelect("Corrects invoice", "original_invoice_id", c.invoices, doc?.original_invoice_id, (i) => `${sale ? "SI" : "PI"} ${i.invoice_no} · ${i.date} · ${i.gross_total}`, "None")
            : recordField("Due date", "due_date", doc?.due_date ?? "", "date")}
          ${recordField(sale ? "Reference" : "Supplier's invoice no.", "reference", doc?.reference)}
          <label>Currency <select name="currency">${[...new Set([base, "USD", "KHR"])].map((x) => `<option ${x === currency ? "selected" : ""}>${x}</option>`).join("")}</select></label>
          <label data-rate>Exchange rate <input name="exchange_rate" inputmode="decimal" value="${esc(doc?.exchange_rate ?? "")}" placeholder="${base} rate of the date"></label>
          <label class="wide">Description <input name="doc_description" value="${esc(doc?.description ?? "")}"></label>
        </div>
        <table class="lines"></table>
        <p><button type="button" class="secondary" data-add>Add line</button></p>`,
      editing: true,
      footer: "Net 0.00 · VAT 0.00",
    });

    const columns = [
      { name: "item_id", head: "Item", cell: (l) => `<select name="item_id">${options(c.items, l?.item_id, (i) => i.name, "")}</select>`,
        init: (tr) => { field(tr, "item_id").onchange = () => fillFromItem(tr, c.items, spec.side, $("record-body").querySelector('[name="currency"]').value === base); } },
      { name: "account_id", head: "Account", cell: (l) => `<select name="account_id">${options(c.accounts, l?.account_id, (a) => `${a.code} ${a.name}`, "Choose")}</select>` },
      { name: "class_id", head: "Class", cell: (l) => `<select name="class_id">${options(c.classes, l?.class_id, (x) => x.name, "")}</select>` },
      { name: "location_id", head: "Location", cell: (l) => `<select name="location_id">${options(c.locations, l?.location_id, (x) => x.name, "")}</select>` },
      { name: "description", head: "Description", cell: (l) => `<input name="description" value="${esc(l?.description ?? "")}">` },
      { name: "quantity", head: "Qty", num: true, cell: (l) => `<input name="quantity" class="num" inputmode="decimal" value="${l?.quantity ?? ""}">` },
      { name: "unit", head: "Unit", cell: (l) => `<select name="unit">${unitOptions(c.items.find((i) => i.id === l?.item_id), l?.unit)}</select>` },
      { name: "unit_price", head: "Unit price", num: true, cell: (l) => `<input name="unit_price" class="num" inputmode="decimal" value="${l?.unit_price ?? ""}">` },
      { name: "amount", head: "Amount", num: true, cell: (l) => `<input name="amount" class="num" inputmode="decimal" value="${l && !l.quantity ? l.net_amount : ""}" placeholder="qty × price">` },
      { name: "tax_code_id", head: "VAT", cell: (l) => `<select name="tax_code_id">${options(c.vat, l?.tax_code_id, (t) => t.name, "None")}</select>` },
      ...(sale
        ? [{ name: "other_tax_code_id", head: "Other tax", cell: (l) => `<select name="other_tax_code_id">${options(c.other, l?.other_tax_code_id, (t) => t.name, "None")}</select>` }]
        : [
          { name: "withholding_code_id", head: "WHT", cell: (l) => `<select name="withholding_code_id">${options(c.wht, l?.withholding_code_id, (t) => t.name, "None")}</select>` },
        ]),
    ];
    const totals = () => {
      let net = 0, other = 0, vat = 0, wht = 0;
      for (const tr of grid.rows()) {
        const amount = value(tr, "amount") !== null ? num(value(tr, "amount")) : num(value(tr, "quantity")) * num(value(tr, "unit_price"));
        const o = amount * rateOf(value(tr, "other_tax_code_id"), c.other);
        net += amount; other += o; vat += (amount + o) * rateOf(value(tr, "tax_code_id"), c.vat);
        wht += amount * rateOf(value(tr, "withholding_code_id"), c.wht);
      }
      const cur = $("record-body").querySelector('[name="currency"]').value;
      recordTotals(`Net ${money(net, cur)}${other ? ` · other tax ${money(other, cur)}` : ""} · VAT ${money(vat, cur)}
        ${wht ? ` · WHT −${money(wht, cur)}` : ""} · <b>${sale ? "total" : "payable"} ${money(net + other + vat - wht, cur)} ${cur}</b>
        <span class="muted small">estimate; the server calculates the saved figures</span>`);
    };
    const grid = lineGrid($("record-body").querySelector("table.lines"), columns, totals);
    const showRate = () => $("record-body").querySelector("[data-rate]").classList.toggle("hidden",
      $("record-body").querySelector('[name="currency"]').value === base);
    $("record-body").querySelector('[name="currency"]').onchange = () => { showRate(); totals(); };
    $("record-body").querySelector("[data-add]").onclick = () => grid.add();
    for (const l of doc?.lines ?? [null, null]) grid.add(l);
    showRate();
    totals();
    wireRecordDirty();
    wireRecordFooter({
      onDraft: () => saveDocument(false, grid, columns, segment, kind, doc, c),
      onPost: () => saveDocument(true, grid, columns, segment, kind, doc, c),
      onCancel: () => (doc ? cancelRecordTo(segment, doc.id) : leaveRecord()),
    });
  } catch (error) {
    showError(error);
  }
}

async function saveDocument(post, grid, columns, segment, kind, doc, c) {
  clearRecordError();
  try {
    const spec = DOCS[kind];
    const sale = spec.side === "sale";
    const base = state.ledger.base_currency;
    const f = $("record-body");
    const text = (v) => (v.trim() === "" ? null : v.trim());
    const lines = grid.rows().map((tr) => ({
      ...Object.fromEntries(columns.map((col) => [col.name, value(tr, col.name)]))
    })).filter((l) => l.account_id || l.amount || l.unit_price || l.description);
    if (!f.querySelector('[name="party"]').value) throw new Error(`Choose a ${sale ? "customer" : "supplier"}.`);
    if (!lines.length) throw new Error("Add at least one line.");
    if (lines.some((l) => !l.account_id)) throw new Error("Every line needs an account.");
    const body = {
      [sale ? "customer_id" : "supplier_id"]: f.querySelector('[name="party"]').value,
      date: f.querySelector('[name="date"]').value,
      reference: text(f.querySelector('[name="reference"]').value),
      description: text(f.querySelector('[name="doc_description"]').value),
      lines, post,
      ...(spec.note
        ? { original_invoice_id: text(f.querySelector('[name="original_invoice_id"]').value) }
        : { due_date: text(f.querySelector('[name="due_date"]').value) }),
      ...(f.querySelector('[name="currency"]').value !== base
        ? { currency: f.querySelector('[name="currency"]').value, exchange_rate: text(f.querySelector('[name="exchange_rate"]').value) }
        : {}),
    };
    const result = await (doc ? write("PUT", `${ledgerUrl()}/${segment}/${doc.id}`, body, doc.version) : write("POST", `${ledgerUrl()}/${segment}`, body));
    saveThenOpen(segment, result.data.id);
  } catch (error) {
    recordError(error);
  }
}

async function deleteDocumentRecord(segment, id, version, no) {
  if (!confirm(`Delete ${no}?${version ? " Its journal is removed too." : ""}`)) return;
  try {
    await write("DELETE", `${ledgerUrl()}/${segment}/${id}`, undefined, version);
    leaveRecord();
  } catch (error) {
    showError(error);
  }
}

// ---- Payments and receipts ----

async function renderPaymentRecord(route) {
  const receipt = route.segment === "receipts";
  const path = route.segment;
  if (route.id === "new") {
    await paymentRecordForm(path, receipt, null);
    return;
  }
  const docs = await api(`${ledgerUrl()}/${path}`);
  const doc = docs.find((d) => d.id === route.id);
  if (!doc) throw new Error("That payment was not found.");
  const [parties, banks] = await Promise.all([api(`${ledgerUrl()}/parties`), api(`${ledgerUrl()}/bank-accounts`)]);
  state._parties = parties;
  const party = parties.find((p) => p.id === doc.party_id)?.name ?? "";
  const bank = banks.find((b) => b.account_id === doc.bank_account_id);
  showRecordShell({
    title: `${receipt ? "Receipt" : "Payment"} ${doc.payment_no}`,
    status: doc.status,
    subtitle: `${accountName(doc.bank_account_id)} · ${doc.date} · ${esc(party)} · ${esc(doc.reference ?? "")}`,
    actions: [
      doc.journal_id ? `<button type="button" class="secondary small" onclick="openJournal('${doc.journal_id}')">Journal ${doc.journal_no ?? ""}</button>` : "",
      canEdit() ? `<button type="button" class="secondary small" onclick="paymentRecordForm('${path}', ${receipt}, '${doc.id}')">Edit</button>` : "",
      canEdit() ? `<button type="button" class="danger small" onclick="deletePaymentRecord('${path}', '${doc.id}', ${doc.version}, '${doc.payment_no}')">Delete</button>` : "",
    ].join(" "),
    body: `<table><tr><th>Category</th><th>Description</th><th class="num">Amount</th><th class="num">VAT</th><th class="num">WHT</th><th class="num">Total</th></tr>
      ${doc.lines.map((l) => `<tr><td>${l.kind === "account" ? esc(accountName(l.account_id)) : `${esc(accountName(l.control_account_id))} · ${l.kind === "invoice" ? "settles an invoice" : "party balance"}`}</td>
        <td>${esc(l.description ?? "")}</td><td class="num">${l.amount}</td><td class="num">${blank(l.tax_amount)}</td>
        <td class="num">${blank(l.withholding_amount)}</td><td class="num">${l.total}</td></tr>`).join("")}
      <tr class="total"><td colspan="5">Total</td><td class="num">${doc.total} ${doc.currency ?? bank?.currency ?? ""}</td></tr></table>
      ${doc.issues.map((i) => `<div class="warning">${esc(i.code)}: ${esc(i.message)}</div>`).join("")}`,
    editing: false,
  });
}

async function paymentRecordForm(path, receipt, id) {
  if (state.recordDirty && !canLeaveRecord()) return;
  try {
    const doc = id ? (await api(`${ledgerUrl()}/${path}`)).find((d) => d.id === id) : null;
    const [banks, parties, accounts, taxCodes, sales, purchases] = await Promise.all([
      api(`${ledgerUrl()}/bank-accounts`), api(`${ledgerUrl()}/parties`), api(`${ledgerUrl()}/accounts`), api(`${ledgerUrl()}/tax-codes`),
      api(`${ledgerUrl()}/sales-invoices`), api(`${ledgerUrl()}/purchase-invoices`),
    ]);
    const vat = taxCodes.filter((t) => t.active && [receipt ? "vat_output" : "vat_input", "vat_zero_rated", "non_vat"].includes(t.kind));
    const wht = taxCodes.filter((t) => t.active && t.kind === "withholding");
    const lineAccounts = accounts.filter((a) => a.postable && a.active && !a.is_bank);
    const invoices = [...sales.map((i) => ({ ...i, label: `SI ${i.invoice_no}`, party: i.customer_id })), ...purchases.map((i) => ({ ...i, label: `PI ${i.invoice_no}`, party: i.supplier_id, gross_total: i.payable_total }))]
      .filter((i) => i.status === "posted" && i.type === "invoice");
    showRecordShell({
      title: doc ? `Edit ${receipt ? "receipt" : "payment"} ${doc.payment_no}` : `New ${receipt ? "receipt" : "payment"}`,
      status: doc?.status ?? "draft",
      subtitle: "The category is what the money was for: leave it empty to settle with the party, or choose an account for a direct payment or fee.",
      body: `<div class="grid">
          ${recordSelect(receipt ? "Received into" : "Paid from", "bank_account_id", banks.map((b) => ({ ...b, id: b.account_id })), doc?.bank_account_id, (b) => `${b.code} ${b.name} · ${b.currency}`, null, true)}
          ${recordSelect(receipt ? "Received from" : "Paid to", "party_id", parties.filter((p) => p.active), doc?.party_id, (p) => p.name, "No party", true)}
          ${recordField("Date", "date", doc?.date ?? today(), "date", "required")}
          ${recordField("Reference", "reference", doc?.reference)}
          <label data-rate>Exchange rate <input name="exchange_rate" inputmode="decimal" value="${esc(doc?.exchange_rate ?? "")}" placeholder="rate of the date"></label>
          <label class="wide">Description <input name="doc_description" value="${esc(doc?.description ?? "")}"></label>
        </div>
        <table class="lines"></table>
        <p><button type="button" class="secondary" data-add>Add line</button></p>`,
      editing: true,
      footer: "Total 0.00",
    });
    const f = $("record-body");
    const invoiceOptions = (selected) => options(invoices.filter((i) => i.party === f.querySelector('[name="party_id"]').value),
      selected, (i) => `${i.label} · ${i.date} · ${i.gross_total}`, "Oldest first");
    const columns = [
      { name: "account_id", head: "Category", cell: (l) => `<select name="account_id">${options(lineAccounts, l?.account_id, (a) => `${a.code} ${a.name}`, "Settle with party")}</select>` },
      { name: "invoice_id", head: "Invoice", cell: (l) => `<select name="invoice_id">${invoiceOptions(l?.invoice_id)}</select>` },
      { name: "description", head: "Description", cell: (l) => `<input name="description" value="${esc(l?.description ?? "")}">` },
      { name: "amount", head: "Amount", num: true, cell: (l) => `<input name="amount" class="num" inputmode="decimal" value="${l?.amount ?? ""}">` },
      { name: "settled_amount", head: "Settles (invoice currency)", num: true, cell: (l) => `<input name="settled_amount" class="num" inputmode="decimal" value="${l?.settled_amount ?? ""}">` },
      { name: "tax_code_id", head: "VAT", cell: (l) => `<select name="tax_code_id">${options(vat, l?.tax_code_id, (t) => t.name, "None")}</select>` },
      { name: "withholding_code_id", head: "WHT", cell: (l) => `<select name="withholding_code_id">${options(wht, l?.withholding_code_id, (t) => t.name, "None")}</select>` },
    ];
    const totals = () => {
      let total = 0;
      for (const tr of grid.rows()) {
        const amount = num(value(tr, "amount"));
        total += amount + amount * rateOf(value(tr, "tax_code_id"), vat) - amount * rateOf(value(tr, "withholding_code_id"), wht);
      }
      const bank = banks.find((b) => b.account_id === f.querySelector('[name="bank_account_id"]').value);
      recordTotals(`<b>${receipt ? "Received" : "Paid"} ${money(total, bank?.currency)} ${bank?.currency ?? ""}</b> <span class="muted small">estimate</span>`);
    };
    const grid = lineGrid(f.querySelector("table.lines"), columns, totals);
    f.querySelector('[name="party_id"]').onchange = () => {
      for (const tr of grid.rows()) field(tr, "invoice_id").innerHTML = invoiceOptions(value(tr, "invoice_id"));
    };
    const showRate = () => f.querySelector("[data-rate]").classList.toggle("hidden",
      banks.find((b) => b.account_id === f.querySelector('[name="bank_account_id"]').value)?.currency === state.ledger.base_currency);
    f.querySelector('[name="bank_account_id"]').onchange = () => { showRate(); totals(); };
    showRate();
    f.querySelector("[data-add]").onclick = () => grid.add();
    for (const l of doc?.lines ?? [null]) grid.add(l);
    totals();
    wireRecordDirty();
    wireRecordFooter({
      onDraft: () => savePayment(false, grid, columns, path, receipt, doc, banks),
      onPost: () => savePayment(true, grid, columns, path, receipt, doc, banks),
      onCancel: () => (doc ? cancelRecordTo(path, doc.id) : leaveRecord()),
    });
  } catch (error) {
    showError(error);
  }
}

async function savePayment(post, grid, columns, path, receipt, doc, banks) {
  clearRecordError();
  try {
    const f = $("record-body");
    const text = (v) => (v.trim() === "" ? null : v.trim());
    const lines = grid.rows().map((tr) => Object.fromEntries(columns.map((col) => [col.name, value(tr, col.name)]))).filter((l) => l.amount);
    if (!lines.length) throw new Error("Add at least one line with an amount.");
    const foreign = banks.find((b) => b.account_id === f.querySelector('[name="bank_account_id"]').value)?.currency !== state.ledger.base_currency;
    const body = {
      bank_account_id: f.querySelector('[name="bank_account_id"]').value,
      party_id: text(f.querySelector('[name="party_id"]').value),
      date: f.querySelector('[name="date"]').value,
      reference: text(f.querySelector('[name="reference"]').value),
      description: text(f.querySelector('[name="doc_description"]').value),
      lines, post, ...(foreign ? { exchange_rate: text(f.querySelector('[name="exchange_rate"]').value) } : {}),
    };
    const result = await (doc ? write("PUT", `${ledgerUrl()}/${path}/${doc.id}`, body, doc.version) : write("POST", `${ledgerUrl()}/${path}`, body));
    saveThenOpen(path, result.data.id);
  } catch (error) {
    recordError(error);
  }
}

async function deletePaymentRecord(path, id, version, no) {
  if (!confirm(`Delete ${no}? Its journal is removed too.`)) return;
  try {
    await write("DELETE", `${ledgerUrl()}/${path}/${id}`, undefined, version);
    leaveRecord();
  } catch (error) {
    showError(error);
  }
}

// Reports keep their filters in the hash and open their rows in the same full-page surface.
async function renderReportRoute(route) {
  const tab = ({ tb: "tb", pl: "pl", bs: "bs", gl: "gl" })[route.id];
  if (!tab) return leaveRecord(false);
  // Leave the record surface without touching the report hash in the address bar.
  state.record = null;
  state.recordDirty = false;
  $("record-page").classList.add("hidden");
  $("home").classList.remove("hidden");
  const form = $(`${tab}-form`);
  if (form) {
    const q = route.query;
    const set = (name, value) => { if (value !== null && form.elements[name]) form.elements[name].value = value; };
    set("from", q.get("from"));
    set("to", q.get("to"));
    set("asOf", q.get("asOf"));
    set("account", q.get("accountId"));
    set("class", q.get("classId"));
    set("location", q.get("locationId"));
  }
  state.reportApplying = true;
  await showTab(tab, true);
  form?.requestSubmit();
  state.reportApplying = false;
}

// A submitted report form writes its filters to the address bar. pushState keeps Back as "previous filters"
// without firing hashchange, so the route is applied from the popstate handler below.
function syncReportUrl(tab, form) {
  if (!state.ledger || state.reportApplying) return;
  const query = new URLSearchParams();
  for (const [key, value] of new FormData(form)) {
    if (value === "" || value === null) continue;
    query.set(key === "account" ? "accountId" : key === "class" ? "classId" : key === "location" ? "locationId" : key, value);
  }
  history.pushState(null, "", `#/ledger/${state.ledger.id}/reports/${tab}?${query}`);
}

window.addEventListener("popstate", () => { applyRoute(); });
