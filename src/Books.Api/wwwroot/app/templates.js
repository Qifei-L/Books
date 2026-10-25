// Report templates: versioned section order and on/off switches for client packs and tax workpapers.
// Defaults come from the server; a ledger publishes overrides that apply from a date.
function templateLabel(kind) {
  return (kind ?? "").replaceAll("_", " ").replace(/^./, (c) => c.toUpperCase());
}

function templateSectionsHtml(spec) {
  return (spec?.sections ?? []).map((s) =>
    `<li>${s.enabled ? "✓" : "—"} <b>${esc(s.key)}</b>${s.label ? ` · ${esc(s.label)}` : ""}${s.enabled ? "" : ' <span class="muted small">off</span>'}</li>`).join("");
}

function previewTemplate(effective, row) {
  const spec = row?.definition ?? effective.definition;
  const sample = effective.kind === "client_pack"
    ? `<p><a class="button" href="#" id="template-sample">Client pack PDF</a> <span class="muted small">uses the effective template</span></p>`
    : effective.kind === "tax_workpaper"
      ? `<p><a class="button" href="#" id="template-sample">Tax workpaper PDF</a> <span class="muted small">uses the effective template</span></p>`
      : "";
  $("journal-body").innerHTML = `<h3>${esc(spec.title)} <span class="pill">${esc(row?.status ?? effective.source)}</span></h3>
    <p class="muted">${esc(templateLabel(effective.kind))}${row ? ` · version ${row.version}${row.effective_from ? ` from ${row.effective_from}` : ""}` : " · code default"}</p>
    <ol class="template-sections">${templateSectionsHtml(spec)}</ol>${sample}`;
  showDialog();
  $("template-sample")?.addEventListener("click", (event) => {
    event.preventDefault();
    const to = today();
    download(effective.kind === "client_pack"
      ? `${ledgerUrl()}/reports/client-pack.pdf?from=${to.slice(0, 4)}-01-01&to=${to}`
      : `${ledgerUrl()}/tax-returns/${(state.taxPeriod ?? to.slice(0, 7))}.pdf`, `template-sample.pdf`);
  });
}

function templateForm(kind, effective, row) {
  const allowed = state.templateSections?.[kind] ?? [];
  const spec = row?.definition ?? effective.definition;
  const enabled = new Set((spec.sections ?? []).filter((s) => s.enabled).map((s) => s.key));
  const fields = `<div class="grid">
      <label class="wide">Name <input name="name" value="${esc(row?.name ?? spec.title ?? templateLabel(kind))}" required></label>
      <label>Effective from <input type="date" name="effective_from" value="${esc(row?.effective_from ?? today())}"></label>
      <label class="wide">Title on the report <input name="title" value="${esc(spec.title ?? templateLabel(kind))}" required></label>
    </div>
    <p class="muted small">Sections are drawn in this order; untick one to leave it out. A published version never changes.</p>
    <div class="template-order">${allowed.map((key, i) => `<label class="check"><input type="checkbox" name="section" value="${esc(key)}" ${enabled.has(key) ? "checked" : ""}> ${i + 1}. ${esc(key)}</label>`).join("")}</div>`;
  const form = openForm(row ? `Edit ${effective.kind} template v${row.version}` : `New ${templateLabel(kind)} version`, fields);
  form.onsubmit = (event) => {
    event.preventDefault();
    busy(form, async () => {
      const f = form.elements;
      const chosen = new Set([...form.querySelectorAll('input[name="section"]:checked')].map((x) => x.value));
      const definition = {
        title: f.title.value.trim(),
        sections: allowed.map((key) => ({ key, enabled: chosen.has(key) })),
      };
      const body = { kind, name: f.name.value.trim(), definition, effective_from: f.effective_from.value || null };
      if (!definition.title) throw new Error("Title is required.");
      const url = `${ledgerUrl()}/report-templates`;
      await (row ? write("PUT", `${url}/${row.id}`, body, row.version) : write("POST", url, body));
      $("form-dialog").close();
      state.rendered["settings-templates"] = false;
      await showTab("settings-templates", true);
    });
  };
}

