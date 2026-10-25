// Inventory master data and the stock-by-location report: locations, and where the stock is. The three
// inventory documents (adjustments, transfers, assemblies) have their own list pages.
async function renderStock(asOf) {
  const box = $("tab-inventory-stock-extra");
  try {
    const [locations, stock] = await Promise.all([
      api(`${ledgerUrl()}/locations`), api(`${ledgerUrl()}/reports/inventory/locations?asOf=${asOf}`),
    ]);
    box.innerHTML = `
      <h3 style="margin-top:16px">Locations ${canEdit() ? `<button class="secondary small" onclick="locationForm()">New</button>` : ""}</h3>
      ${locations.length ? `<table><tr><th>Name</th><th>Code</th><th>Default</th><th>Active</th></tr>
        ${locations.map((l) => `<tr ${canEdit() ? `class="click" data-location="${l.id}"` : ""}><td>${esc(l.name)}</td><td>${esc(l.code ?? "")}</td><td>${l.is_default ? "default" : ""}</td><td>${l.active ? "" : "inactive"}</td></tr>`).join("")}</table>
        <h3 style="margin-top:16px">Stock by location</h3>
        <table><tr><th>Item</th><th>Location</th><th class="num">Quantity</th></tr>
        ${stock.rows.map((r) => `<tr><td>${esc(r.item)}</td><td>${esc(r.location ?? "(none)")}</td><td class="num ${Number(r.quantity) < 0 ? "error" : ""}">${r.quantity} ${esc(r.unit ?? "")}</td></tr>`).join("")}</table>`
        : `<p class="muted small">No locations: all stock is in one place. Add locations to keep stock per warehouse or shop; the first becomes the default.</p>`}`;
    for (const tr of box.querySelectorAll("tr[data-location]")) tr.onclick = () => locationForm(locations.find((l) => l.id === tr.dataset.location));
  } catch (error) {
    box.innerHTML = `<p class="error">${esc(error.message)}</p>`;
  }
}

function locationForm(l) {
  simpleForm(l ? `Edit ${l.name}` : "New location", `
    <label class="wide">Name <input name="name" value="${esc(l?.name ?? "")}" required></label>
    <label>Code <input name="code" value="${esc(l?.code ?? "")}"></label>
    <label class="check"><input type="checkbox" name="is_default" ${l?.is_default ? "checked" : ""}> Default</label>
    ${l ? `<label class="check"><input type="checkbox" name="active" ${l.active ? "checked" : ""}> Active</label>` : ""}`,
  async (f) => {
    const body = { name: f.name.value.trim(), code: orNull(f.code.value), is_default: f.is_default.checked, active: l ? f.active.checked : true };
    await (l ? write("PUT", `${ledgerUrl()}/locations/${l.id}`, body, l.version) : write("POST", `${ledgerUrl()}/locations`, body));
  });
}

async function transferStockForm() {
  const [locations, items] = await Promise.all([api(`${ledgerUrl()}/locations`), api(`${ledgerUrl()}/items`)]);
  const active = locations.filter((l) => l.active);
  const tracked = items.filter((i) => i.track_inventory && i.active);
  simpleForm("Transfer stock", `
    <label>Date <input type="date" name="date" value="${today()}"></label>
    <label class="wide">From <select name="from">${options(active, active.find((l) => l.is_default)?.id, (l) => l.name)}</select></label>
    <label class="wide">To <select name="to">${options(active, active.find((l) => !l.is_default)?.id, (l) => l.name)}</select></label>
    <label class="wide">Item <select name="item">${options(tracked, null, (i) => i.name, "Choose")}</select></label>
    <label>Quantity <input name="quantity" class="num" inputmode="decimal"></label>
    <label>Reference <input name="reference"></label>`,
  async (f) => {
    if (!f.item.value) throw new Error("Choose an item.");
    await write("POST", `${ledgerUrl()}/stock-transfers`, {
      date: f.date.value, from_location_id: f.from.value, to_location_id: f.to.value, reference: orNull(f.reference.value),
      lines: [{ item_id: f.item.value, quantity: f.quantity.value.trim() }],
    });
  }, `<p class="muted small">Moves quantity only: cost is one average per item across locations, so no journal is posted.</p>`);
}

