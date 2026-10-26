using Books.Application.Access;
using Books.Application.Common;
using Books.Application.Files;
using Books.Domain.Access;
using Books.Domain.Documents;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Documents;

public sealed record AttachmentInput(string RecordKind, Guid RecordId, string FileName, long Size);

public sealed record AttachmentDto(Guid Id, string RecordKind, Guid RecordId, string FileName, string ContentType, long Size, string Hash,
    Guid UploadedBy, DateTimeOffset CreatedAt);

/// <summary>
/// Files attached to journals and documents (M17). Read access downloads; edit access uploads and deletes. The bytes go
/// to file storage, the row keeps the key and hash, every write is audited, and only known record kinds and safe file
/// types are accepted.
/// </summary>
public sealed class AttachmentService(IBooksDb db, ReadRunner reads, WriteRunner writes, IFileStorage storage)
{
    public const long MaxBytes = 20 * 1024 * 1024;

    public Task<IReadOnlyList<AttachmentDto>> ListAsync(Principal principal, Guid orgId, Guid ledgerId, string? recordKind, Guid? recordId,
        CancellationToken ct) =>
        reads.RunAsync<IReadOnlyList<AttachmentDto>>(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            if (recordKind is not null && !Attachment.Kinds.Contains(recordKind))
            {
                throw BooksProblem.Invalid($"record_kind must be one of {string.Join(", ", Attachment.Kinds)}.");
            }
            var rows = db.Attachments.AsNoTracking().Where(a => a.LedgerId == ledgerId);
            if (recordKind is not null)
            {
                rows = rows.Where(a => a.RecordKind == recordKind);
            }
            if (recordId is { } id)
            {
                rows = rows.Where(a => a.RecordId == id);
            }
            return (await rows.OrderBy(a => a.CreatedAt).ToListAsync(ct)).Select(ToDto).ToList();
        }, ct);

    public Task<(AttachmentDto Attachment, Stream Content)> DownloadAsync(Principal principal, Guid orgId, Guid ledgerId, Guid id,
        CancellationToken ct) =>
        reads.RunAsync(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            var row = await db.Attachments.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id && a.LedgerId == ledgerId, ct)
                ?? throw BooksProblem.NotFound();
            return (ToDto(row), await storage.OpenReadAsync(row.StorageKey, ct));
        }, ct);

    public async Task<WriteResult> UploadAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, AttachmentInput input,
        Stream content, CancellationToken ct)
    {
        if (!Attachment.Kinds.Contains(input.RecordKind))
        {
            throw BooksProblem.Invalid($"record_kind must be one of {string.Join(", ", Attachment.Kinds)}.");
        }
        if (input.Size is <= 0 or > MaxBytes)
        {
            throw new BooksProblem(413, "FILE_TOO_LARGE", $"An attachment must be between 1 byte and {MaxBytes / (1024 * 1024)} MB.");
        }
        var name = SafeName(input.FileName);
        var extension = Path.GetExtension(name).ToLowerInvariant();
        if (!Attachment.ContentTypes.TryGetValue(extension, out var contentType))
        {
            throw BooksProblem.Invalid($"Only these file types are accepted: {string.Join(", ", Attachment.ContentTypes.Keys)}.");
        }

        var result = await writes.RunAsync(principal, new WriteRequest("attachment.upload", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            if (!await RecordExistsAsync(ledgerId, input.RecordKind, input.RecordId, ct))
            {
                throw BooksProblem.NotFound();
            }
            var copied = await ReadLimitedAsync(content, ct);
            StoredFile stored;
            try
            {
                stored = await storage.SaveAsync("attachments", new MemoryStream(copied, writable: false), extension, ct);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                throw new BooksProblem(503, "STORAGE_UNAVAILABLE", "The attachment could not be stored; retry.");
            }
            try
            {
                var row = new Attachment(Guid.CreateVersion7(), orgId, ledgerId, input.RecordKind, input.RecordId, name, contentType,
                    stored.Size, stored.Key, stored.Hash, principal.UserId, scope.Now);
                db.Attachments.Add(row);
                scope.Audit("attachment.uploaded", "attachment", row.Id, new
                {
                    record_kind = row.RecordKind, record_id = row.RecordId, file_name = row.FileName, size = row.Size, hash = row.Hash,
                }, orgId);
                await db.SaveChangesAsync(ct);
                return WriteResult.Of(201, ToDto(row));
            }
            catch
            {
                await storage.DeleteAsync(stored.Key, CancellationToken.None);
                throw;
            }
        }, ct);
        return result;
    }

    public Task<WriteResult> DeleteAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, Guid id, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("attachment.delete", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var row = await db.Attachments.SingleOrDefaultAsync(a => a.Id == id && a.LedgerId == ledgerId, ct) ?? throw BooksProblem.NotFound();
            var dto = ToDto(row);
            db.Attachments.Remove(row);
            scope.Audit("attachment.deleted", "attachment", row.Id, dto, orgId);
            await db.SaveChangesAsync(ct);
            // The row is gone first: an orphan file is harmless, a row without a file is not.
            await storage.DeleteAsync(row.StorageKey, CancellationToken.None);
            return WriteResult.Of(200, dto);
        }, ct);

    private async Task<bool> RecordExistsAsync(Guid ledgerId, string kind, Guid id, CancellationToken ct) => kind switch
    {
        "journal" => await db.Journals.AnyAsync(j => j.Id == id && j.LedgerId == ledgerId, ct),
        "sales_invoice" => await db.SalesInvoices.AnyAsync(i => i.Id == id && i.LedgerId == ledgerId && !i.IsCreditNote, ct),
        "sales_credit_note" => await db.SalesInvoices.AnyAsync(i => i.Id == id && i.LedgerId == ledgerId && i.IsCreditNote, ct),
        "purchase_invoice" => await db.PurchaseInvoices.AnyAsync(i => i.Id == id && i.LedgerId == ledgerId && !i.IsDebitNote, ct),
        "purchase_debit_note" => await db.PurchaseInvoices.AnyAsync(i => i.Id == id && i.LedgerId == ledgerId && i.IsDebitNote, ct),
        _ => false,
    };

    /// <summary>Copies at most <see cref="MaxBytes"/>; a stream longer than its declared size is rejected rather than trusted.</summary>
    private static async Task<byte[]> ReadLimitedAsync(Stream content, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        long total = 0;
        int read;
        while ((read = await content.ReadAsync(chunk, ct)) > 0)
        {
            total += read;
            if (total > MaxBytes)
            {
                throw new BooksProblem(413, "FILE_TOO_LARGE", $"An attachment must be at most {MaxBytes / (1024 * 1024)} MB.");
            }
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
        }
        if (total == 0)
        {
            throw BooksProblem.Invalid("The file is empty.");
        }
        return buffer.ToArray();
    }

    /// <summary>A display name only: no directories, control characters or Windows-reserved trailing dots/spaces.</summary>
    private static string SafeName(string fileName)
    {
        // Take the last segment of either separator before cleaning, so "../../x.pdf" becomes "x.pdf".
        var path = (fileName ?? "").Replace('\\', '/');
        var segment = path[(path.LastIndexOf('/') + 1)..];
        var name = new string(segment.Where(c => !char.IsControl(c) && c is not ('/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|')).ToArray())
            .Trim().TrimEnd('.');
        if (name.Length > 255)
        {
            var extension = Path.GetExtension(name);
            var stem = Path.GetFileNameWithoutExtension(name);
            name = stem[..(255 - extension.Length)] + extension;
        }
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..")
        {
            throw BooksProblem.Invalid("file_name is required.");
        }
        return name;
    }

    private static AttachmentDto ToDto(Attachment a) => new(a.Id, a.RecordKind, a.RecordId, a.FileName, a.ContentType, a.Size, a.Hash,
        a.UploadedBy, a.CreatedAt);
}
