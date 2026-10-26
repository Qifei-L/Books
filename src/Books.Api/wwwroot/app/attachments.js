// Files attached to a journal or document (M17). Read access downloads; edit access uploads and deletes.
// The bytes live on the server's file storage; the record keeps the file's name, hash and size.
function attachmentsPanel(kind, id) {
  return `<details class="files-block" data-files-kind="${esc(kind)}" data-files-id="${esc(id)}">
    <summary>Files</summary>
    <div class="files-body" data-files-body>Loading…</div>
  </details>`;
}

function wireAttachments(root) {
  for (const details of root.querySelectorAll("details[data-files-kind]")) {
    details.addEventListener("toggle", () => {
      if (details.open) loadAttachments(details);
    });
  }
}

async function loadAttachments(details) {
  const kind = details.dataset.filesKind;
  const id = details.dataset.filesId;
  const body = details.querySelector("[data-files-body]");
  try {
    const files = await api(`${ledgerUrl()}/attachments?recordKind=${encodeURIComponent(kind)}&recordId=${encodeURIComponent(id)}`);
    const rows = files.map((a) => `<tr>
        <td><a href="#" data-file-download="${a.id}" data-file-name="${esc(a.file_name)}">${esc(a.file_name)}</a></td>
        <td>${esc(a.content_type)}</td><td class="num">${fileSize(a.size)}</td>
        <td class="muted small">${esc(a.created_at.slice(0, 10))}</td>
        <td class="actions-cell">${canEdit() ? `<button type="button" class="link" data-file-delete="${a.id}">Delete</button>` : ""}</td>
      </tr>`).join("");
    body.innerHTML = `${files.length
      ? `<table><tr><th>File</th><th>Type</th><th class="num">Size</th><th>Added</th><th></th></tr>${rows}</table>`
      : '<p class="muted small">No files attached yet.</p>'}
      ${canEdit() ? `<p class="files-upload"><input type="file" data-files-input> <button type="button" class="secondary small" data-files-upload>Upload</button></p>` : ""}`;

    for (const link of body.querySelectorAll("[data-file-download]")) {
      link.onclick = (event) => {
        event.preventDefault();
        download(`${ledgerUrl()}/attachments/${link.dataset.fileDownload}/download`, link.dataset.fileName);
      };
    }
    for (const button of body.querySelectorAll("[data-file-delete]")) {
      button.onclick = async () => {
        if (!confirm("Delete this file?")) return;
        try {
          await write("DELETE", `${ledgerUrl()}/attachments/${button.dataset.fileDelete}`, null);
          await loadAttachments(details);
        } catch (error) {
          showError(error);
        }
      };
    }
    body.querySelector("[data-files-upload]")?.addEventListener("click", async () => {
      const input = body.querySelector("[data-files-input]");
      const file = input?.files?.[0];
      if (!file) return showError(new Error("Choose a file first."));
      const form = new FormData();
      form.append("record_kind", kind);
      form.append("record_id", id);
      form.append("file", file);
      try {
        const response = await fetch(`${ledgerUrl()}/attachments`, {
          method: "POST",
          headers: { Authorization: `Bearer ${state.token}`, "Idempotency-Key": crypto.randomUUID() },
          body: form,
        });
        const json = response.status === 204 ? null : await response.json();
        if (!response.ok) throw new Error(json?.detail ?? json?.title ?? `Upload failed (HTTP ${response.status}).`);
        await loadAttachments(details);
      } catch (error) {
        showError(error);
      }
    });
  } catch (error) {
    body.innerHTML = `<p class="error">${esc(error.message)}</p>`;
  }
}

function fileSize(bytes) {
  const n = Number(bytes ?? 0);
  if (n < 1024) return `${n} B`;
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} KB`;
  return `${(n / (1024 * 1024)).toFixed(1)} MB`;
}
