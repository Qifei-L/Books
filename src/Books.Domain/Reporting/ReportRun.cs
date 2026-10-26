using Books.Domain.Access;

namespace Books.Domain.Reporting;

/// <summary>
/// One generated report file. The bytes live in file storage; the row keeps the hash, the template version and a
/// snapshot of the data, so a shared copy can be re-rendered (for example with a watermark) without reading the books again.
/// </summary>
public sealed class ReportRun
{
    private ReportRun() { }

    public ReportRun(Guid id, Guid orgId, Guid ledgerId, Guid? templateId, string kind, string period, Guid generatedBy, DateTimeOffset generatedAt,
        string fileKind, string storageKey, string hash, string fileName, string contentType, long size, string snapshot)
    {
        if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(period))
        {
            throw new DomainException("kind and period are required.");
        }
        if (fileKind is not ("pdf" or "xlsx"))
        {
            throw new DomainException("file_kind must be pdf or xlsx.");
        }
        Id = id;
        OrgId = orgId;
        LedgerId = ledgerId;
        TemplateId = templateId;
        Kind = kind;
        Period = period;
        GeneratedBy = generatedBy;
        GeneratedAt = generatedAt;
        FileKind = fileKind;
        StorageKey = storageKey;
        Hash = hash;
        FileName = fileName;
        ContentType = contentType;
        Size = size;
        Snapshot = snapshot;
    }

    public Guid Id { get; private set; }
    public Guid OrgId { get; private set; }
    public Guid LedgerId { get; private set; }

    /// <summary>The report template version that laid it out; null when the code default was used.</summary>
    public Guid? TemplateId { get; private set; }

    public string Kind { get; private set; } = "";
    public string Period { get; private set; } = "";
    public Guid GeneratedBy { get; private set; }
    public DateTimeOffset GeneratedAt { get; private set; }
    public string FileKind { get; private set; } = "pdf";
    public string StorageKey { get; private set; } = "";
    public string Hash { get; private set; } = "";
    public string Status { get; private set; } = "ready";
    public string FileName { get; private set; } = "";
    public string ContentType { get; private set; } = "application/pdf";
    public long Size { get; private set; }

    /// <summary>The report data as JSON, so a shared copy can be re-rendered with the same figures.</summary>
    public string Snapshot { get; private set; } = "{}";
}

/// <summary>A read-only link to a report run: it expires, can be revoked, and its downloads are audited.</summary>
public sealed class ReportShare
{
    private ReportShare() { }

    public ReportShare(Guid id, Guid reportRunId, string token, DateTimeOffset expiresAt, Guid createdBy, DateTimeOffset createdAt, bool watermark)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new DomainException("token is required.");
        }
        if (expiresAt <= createdAt)
        {
            throw new DomainException("A share link must expire after it is created.");
        }
        Id = id;
        ReportRunId = reportRunId;
        Token = token;
        ExpiresAt = expiresAt;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
        Watermark = watermark;
    }

    public Guid Id { get; private set; }
    public Guid ReportRunId { get; private set; }
    public string Token { get; private set; } = "";
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>A visible mark on every page of the shared copy.</summary>
    public bool Watermark { get; private set; }

    public bool Active(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}
