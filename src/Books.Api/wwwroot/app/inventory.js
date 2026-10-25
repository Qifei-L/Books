// Inventory tab: the stock report, items, and stock adjustments and counts (write-offs, damage, stock found).

async function renderInventory(asOf = state.inventoryAsOf ?? today()) {
  state.inventoryAsOf = asOf;
  try {
    const [report, items, adjustments, accounts, taxCodes] = await Promise.all([
      api(`${ledgerUrl()}/reports/inventory?asOf=${asOf}`), api(`${ledgerUrl()}/items`), api(`${ledgerUrl()}/stock-adjustments`),
      api(`${ledgerUrl()}/accounts`), api(`${ledgerUrl()}/tax-codes`),
    ]);
    const itemName = (id) => items.find((i) => i.id === id)?.name ?? "";
    const taxName = (id) => taxCodes.find((t) => t.id === id)?.name ?? "";
    const shown = adjustments.filter((a) => a.status !== "deleted");
    $("tab-inventory").innerHTML = `
      ${canEdit() ? `<p><button onclick="adjustmentForm()">Adjust stock</button> <button class="secondary" onclick="adjustmentForm(null, true)">Stock count</button>
        <button class="secondary" onclick="itemForm()">New item</button></p>` : ""}
      <h3>Stock on hand</h3>
      <form class="inline" id="inventory-form"><label>As of <input type="date" name="asOf" value="${asOf}"></label><button type="submit">Run</button></form>
      <p>${report.matches_control ? `<span class="ok">Equals the inventory account ${report.control_balance}</span>` : `<span class="error">Does not equal the inventory account ${report.control_balance}</span>`}</p>
      <table><tr><th>Item</th><th>Unit</th><th class="num">In</th><th class="num">Out</th><th class="num">On hand</th><th class="num">Average cost</th><th class="num">Value</th></tr>
      ${report.items.map((r) => `<tr><td>${esc(r.name)}</td><td>${esc(r.unit ?? "")}</td><td class="num">${r.quantity_in}</td><td class="num">${r.quantity_out}</td>
        <td class="num">${r.on_hand}</td><td class="num">${r.average_cost}</td><td class="num">${r.value}</td></tr>`).join("")}
      <tr class="total"><td colspan="6">Total ${report.currency}</td><td class="num">${report.total_value}</td></tr></table>
      <h3 style="margin-top:16px">Stock adjustments and counts</h3>
      <table><tr><th>No.</th><th>Date</th><th>Reference</th><th>Lines</th><th class="num">Value</th><th>Status</th></tr>
      ${shown.map((a) => `<tr class="click" data-adjustment="${a.id}"><td>SA ${a.adjustment_no}</td><td>${a.date}</td><td>${esc(a.reference ?? "")}</td>
        <td>${a.lines.map((l) => `${esc(itemName(l.item_id))} ${Number(l.quantity) > 0 ? "+" : ""}${l.quantity}`).join("; ")}</td><td class="num">${a.total_value}</td>
        <td>${a.status === "posted" ? `<span class="pill posted">posted</span> No. ${a.journal_no}` : `<span class="pill draft">${a.status}</span> ${a.issues.map((i) => esc(i.message)).join(" ")}`}</td></tr>`).join("")}</table>
      <h3 style="margin-top:16px">Items</h3>
      <table><tr><th>Code</th><th>Item</th><th>Unit</th><th class="num">Sales price</th><th class="num">Purchase price</th><th>Inventory</th><th>Suggests account</th><th>Suggests tax</th></tr>
      ${items.map((i) => `<tr ${canEdit() ? `class="click" data-item="${i.id}"` : ""}><td>${esc(i.code ?? "")}</td><td>${esc(i.name)}${i.active ? "" : ` <span class="pill">inactive</span>`}</td><td>${esc(i.unit ?? "")}</td>
        <td class="num">${i.sales_price ?? ""}</td><td class="num">${i.purchase_price ?? ""}</td>
        <td>${i.track_inventory ? "tracked" : ""}</td><td>${i.default_account_id ? esc(accountName(i.default_account_id)) : ""}</td><td>${esc(taxName(i.default_tax_code_id))}</td></tr>`).join("")}</table>`;
    $("inventory-form").onsubmit = (e) => { e.preventDefault(); renderInventory(e.target.asOf.value); };
    for (const tr of $("tab-inventory").querySelectorAll("tr[data-adjustment]")) {
      tr.onclick = () => showAdjustment(shown.find((a) => a.id === tr.dataset.adjustment), itemName);
    }
    for (const tr of $("tab-inventory").querySelectorAll("tr[data-item]")) tr.onclick = () => itemForm(items.find((i) => i.id === tr.dataset.item), accounts, taxCodes);
  } catch (error) {
    $("tab-inventory").innerHTML = `<p class="muted">${esc(error.message)}</p>`;
  }
}

