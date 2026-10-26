// Report delivery settings: organization branding and per-ledger print defaults.
function reportField(label, name, value, type = "text") {
  return '<label>' + label + ' <input name="' + name + '" type="' + type + '" value="' + esc(value ?? "") + '"></label>';
}

async function renderReportSettings() {
  const page = $("tab-settings-branding");
  const [branding, settings] = await Promise.all([
    api("/api/v1/organizations/" + state.org + "/branding"),
    api(ledgerUrl() + "/report-settings"),
  ]);
  const brand = isAdmin()
    ? '<form id="branding-form" class="card"><h3>Organization branding</h3><div class="grid">' +
      reportField("Legal name", "legal_name", branding.legal_name) +
      reportField("Khmer legal name", "legal_name_km", branding.legal_name_km) +
      reportField("VATTIN", "vat_tin", branding.vat_tin) +
      reportField("Brand color", "brand_color", branding.brand_color) +
      reportField("Phone", "phone", branding.phone) +
      reportField("Email", "email", branding.email, "email") +
      reportField("Prepared by", "prepared_by", branding.prepared_by) +
      reportField("Reviewed by", "reviewed_by", branding.reviewed_by) +
      '<label class="wide">Address <input name="address" value="' + esc(branding.address ?? "") + '"></label>' +
      '<label class="wide">Footer <input name="footer" value="' + esc(branding.footer ?? "") + '"></label>' +
      '</div><p class="actions"><button type="submit">Save branding</button></p></form>'
    : '<div class="card"><h3>Organization branding</h3><p class="muted">Only an organization administrator can change branding.</p></div>';

  page.innerHTML = '<p class="muted small">These settings feed client report packs, tax workpapers and print layouts. Ledger settings override the organization defaults for output only.</p>' +
    '<p><a class="button" href="#" id="client-pack-pdf">Client pack PDF</a> <a class="button" href="#" id="client-pack-xlsx">Excel pack</a> <a class="button" href="#" id="client-pack-share">Share pack</a> <span class="muted small">Current year to date · P&L, balance sheet, trial balance and aging</span></p>' +
    brand +
    '<form id="report-output-form" class="card"><h3>Ledger output</h3><div class="grid">' +
    '<label>Language <select name="language">' + ["en", "km", "bilingual"].map((x) => '<option ' + (settings.language === x ? "selected" : "") + '>' + x + '</option>').join("") + '</select></label>' +
    '<label>Comparative <select name="comparative">' + ["none", "prior_period", "year_start", "prior_year"].map((x) => '<option ' + (settings.comparative === x ? "selected" : "") + '>' + x + '</option>').join("") + '</select></label>' +
    '<label>Paper <select name="paper_size">' + ["A4", "Letter"].map((x) => '<option ' + (settings.paper_size === x ? "selected" : "") + '>' + x + '</option>').join("") + '</select></label>' +
    '<label>Orientation <select name="orientation">' + ["portrait", "landscape"].map((x) => '<option ' + (settings.orientation === x ? "selected" : "") + '>' + x + '</option>').join("") + '</select></label>' +
    '<label>Watermark <select name="watermark">' + ["none", "draft", "final"].map((x) => '<option ' + (settings.watermark === x ? "selected" : "") + '>' + x + '</option>').join("") + '</select></label>' +
    '<label>Share link days <input name="share_days" type="number" min="0" max="365" value="' + settings.share_days + '"></label>' +
    '<label class="check"><input type="checkbox" name="show_zero_balances" ' + (settings.show_zero_balances ? "checked" : "") + '> Show zero balances</label>' +
    '<label class="check"><input type="checkbox" name="show_account_codes" ' + (settings.show_account_codes ? "checked" : "") + '> Show account codes</label>' +
    '<label class="wide">Pack sections <input name="pack_sections" value="' + esc(settings.pack_sections) + '" placeholder="pl,bs,tb,gl,aging"></label>' +
    '</div><p class="actions"><span class="spacer"></span><button type="submit">Save output settings</button></p></form>';

  const brandForm = $("branding-form");
  if (brandForm) brandForm.onsubmit = (event) => {
    event.preventDefault();
    busy(brandForm, async () => {
      const f = brandForm.elements;
      const body = {
        legal_name: f.legal_name.value.trim() || null, legal_name_km: f.legal_name_km.value.trim() || null,
        vat_tin: f.vat_tin.value.trim() || null, brand_color: f.brand_color.value.trim() || null,
        address: f.address.value.trim() || null, phone: f.phone.value.trim() || null, email: f.email.value.trim() || null,
        footer: f.footer.value.trim() || null, prepared_by: f.prepared_by.value.trim() || null, reviewed_by: f.reviewed_by.value.trim() || null,
      };
      await write("PUT", "/api/v1/organizations/" + state.org + "/branding", body, branding.version !== "0" ? branding.version : undefined);
      await refreshLedger();
    });
  };

  $("client-pack-pdf").onclick = (event) => {
    event.preventDefault();
    const to = today();
    download(ledgerUrl() + "/reports/client-pack.pdf?from=" + to.slice(0, 4) + "-01-01&to=" + to, "client-pack-" + to + ".pdf");
  };

  $("client-pack-xlsx").onclick = (event) => {
    event.preventDefault();
    const to = today();
    download(ledgerUrl() + "/reports/client-pack.xlsx?from=" + to.slice(0, 4) + "-01-01&to=" + to, "client-pack-" + to + ".xlsx");
  };

  // Generates the pack (which records a run) and hands out a read-only link that expires.
  $("client-pack-share").onclick = (event) => {
    event.preventDefault();
    const to = today();
    sharePack(ledgerUrl() + "/reports/client-pack.pdf?from=" + to.slice(0, 4) + "-01-01&to=" + to, settings.share_days);
  };

  $("report-output-form").onsubmit = (event) => {
    event.preventDefault();
    const form = event.target;
    busy(form, async () => {
      const f = form.elements;
      const body = {
        language: f.language.value, comparative: f.comparative.value, show_zero_balances: f.show_zero_balances.checked,
        show_account_codes: f.show_account_codes.checked, paper_size: f.paper_size.value, orientation: f.orientation.value,
        watermark: f.watermark.value, pack_sections: f.pack_sections.value.trim() || "pl,bs,tb,gl,aging", share_days: Number(f.share_days.value),
      };
      await write("PUT", ledgerUrl() + "/report-settings", body, settings.version !== "0" ? settings.version : undefined);
      await refreshLedger();
    });
  };
}

/** Generates a pack (which records a report run) and opens a read-only share link for it. */
async function sharePack(url, days) {
  try {
    const response = await fetch(url, { headers: { Authorization: "Bearer " + state.token } });
    if (!response.ok) throw new Error("The report could not be generated (HTTP " + response.status + ").");
    const runId = response.headers.get("X-Report-Run-Id");
    if (!runId) throw new Error("The server did not record a report run.");
    const share = (await write("POST", ledgerUrl() + "/report-runs/" + runId + "/shares", {
      days: Number(days) || 7, watermark: true,
    })).data;
    const link = location.origin + share.url;
    const copied = await navigator.clipboard?.writeText(link).then(() => true).catch(() => false);
    window.open(share.url, "_blank", "noopener");
    if (!copied) prompt("Share link", link);
  } catch (error) {
    showError(error);
  }
}