async function assemblyForm() {
  const [items, accounts] = await Promise.all([api(`${ledgerUrl()}/items`), api(`${ledgerUrl()}/accounts`)]);
  const buildable = items.filter((i) => i.track_inventory && i.active && (i.components ?? []).length);
  const expense = accounts.filter((a) => a.type === "cost_and_expense" && a.postable && a.active && !a.is_control);
  simpleForm("Make (assembly)", `
    <label>Date <input type="date" name="date" value="${today()}"></label>
    <label class="wide">Item <select name="item">${options(buildable, null, (i) => i.name, buildable.length ? "Choose" : "No item has a bill of materials yet")}</select></label>
    <label>Quantity <input name="quantity" class="num" inputmode="decimal"></label>
    <label>Labour <input name="labour" class="num" inputmode="decimal" placeholder="0.00"></label>
    <label class="wide">Labour account <select name="labour_account">${options(expense, null, (a) => `${a.code} ${a.name}`, "None")}</select></label>
    <label>Reference <input name="reference"></label>`,
  async (f) => {
    if (!f.item.value) throw new Error("Choose an item with a bill of materials.");
    const r = await write("POST", `${ledgerUrl()}/assemblies`, {
      date: f.date.value, item_id: f.item.value, quantity: f.quantity.value.trim(), labour: orNull(f.labour.value),
      labour_account_id: orNull(f.labour_account.value), reference: orNull(f.reference.value),
    });
    if (r.data.warnings?.length) showError(new Error(r.data.warnings.join(" ")));
  }, `<p class="muted small">Set an item's bill of materials through the API or AI (components: item and quantity per unit).</p>`);
}

function showStockTransfer(t, itemName, place) {
  $("journal-body").innerHTML = `<h3>Stock transfer ${t.transfer_no} <span class="pill ${t.status}">${t.status}</span></h3>
    <p class="muted">${t.date} · ${esc(place(t.from_location_id))} → ${esc(place(t.to_location_id))} · ${esc(t.reference ?? "")} · ${esc(t.description ?? "")}</p>
    <table><tr><th>Item</th><th class="num">Quantity</th><th>Note</th></tr>
    ${(t.lines ?? []).map((l) => `<tr><td>${esc(itemName(l.item_id))}</td><td class="num">${quantityText(l.quantity)}</td><td>${esc(l.description ?? "")}</td></tr>`).join("")}</table>
    <p class="muted small">Quantity only: cost is one average per item across locations, so no journal is posted.</p>
    ${canEdit() ? `<p><button class="danger" onclick="deleteStock('stock-transfers', '${t.id}', '${t.version}')">Delete</button></p>` : ""}`;
  showDialog();
}

function showAssembly(a, itemName) {
  $("journal-body").innerHTML = `<h3>Assembly ${a.assembly_no} <span class="pill ${a.status}">${a.status}</span></h3>
    <p class="muted">${a.date} · made ${quantityText(a.quantity)} × ${esc(itemName(a.item_id))} · ${esc(a.reference ?? "")} · ${esc(a.description ?? "")}</p>
    <table><tr><th>Component</th><th class="num">Quantity</th><th class="num">Value</th></tr>
    ${(a.components ?? []).map((c) => `<tr><td>${esc(itemName(c.component_id))}</td><td class="num">${quantityText(c.quantity)}</td><td class="num">${c.value}</td></tr>`).join("")}
    <tr class="subtotal"><td>Labour</td><td></td><td class="num">${a.labour}</td></tr>
    <tr class="total"><td>Value / unit cost</td><td class="num">${quantityText(a.quantity)}</td><td class="num">${a.value} · ${a.unit_cost}</td></tr></table>
    ${warningsHtml(a)}${journalButton(a)}
    ${canEdit() ? `<p><button class="danger" onclick="deleteStock('assemblies', '${a.id}', '${a.version}')">Delete</button></p>` : ""}`;
  showDialog();
}

async function deleteStock(path, id, version) {
  if (!confirm("Delete it? Any journal it posted is removed too.")) return;
  try {
    await write("DELETE", `${ledgerUrl()}/${path}/${id}`, undefined, version);
    if ($("journal-dialog").open) $("journal-dialog").close();
    await refreshLedger();
  } catch (error) {
    showError(error);
  }
}