function showAdjustment(a, itemName) {
  $("journal-body").innerHTML = `<h3>Stock adjustment ${a.adjustment_no} <span class="pill ${a.status}">${a.status}</span></h3>
    <p class="muted">${a.date} · ${esc(a.reference ?? "")} · ${esc(a.description ?? "")}</p>
    <table><tr><th>Item</th><th class="num">Book</th><th class="num">Counted</th><th class="num">Change</th><th class="num">Unit cost</th><th class="num">Value</th><th>Account</th></tr>
    ${a.lines.map((l) => `<tr><td>${esc(itemName(l.item_id))}${l.description ? ` · ${esc(l.description)}` : ""}</td><td class="num">${l.book_quantity}</td>
      <td class="num">${l.counted_quantity ?? ""}</td><td class="num">${l.quantity}</td><td class="num">${l.unit_cost ?? ""}</td><td class="num">${l.value}</td>
      <td>${esc(accountName(l.account_id))}</td></tr>`).join("")}
    <tr class="total"><td colspan="5">Total</td><td class="num">${a.total_value}</td><td></td></tr></table>
    ${a.issues.map((i) => `<div class="warning">${esc(i.code)}: ${esc(i.message)}</div>`).join("")}${journalButton(a)}
    ${canEdit() ? `<p><button class="secondary" onclick="editAdjustment('${a.id}')">Edit</button></p>` : ""}`;
  $("journal-dialog").showModal();
}

async function editAdjustment(id) {
  $("journal-dialog").close();
  const a = (await api(`${ledgerUrl()}/stock-adjustments`)).find((x) => x.id === id);
  if (a) adjustmentForm(a);
}

/**
 * Write-offs and counts in one form. A line gives either the counted quantity (Books takes the difference from the
 * book quantity on the date) or the change itself. A count starts with every inventory item listed; lines left
 * empty, or counted at the book quantity, are not saved.
 */
