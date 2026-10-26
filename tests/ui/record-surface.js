// Playwright smoke test for the full-page record surface (owner decision C+A).
//
// Run against a Development API with the local issuer:
//   node tests/ui/record-surface.js
// Env: BOOKS_BASE (default http://127.0.0.1:5180), BOOKS_CHROME (browser path),
//      BOOKS_TOKEN (skip the dev token), BOOKS_UI_SHOTS (screenshot directory).
//
// Requires playwright-core (see package.json). The test creates a fresh demo organization, so it never depends
// on data left by another run.

const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { chromium } = require("playwright-core");

const base = process.env.BOOKS_BASE ?? "http://127.0.0.1:5180";
const shots = process.env.BOOKS_UI_SHOTS;

function browserPath() {
  const candidates = [
    process.env.BOOKS_CHROME,
    path.join(os.homedir(), "AppData", "Local", "ms-playwright", "chromium-1243", "chrome-win64", "chrome.exe"),
    "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
    "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe",
    "/usr/bin/chromium",
    "/usr/bin/google-chrome",
  ].filter(Boolean);
  const found = candidates.find((candidate) => fs.existsSync(candidate));
  assert.ok(found, "Set BOOKS_CHROME to a Chromium-based browser.");
  return found;
}

async function token() {
  if (process.env.BOOKS_TOKEN) return process.env.BOOKS_TOKEN;
  const response = await fetch(`${base}/dev/token`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ subject: `ui-${Date.now()}`, email: "ui@example.test", name: "UI smoke" }),
  });
  assert.equal(response.status, 200, "The local issuer must be available (Development only).");
  return (await response.json()).access_token;
}

