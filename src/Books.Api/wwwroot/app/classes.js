// Classes are reporting segments carried by journal lines. They are deliberately separate from accounts: the same
// account can be reported by class, and a class never changes which account is posted.
async function renderClasses() {
  const classes = await api(ledgerUrl() + "/classes");
  const rows = classes.map((c) => '<tr ' + (canEdit() ? 'class="click" data-class="' + c.id + '"' : "") +
    '><td>' + esc(c.name) + '</td><td>' + esc(c.code ?? "") + '</td><td>' + (c.active ? "" : "inactive") + '</td></tr>').join("");
  $("tab-classes").innerHTML =
    '<p class="muted small">Classes are optional reporting dimensions on journal lines (for example a product line, department or business unit). They do not change the account or the balance.</p>' +
    '<p>' + (canEdit() ? '<button class="secondary" onclick="classForm()">New class</button>' : "") + '</p>' +
    (classes.length ? '<table><tr><th>Name</th><th>Code</th><th>Active</th></tr>' + rows + '</table>'
      : '<p class="muted small">No classes yet.</p>');
  for (const tr of $("tab-classes").querySelectorAll("tr[data-class]")) {
    tr.onclick = () => classForm(classes.find((c) => c.id === tr.dataset.class));
  }
}

function classForm(c) {
  const fields = '<label class="wide">Name <input name="name" value="' + esc(c ? c.name : "") + '" required></label>' +
    '<label>Code <input name="code" value="' + esc(c ? (c.code ?? "") : "") + '"></label>' +
    (c ? '<label class="check"><input type="checkbox" name="active" ' + (c.active ? "checked" : "") + '> Active</label>' : "");
  simpleForm(c ? "Edit " + c.name : "New class", fields, async (f) => {
    const body = { name: f.name.value.trim(), code: f.code.value.trim() || null, active: c ? f.active.checked : true };
    if (!body.name) throw new Error("Name is required.");
    await (c ? write("PUT", ledgerUrl() + "/classes/" + c.id, body, c.version) : write("POST", ledgerUrl() + "/classes", body));
  });
}