async function adjustmentForm(doc, count = false) {
  try {
    const [items, accounts] = await Promise.all([api(`${ledgerUrl()}/items`), api(`${ledgerUrl()}/accounts`)]);
    const tracked = items.filter((i) => i.track_inventory && i.active);
    const others = accounts.filter((a) => a.postable && a.active && !a.is_control && !a.is_bank && !a.usages.includes("inventory"));
    const title = doc ? `Edit stock adjustment ${doc.adjustment_no}` : count ? "Stock count" : "Adjust stock";
    const form = openForm(title, `
      <div class="grid">
        <label>Date <input type="date" name="date" value="${doc?.date ?? today()}" required></label>
        <label>Reference <input name="reference" value="${esc(doc?.reference ?? "")}"></label>
        <label class="wide">Description <input name="description" value="${esc(doc?.description ?? "")}" placeholder="${count ? "Month-end count" : "Damaged, expired, found…"}"></label>
      </div>
      <p class="muted small">Give the counted quantity, or the change (negative writes off). Stock going out is valued at the average cost; stock coming in at the unit cost, or the average cost if left empty. The account defaults to cost of sales.</p>
      <table class="lines"></table>
      <p><button type="button" class="secondary" data-add>Add line</button></p>
      <p class="actions">
        ${doc ? `<button type="button" class="danger" data-delete>Delete</button>` : ""}
        <span class="spacer"></span>
        <button type="button" class="secondary" data-cancel>Cancel</button>
        <button type="submit" class="secondary" data-post="false">Save draft</button>
        <button type="submit" data-post="true">Save and post</button>
      </p>`);
    let onHand = {};
    const columns = [
      { name: "item_id", head: "Item", cell: (l) => `<select name="item_id">${options(tracked, l?.item_id, (i) => i.name, "Choose")}</select>` },
      { name: "on_hand", head: "On hand", num: true, cell: () => `<span data-on-hand></span>` },
      { name: "counted_quantity", head: "Counted", num: true, cell: (l) => `<input name="counted_quantity" class="num" inputmode="decimal" value="${l?.counted_quantity ?? ""}">` },
      { name: "quantity", head: "Change", num: true, cell: (l) => `<input name="quantity" class="num" inputmode="decimal" value="${l?.counted_quantity == null ? l?.quantity ?? "" : ""}">` },
      { name: "unit_cost", head: "Unit cost", num: true, cell: (l) => `<input name="unit_cost" class="num" inputmode="decimal" value="${l?.unit_cost ?? ""}">` },
      { name: "account_id", head: "Account", cell: (l) => `<select name="account_id">${options(others, l?.account_id, (a) => `${a.code} ${a.name}`, "Cost of sales")}</select>` },
      { name: "description", head: "Note", cell: (l) => `<input name="description" value="${esc(l?.description ?? "")}">` },
    ];
    const showOnHand = () => {
      for (const tr of grid.rows()) {
        const row = onHand[value(tr, "item_id")];
        tr.querySelector("[data-on-hand]").textContent = row ? row.on_hand : "";
        field(tr, "quantity").disabled = value(tr, "counted_quantity") !== null;
        field(tr, "counted_quantity").disabled = value(tr, "quantity") !== null;
      }
    };
    // Book quantities on the date, for the person counting; the server works out the change itself when saving.
    const loadOnHand = async () => {
      const report = await api(`${ledgerUrl()}/reports/inventory?asOf=${form.elements.date.value}`);
      onHand = Object.fromEntries(report.items.map((r) => [r.item_id, r]));
      showOnHand();
    };
    const grid = lineGrid(form.querySelector("table.lines"), columns, showOnHand);
    form.elements.date.onchange = loadOnHand;
    form.querySelector("[data-add]").onclick = () => { grid.add(); showOnHand(); };
    for (const l of doc?.lines ?? (count ? tracked.map((i) => ({ item_id: i.id })) : [null])) grid.add(l);
    await loadOnHand();

    const url = `${ledgerUrl()}/stock-adjustments`;
    onSave(form, async (post) => {
      const f = form.elements;
      const text = (v) => (v.trim() === "" ? null : v.trim());
      const lines = grid.rows().map((tr) => ({
        item_id: value(tr, "item_id"), counted_quantity: value(tr, "counted_quantity"), quantity: value(tr, "quantity"),
        unit_cost: value(tr, "unit_cost"), account_id: value(tr, "account_id"), description: value(tr, "description"),
      })).filter((l) => l.item_id && (l.quantity !== null || l.counted_quantity !== null))
        .filter((l) => l.counted_quantity === null || Number(l.counted_quantity) !== Number(onHand[l.item_id]?.on_hand ?? 0));
      if (!lines.length) throw new Error("Nothing to adjust: every counted quantity equals the book quantity.");
      const body = { date: f.date.value, reference: text(f.reference.value), description: text(f.description.value), lines, post };
      const result = await (doc ? write("PUT", `${url}/${doc.id}`, body, doc.version) : write("POST", url, body));
      $("form-dialog").close();
      await refreshLedger();
      const items2 = await api(`${ledgerUrl()}/items`);
      showAdjustment(result.data, (id) => items2.find((i) => i.id === id)?.name ?? "");
    });
    onDelete(form, doc, `stock adjustment ${doc?.adjustment_no}`, `${url}/${doc?.id}`, doc?.status === "posted" ? " Its journal is removed too." : "");
  } catch (error) {
    showError(error);
  }
}

