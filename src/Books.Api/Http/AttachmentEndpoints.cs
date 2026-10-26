using Books.Api.Auth;
using Books.Application.Common;
using Books.Application.Documents;

namespace Books.Api.Http;

public static class AttachmentEndpoints
{
    public static void MapAttachmentEndpoints(this RouteGroupBuilder api)
    {
        var ledger = api.MapGroup("/organizations/{orgId:guid}/ledgers/{ledgerId:guid}");

        ledger.MapGet("/attachments", async (Guid orgId, Guid ledgerId, string? recordKind, Guid? recordId, PrincipalAccessor principals,
            AttachmentService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.ListAsync(await principals.GetAsync(ct), orgId, ledgerId, recordKind, recordId, ct)));

        // Multipart upload: record_kind, record_id and file. Read access downloads; edit access uploads and deletes.
        ledger.MapPost("/attachments", async (Guid orgId, Guid ledgerId, HttpContext http, PrincipalAccessor principals,
            AttachmentService service, CancellationToken ct) =>
        {
            var form = await http.Request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file") ?? throw BooksProblem.Invalid("file is required.");
            var kind = form["record_kind"].ToString();
            if (!Guid.TryParse(form["record_id"].ToString(), out var recordId))
            {
                throw BooksProblem.Invalid("record_id must be a UUID.");
            }
            var meta = WriteHttp.Meta(http, new { record_kind = kind, record_id = recordId, file_name = file.FileName, size = file.Length });
            await using var stream = file.OpenReadStream();
            return WriteHttp.Result(http, await service.UploadAsync(await principals.GetAsync(ct), meta, orgId, ledgerId,
                new AttachmentInput(kind, recordId, file.FileName, file.Length), stream, ct));
        });

        ledger.MapGet("/attachments/{id:guid}/download", async (Guid orgId, Guid ledgerId, Guid id, PrincipalAccessor principals,
            AttachmentService service, CancellationToken ct) =>
        {
            var (attachment, stream) = await service.DownloadAsync(await principals.GetAsync(ct), orgId, ledgerId, id, ct);
            return Results.File(stream, attachment.ContentType, attachment.FileName);
        });

        ledger.MapDelete("/attachments/{id:guid}", async (Guid orgId, Guid ledgerId, Guid id, PrincipalAccessor principals,
            AttachmentService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.DeleteAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, null), orgId, ledgerId, id, ct)));
    }
}
