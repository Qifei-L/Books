using System.ComponentModel;
using Books.Api.Auth;
using Books.Application.Documents;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Books.Api.Mcp;

/// <summary>Attachments through MCP: AI clients list and upload files to journals and documents under the same rules.</summary>
[McpServerToolType]
public sealed class BooksAttachmentTools(PrincipalAccessor principals, AttachmentService attachments, IHttpContextAccessor http)
{
    [McpServerTool(Name = "list_attachments", ReadOnly = true, Idempotent = true)]
    [Description("Files attached to a ledger, optionally one record: metadata only (name, content type, size, hash), newest last.")]
    public Task<CallToolResult> List(Guid org_id, Guid ledger_id, string? record_kind = null, Guid? record_id = null, CancellationToken ct = default) =>
        BooksReadTools.RunAsync(async () => await attachments.ListAsync(await principals.GetAsync(ct), org_id, ledger_id, record_kind, record_id, ct));

    [McpServerTool(Name = "upload_attachment", Idempotent = true)]
    [Description("Uploads a file to a journal, sales invoice/credit note or purchase invoice/debit note. Content is base64; "
        + "the size limit is 20 MB, only document and image file types are accepted, and records the file's name and hash.")]
    public Task<CallToolResult> Upload(Guid org_id, Guid ledger_id,
        [Description("A new UUID per intended upload; reuse it to retry safely.")] Guid idempotency_key,
        [Description("journal, sales_invoice, sales_credit_note, purchase_invoice or purchase_debit_note.")] string record_kind,
        Guid record_id, string file_name, string content_base64, CancellationToken ct = default) =>
        McpWrites.RunAsync(async () =>
        {
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(content_base64);
            }
            catch (FormatException)
            {
                throw Books.Application.Common.BooksProblem.Invalid("content_base64 is not valid base64.");
            }
            var meta = McpWrites.Meta(http, "upload_attachment", idempotency_key, new
            {
                org_id, ledger_id, record_kind, record_id, file_name, size = bytes.Length,
            });
            return await attachments.UploadAsync(await principals.GetAsync(ct), meta, org_id, ledger_id,
                new AttachmentInput(record_kind, record_id, file_name, bytes.Length), new MemoryStream(bytes, writable: false), ct);
        });
}