async function itemForm(item, accounts, taxCodes) {
  try {
    [accounts, taxCodes] = accounts ? [accounts, taxCodes] : await Promise.all([api(`${ledgerUrl()}/accounts`), api(`${ledgerUrl()}/tax-codes`)]);
    const lineAccounts = accounts.filter((a) => a.postable && a.active && !a.is_control && !a.is_bank);
    const vat = taxCodes.filter((t) => t.active && ["vat_output", "vat_input", "vat_zero_rated", "non_vat"].includes(t.kind));
    const of = (type, usage) => accounts.filter((a) => a.postable && a.active && a.type === type && (!usage || a.usages.includes(usage)));
    const used = item?.used ?? false;
    const form = openForm(item ? `Edit ${item.name}` : "New item", `
      <div class="grid">
        <label>Code <input name="code" value="${esc(item?.code ?? "")}" placeholder="SKU"></label>
        <label class="wide">Name <input name="name" value="${esc(item?.name ?? "")}" required></label>
        <label>Unit <input name="unit" value="${esc(item?.unit ?? "")}" placeholder="case, kg, pcs"></label>
        <label class="check"><input type="checkbox" name="track_inventory" ${item?.track_inventory ? "checked" : ""}> Track inventory</label>
        <label class="wide">Suggests account <select name="default_account_id">${options(lineAccounts, item?.default_account_id, (a) => `${a.code} ${a.name}`, "None")}</select></label>
        <label class="wide">Suggests tax <select name="default_tax_code_id">${options(vat, item?.default_tax_code_id, (t) => t.name, "None")}</select></label>
        <label class="wide">Description <input name="description" value="${esc(item?.description ?? "")}"></label>
      </div>
      <h4>Sales</h4>
      <div class="grid">
        <label>Price (${state.ledger.base_currency}) <input name="sales_price" class="num" inputmode="decimal" value="${item?.sales_price ?? ""}"></label>
        <label class="wide">Tax <select name="sales_tax_code_id">${options(vat.filter((t) => t.kind !== "vat_input"), item?.sales_tax_code_id, (t) => t.name, "None")}</select></label>
        <label class="wide">Text on sales <input name="sales_description" value="${esc(item?.sales_description ?? "")}"></label>
      </div>
      <h4>Purchases</h4>
      <div class="grid">
        <label>Price (${state.ledger.base_currency}) <input name="purchase_price" class="num" inputmode="decimal" value="${item?.purchase_price ?? ""}"></label>
        <label class="wide">Tax <select name="purchase_tax_code_id">${options(vat.filter((t) => t.kind !== "vat_output"), item?.purchase_tax_code_id, (t) => t.name, "None")}</select></label>
        <label class="wide">Text on purchases <input name="purchase_description" value="${esc(item?.purchase_description ?? "")}"></label>
      </div>
      <div class="grid">
        ${item ? `<label class="check"><input type="checkbox" name="active" ${item.active ? "checked" : ""}> Active</label>` : ""}
      </div>
      <div class="grid" data-inventory>
        <label class="wide">Inventory account (asset) <select name="inventory_account_id" ${used ? "disabled" : ""}>${options(of("assets", "inventory"), item?.inventory_account_id, (a) => `${a.code} ${a.name}`, "Ledger's inventory account")}</select></label>
        <label class="wide">Income account <select name="income_account_id">${options(of("income"), item?.income_account_id, (a) => `${a.code} ${a.name}`, "Account on each sales line")}</select></label>
        <label class="wide">Cost of sales account (expense) <select name="cost_of_sales_account_id">${options(of("cost_and_expense"), item?.cost_of_sales_account_id, (a) => `${a.code} ${a.name}`, "Ledger's cost of sales account")}</select></label>
      </div>
      <p class="muted small">A tracked item is bought into its inventory account, sold into its income account at its average cost, and the cost goes to its cost of sales account; purchases and sales of it need a quantity.
        ${used ? "This item has postings, so its stock stays in its inventory account and tracking cannot be turned off; income and cost of sales accounts apply to documents saved from now on." : ""}</p>
      <p class="actions"><span class="spacer"></span>
        <button type="button" class="secondary" data-cancel>Cancel</button>
        <button type="submit">Save</button>
      </p>`);
    const showAccounts = () => form.querySelector("[data-inventory]").classList.toggle("hidden", !form.elements.track_inventory.checked);
    form.elements.track_inventory.onchange = showAccounts;
    form.elements.track_inventory.disabled = used;
    showAccounts();
    form.onsubmit = (event) => {
      event.preventDefault();
      busy(form, async () => {
        const f = form.elements;
        const text = (v) => (v.trim() === "" ? null : v.trim());
        if (!text(f.name.value)) throw new Error("Name is required.");
        const body = {
          name: f.name.value.trim(), unit: text(f.unit.value), description: text(f.description.value), default_account_id: text(f.default_account_id.value),
          default_tax_code_id: text(f.default_tax_code_id.value), track_inventory: f.track_inventory.checked, active: item ? f.active.checked : true,
          code: text(f.code.value), sales_price: text(f.sales_price.value), sales_tax_code_id: text(f.sales_tax_code_id.value), sales_description: text(f.sales_description.value),
          purchase_price: text(f.purchase_price.value), purchase_tax_code_id: text(f.purchase_tax_code_id.value), purchase_description: text(f.purchase_description.value),
          ...(f.track_inventory.checked ? {
            inventory_account_id: text(f.inventory_account_id.value), income_account_id: text(f.income_account_id.value),
            cost_of_sales_account_id: text(f.cost_of_sales_account_id.value),
          } : {}),
        };
        const url = `${ledgerUrl()}/items`;
        await (item ? write("PUT", `${url}/${item.id}`, body, item.version) : write("POST", url, body));
        $("form-dialog").close();
        await refreshLedger();
      });
    };
  } catch (error) {
    showError(error);
  }
}
