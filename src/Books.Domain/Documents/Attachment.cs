using Books.Domain.Access;

namespace Books.Domain.Documents;

/// <summary>
/// A file attached to a record (journal, sales or purchase document). The bytes live in file storage and the row keeps
/// only the key, content type and hash; attachments are optional and never change the accounting.
/// </summary>
public sealed class Attachment
{
    private Attachment() { }

    public Attachment(Guid id, Guid orgId, Guid ledgerId, string recordKind, Guid recordId, string fileName, string contentType,
        long size, string storageKey, string hash, Guid uploadedBy, DateTimeOffset createdAt)
    {
        Id = id;
        OrgId = orgId;
        LedgerId = ledgerId;
        RecordKind = recordKind;
        RecordId = recordId;
        FileName = fileName;
        ContentType = contentType;
        Size = size;
        StorageKey = storageKey;
        Hash = hash;
        UploadedBy = uploadedBy;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid OrgId { get; private set; }
    public Guid LedgerId { get; private set; }

    /// <summary>journal, sales_invoice, sales_credit_note, purchase_invoice or purchase_debit_note.</summary>
    public string RecordKind { get; private set; } = "";

    public Guid RecordId { get; private set; }
    public string FileName { get; private set; } = "";
    public string ContentType { get; private set; } = "";
    public long Size { get; private set; }
    public string StorageKey { get; private set; } = "";
    public string Hash { get; private set; } = "";
    public Guid UploadedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static readonly string[] Kinds = ["journal", "sales_invoice", "sales_credit_note", "purchase_invoice", "purchase_debit_note"];

    /// <summary>Allowed extensions and the content type served for each; anything else is rejected.</summary>
    public static readonly IReadOnlyDictionary<string, string> ContentTypes = new Dictionary<string, string>
    {
        [".pdf"] = "application/pdf",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".xls"] = "application/vnd.ms-excel",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".doc"] = "application/msword",
        [".csv"] = "text/csv",
        [".txt"] = "text/plain",
        [".zip"] = "application/zip",
    };
}