async function run() {
  const accessToken = await token();
  await fetch(`${base}/dev/demo`, { method: "POST", headers: { Authorization: `Bearer ${accessToken}` } });
  const browser = await chromium.launch({ executablePath: browserPath(), headless: true });
  const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
  const errors = [];
  page.on("dialog", (dialog) => dialog.accept());
  page.on("console", (m) => { if (m.type() === "error" && !m.text().includes("404")) errors.push(m.text()); });
  page.on("pageerror", (error) => errors.push(error.message));
  const shot = async (name) => {
    if (!shots) return;
    fs.mkdirSync(shots, { recursive: true });
    await page.screenshot({ path: path.join(shots, `${name}.png`) });
  };

  await page.addInitScript((value) => sessionStorage.setItem("books.token", value), accessToken);
  await page.goto(`${base}/app/`);
  await page.waitForSelector("#home:not(.hidden)", { timeout: 20000 });
  await page.waitForTimeout(1500);

  const nav = async (selector, rowSelector) => {
    await page.locator(selector).click();
    await page.waitForSelector(rowSelector, { timeout: 15000 });
    await page.waitForTimeout(400);
  };
  const openJournal = async () => page.evaluate(async () => {
    const response = await fetch(`/api/v1/organizations/${state.org}/ledgers/${state.ledger.id}/journals`, {
      headers: { Authorization: `Bearer ${sessionStorage.getItem("books.token")}` },
    });
    const journals = (await response.json()).data;
    const manual = journals.find((j) => !j.source_kind && j.status !== "deleted");
    location.hash = `#/ledger/${state.ledger.id}/journals/${manual.id}`;
  });

  // Journal: view, edit, cancel, deep link and Back.
  await nav('#sidenav button[data-tab="journals"]', "#tab-journals tr[data-id]");
  await openJournal();
  await page.waitForSelector("#record-page:not(.hidden)", { timeout: 15000 });
  assert.match(await page.locator("#record-title").innerText(), /^Journal \d+$/);
  assert.ok((await page.locator("#record-bar").getAttribute("class")).includes("hidden"));
  await shot("record-journal-view");

  await page.locator("#record-actions button", { hasText: "Edit" }).first().click();
  await page.waitForFunction(() => !document.getElementById("record-bar").classList.contains("hidden"));
  await page.locator('#record-body [name="reference"]').first().fill("UI-SMOKE");
  await page.locator("#record-cancel").click();
  await page.waitForFunction(() => document.getElementById("record-bar").classList.contains("hidden"));
  await shot("record-journal-edit");

  const hash = await page.evaluate(() => location.hash);
  await page.reload();
  await page.waitForSelector("#record-page:not(.hidden)", { timeout: 20000 });
  assert.equal(await page.evaluate(() => location.hash), hash, "The record address survives a reload.");
  await page.locator("#record-back").click();
  await page.waitForSelector("#home:not(.hidden)", { timeout: 15000 });
  assert.equal(await page.locator("#page-title").innerText(), "Journals");

  // Sales, purchase and payment open full page; an invoice's Edit uses the same surface.
  await nav('#sidenav button.module:has-text("Sales")', "#tab-sales-invoices tr[data-id]");
  await page.locator("#tab-sales-invoices tr[data-id]").first().click();
  await page.waitForSelector("#record-page:not(.hidden)", { timeout: 15000 });
  assert.match(await page.locator("#record-title").innerText(), /^Sales invoice \d+$/);
  await shot("record-sale-view");
  await page.locator('#record-actions button', { hasText: "Edit" }).first().click();
  await page.waitForFunction(() => !document.getElementById("record-bar").classList.contains("hidden"));
  await page.locator("#record-cancel").click();
  await page.waitForFunction(() => document.getElementById("record-bar").classList.contains("hidden"));
  await page.waitForTimeout(300);
  await page.locator("#record-back").click();
  await page.waitForSelector("#home:not(.hidden)");

  await nav('#sidenav button.module:has-text("Purchases")', "#tab-purchases-invoices tr[data-id]");
  await page.locator("#tab-purchases-invoices tr[data-id]").first().click();
  await page.waitForSelector("#record-page:not(.hidden)", { timeout: 15000 });
  assert.match(await page.locator("#record-title").innerText(), /^Purchase invoice \d+$/);
  await page.locator("#record-back").click();
  await page.waitForSelector("#home:not(.hidden)");

  // The old per-line "Tax invoice" checkbox is gone: deductibility is the VAT code's kind.
  await page.locator("#tab-purchases-invoices .new-doc").click();
  await page.waitForFunction(() => !document.getElementById("record-bar").classList.contains("hidden"));
  assert.equal(await page.locator('#record-body input[name="valid_tax_invoice"]').count(), 0);
  assert.ok(!(await page.locator("#record-body table.lines").innerText()).includes("Tax invoice"));
  await page.locator("#record-back").click();
  await page.waitForSelector("#home:not(.hidden)");

  const payment = await page.evaluate(async () => {
    const response = await fetch(`/api/v1/organizations/${state.org}/ledgers/${state.ledger.id}/payments`, {
      headers: { Authorization: `Bearer ${sessionStorage.getItem("books.token")}` },
    });
    return (await response.json()).data.find((p) => p.status !== "deleted");
  });
  if (payment) {
    await page.evaluate((id) => { location.hash = `#/ledger/${state.ledger.id}/payments/${id}`; }, payment.id);
    await page.waitForSelector("#record-page:not(.hidden)", { timeout: 15000 });
    assert.match(await page.locator("#record-title").innerText(), /^Payment \d+$/);
    await shot("record-payment-view");
    await page.locator("#record-back").click();
    await page.waitForSelector("#home:not(.hidden)");
  }

  // Report filters live in the URL and survive a reload and Back.
  await nav('#sidenav button[data-tab="tb"]', "#tb-out table");
  await page.locator('#tb-form input[name="from"]').fill("2026-02-01");
  await page.locator("#tb-form").evaluate((form) => form.requestSubmit());
  await page.waitForFunction(() => location.hash.includes("from=2026-02-01"), null, { timeout: 15000 });
  await page.waitForTimeout(500);
  const reportHash = await page.evaluate(() => location.hash);
  await page.reload();
  await page.waitForFunction(() => !document.getElementById("home").classList.contains("hidden"), null, { timeout: 20000 });
  await page.waitForSelector("#tb-out table", { timeout: 15000 });
  assert.equal(await page.locator('#tb-form input[name="from"]').inputValue(), "2026-02-01");
  assert.equal(await page.evaluate(() => location.hash), reportHash);
  await page.locator('#tb-form input[name="from"]').fill("2026-03-01");
  await page.locator("#tb-form").evaluate((form) => form.requestSubmit());
  await page.waitForFunction(() => location.hash.includes("from=2026-03-01"), null, { timeout: 15000 });
  await page.goBack();
  await page.waitForFunction(() => location.hash.includes("from=2026-02-01"), null, { timeout: 15000 });
  assert.equal(await page.locator('#tb-form input[name="from"]').inputValue(), "2026-02-01");

  // H: organization settings list ledger roles, and a party keeps its Khmer details.
  await nav('#sidenav button[data-tab="settings-org"]', "#tab-settings-org table");
  assert.ok((await page.locator("#tab-settings-org").innerText()).includes("Members and ledger roles"));
  await nav('#sidenav button[data-tab="parties"]', "#tab-parties tr[data-party]");
  await page.locator("#tab-parties tr[data-party]").first().click();
  await page.waitForSelector("#form-dialog[open]", { timeout: 15000 });
  assert.equal(await page.locator('#form-dialog input[name="name_km"]').count(), 1);
  await page.locator("#form-dialog [data-cancel]").click();

  // New journal: the same header, grid and fixed bar; save and post, then delete.
  await nav('#sidenav button[data-tab="journals"]', "#tab-journals tr[data-id]");
  await page.locator("#tab-journals .new-doc").click();
  await page.waitForFunction(() => !document.getElementById("record-bar").classList.contains("hidden"));
  assert.equal(await page.locator("#record-title").innerText(), "New journal");
  const pair = await page.evaluate(() => state.accounts
    .filter((a) => a.postable && a.active && !a.is_control && !a.is_bank)
    .slice(0, 2).map((a) => a.id));
  await page.locator('#record-body table.lines select[name="account_id"]').nth(0).selectOption(pair[0]);
  await page.locator('#record-body table.lines select[name="account_id"]').nth(1).selectOption(pair[1]);
  await page.locator('#record-body table.lines input[name="debit"]').nth(0).fill("5.00");
  await page.locator('#record-body table.lines input[name="credit"]').nth(1).fill("5.00");
  await shot("record-journal-new");
  await page.locator("#record-post").click();
  await page.waitForFunction(() => !location.hash.endsWith("/new") && !document.getElementById("record-page").classList.contains("hidden"), null, { timeout: 15000 });
  await page.waitForFunction(() => document.querySelector("#record-status .pill")?.textContent === "posted", null, { timeout: 15000 });
  assert.equal(await page.locator("#record-status .pill").innerText(), "posted");
  await page.locator("#record-actions button", { hasText: "Delete" }).first().click();
  await page.waitForSelector("#home:not(.hidden)", { timeout: 15000 });

  assert.deepEqual(errors, [], `Page errors: ${errors.join(" | ")}`);
  await browser.close();
  console.log("record surface smoke: ok");
}

run().catch((error) => {
  console.error(error);
  process.exit(1);
});