async function renderReportTemplates() {
  const data = await api(`${ledgerUrl()}/report-templates`);
  state.templateSections = data.sections;
  const effective = new Map(data.effective.map((e) => [e.kind, e]));
  const draftOrPublished = data.templates;
  const rows = data.effective.map((e) => {
    const versions = draftOrPublished.filter((t) => t.kind === e.kind);
    const tone = e.source === "ledger" ? "posted" : "draft";
    return `<tr>
      <td><b>${esc(templateLabel(e.kind))}</b><div class="muted small">${esc(e.name)}</div></td>
      <td><span class="pill ${tone}">${esc(e.source)}</span>${e.version ? ` <span class="muted small">v${e.version}</span>` : ""}</td>
      <td>${(e.definition.sections ?? []).filter((s) => s.enabled).map((s) => esc(s.key)).join(" → ") || '<span class="muted">none</span>'}</td>
      <td class="actions-cell">
        <button type="button" class="secondary small" data-preview="${esc(e.kind)}">Preview</button>
        ${canEdit() ? `<button type="button" class="secondary small" data-new="${esc(e.kind)}">New version</button>` : ""}
      </td></tr>`;
  }).join("");
  const versionRows = draftOrPublished.length
    ? draftOrPublished.map((t) => `<tr>
        <td>${esc(templateLabel(t.kind))}</td><td>${esc(t.name)}</td>
        <td>v${t.version}</td><td><span class="pill ${t.status}">${esc(t.status)}</span></td>
        <td>${esc(t.effective_from ?? "earliest")}</td>
        <td class="actions-cell">
          <button type="button" class="secondary small" data-preview-row="${t.id}">Preview</button>
          ${canEdit() && t.status === "draft" ? `<button type="button" class="secondary small" data-edit="${t.id}">Edit</button>` : ""}
          ${isAdmin() && t.status === "draft" ? `<button type="button" class="small" data-publish="${t.id}">Publish</button>` : ""}
        </td></tr>`).join("")
    : `<tr><td colspan="6" class="muted">No ledger versions yet; the code defaults are in use.</td></tr>`;
  $("tab-settings-templates").innerHTML = `
    <p class="muted small">Client packs and tax workpapers read their section order and switches from the template effective for the period. Defaults are built into the code; a ledger can publish its own version from a date, and a published version never changes.</p>
    <h3>Effective now</h3>
    <table><tr><th>Template</th><th>Source</th><th>Sections</th><th></th></tr>${rows}</table>
    <h3 style="margin-top:20px">Versions</h3>
    <table><tr><th>Kind</th><th>Name</th><th>Version</th><th>Status</th><th>Effective from</th><th></th></tr>${versionRows}</table>`;
  for (const button of $("tab-settings-templates").querySelectorAll("[data-preview]")) {
    button.onclick = () => previewTemplate(effective.get(button.dataset.preview), null);
  }
  for (const button of $("tab-settings-templates").querySelectorAll("[data-preview-row]")) {
    const row = draftOrPublished.find((t) => t.id === button.dataset.previewRow);
    button.onclick = () => previewTemplate(effective.get(row.kind), row);
  }
  for (const button of $("tab-settings-templates").querySelectorAll("[data-new]")) {
    const kind = button.dataset.new;
    button.onclick = () => templateForm(kind, effective.get(kind), null);
  }
  for (const button of $("tab-settings-templates").querySelectorAll("[data-edit]")) {
    const row = draftOrPublished.find((t) => t.id === button.dataset.edit);
    button.onclick = () => templateForm(row.kind, effective.get(row.kind), row);
  }
  for (const button of $("tab-settings-templates").querySelectorAll("[data-publish]")) {
    const row = draftOrPublished.find((t) => t.id === button.dataset.publish);
    button.onclick = async () => {
      try {
        await write("POST", `${ledgerUrl()}/report-templates/${row.id}/publish`, undefined, row.version);
        state.rendered["settings-templates"] = false;
        await showTab("settings-templates", true);
      } catch (error) {
        showError(error);
      }
    };
  }
}
