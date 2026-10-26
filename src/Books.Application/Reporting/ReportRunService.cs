using System.Security.Cryptography;
using System.Text;
using Books.Application.Access;
using Books.Application.Common;
using Books.Application.Files;
using Books.Domain.Access;
using Books.Domain.Reporting;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Reporting;

public sealed record ReportRunInput(string Kind, Guid? TemplateId, string Period, string FileKind, string FileName, string ContentType,
    object Snapshot, string RequestId);

public sealed record ReportRunDto(Guid Id, string Kind, Guid? TemplateId, string Period, Guid GeneratedBy, DateTimeOffset GeneratedAt,
    string FileKind, string Status, string Hash, long Size, string FileName, string ContentType);

public sealed record ReportShareInput(int? Days, bool? Watermark);

public sealed record ReportShareDto(Guid Id, Guid ReportRunId, string Token, string Url, DateTimeOffset ExpiresAt, DateTimeOffset? RevokedAt,
    Guid CreatedBy, DateTimeOffset CreatedAt, bool Watermark);

/// <summary>A share token resolved to its run, with the watermark label the shared copy must carry.</summary>
public sealed record ShareAccess(ReportShare Share, ReportRun Run)
{
    public string? WatermarkLabel => Share.Watermark ? $"Shared copy · {Run.Period}" : null;
}

/// <summary>
/// Generates report runs, stores their files through <see cref="IFileStorage"/> and hands out read-only share links.
/// Client packs and tax workpapers both record a run before the file is returned, so every delivered pack is traceable
/// to its template version, hash and download history.
/// </summary>
public sealed class ReportRunService(IBooksDb db, ReadRunner reads, WriteRunner writes, IFileStorage storage)
{
    public async Task<ReportRunDto> RecordAsync(Principal principal, Guid orgId, Guid ledgerId, ReportRunInput input, byte[] content, CancellationToken ct)
    {
        var result = await writes.RunAsync(principal, new WriteRequest("report_run.create", orgId,
            new WriteMeta(Guid.NewGuid(), Guid.NewGuid().ToString("N"), null, input.RequestId), LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            if (!ReportTemplates.TryKind(input.Kind, out var kind))
            {
                throw BooksProblem.Invalid("kind is not a report kind.");
            }
            if (input.FileKind is not ("pdf" or "xlsx"))
            {
                throw BooksProblem.Invalid("file_kind must be pdf or xlsx.");
            }
            if (input.TemplateId is { } templateId)
            {
                var exists = await db.ReportTemplates.AnyAsync(t => t.Id == templateId && t.OrgId == orgId
                    && (t.LedgerId == null || t.LedgerId == ledgerId), ct);
                if (!exists)
                {
                    throw BooksProblem.Invalid("template_id does not belong to this ledger.");
                }
            }
            var extension = input.FileKind == "pdf" ? ".pdf" : ".xlsx";
            StoredFile stored;
            try
            {
                stored = await storage.SaveAsync("reports", new MemoryStream(content, writable: false), extension, ct);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                throw new BooksProblem(503, "STORAGE_UNAVAILABLE", "The report file could not be stored; retry.");
            }
            try
            {
                var run = new ReportRun(Guid.CreateVersion7(), orgId, ledgerId, input.TemplateId, ReportTemplates.KindName(kind),
                    input.Period, principal.UserId, scope.Now, input.FileKind, stored.Key, stored.Hash, input.FileName,
                    input.ContentType, stored.Size, BooksJson.Serialize(input.Snapshot));
                db.ReportRuns.Add(run);
                scope.Audit("report_run.created", "report_run", run.Id, new
                {
                    kind = run.Kind, period = run.Period, template_id = run.TemplateId, file_kind = run.FileKind, hash = run.Hash,
                }, orgId);
                await db.SaveChangesAsync(ct);
                return WriteResult.Of(201, ToDto(run));
            }
            catch
            {
                await storage.DeleteAsync(stored.Key, CancellationToken.None);
                throw;
            }
        }, ct);
        return Deserialize(result);
    }

    public Task<IReadOnlyList<ReportRunDto>> ListAsync(Principal principal, Guid orgId, Guid ledgerId, CancellationToken ct) =>
        reads.RunAsync<IReadOnlyList<ReportRunDto>>(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            return (await db.ReportRuns.AsNoTracking().Where(r => r.LedgerId == ledgerId).OrderByDescending(r => r.GeneratedAt).ToListAsync(ct))
                .Select(ToDto).ToList();
        }, ct);

    public Task<ReportRunDto> GetAsync(Principal principal, Guid orgId, Guid ledgerId, Guid id, CancellationToken ct) =>
        reads.RunAsync(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            return ToDto(await db.ReportRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id && r.LedgerId == ledgerId, ct)
                ?? throw BooksProblem.NotFound());
        }, ct);

    /// <summary>The stored file of a run, for a signed-in reader of the ledger.</summary>
    public Task<(ReportRunDto Run, Stream Content)> OpenAsync(Principal principal, Guid orgId, Guid ledgerId, Guid id, CancellationToken ct) =>
        reads.RunAsync(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            var run = await db.ReportRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id && r.LedgerId == ledgerId, ct)
                ?? throw BooksProblem.NotFound();
            return (ToDto(run), await storage.OpenReadAsync(run.StorageKey, ct));
        }, ct);

    public async Task<WriteResult> CreateShareAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, Guid runId,
        ReportShareInput input, CancellationToken ct)
    {
        return await writes.RunAsync(principal, new WriteRequest("report_share.create", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var run = await db.ReportRuns.SingleOrDefaultAsync(r => r.Id == runId && r.LedgerId == ledgerId && r.Status == "ready", ct)
                ?? throw BooksProblem.NotFound();
            var days = input.Days ?? 7;
            if (days is < 1 or > 365)
            {
                throw BooksProblem.Invalid("days must be 1–365.");
            }
            var share = new ReportShare(Guid.CreateVersion7(), run.Id, Token(), scope.Now.AddDays(days), principal.UserId, scope.Now,
                input.Watermark ?? true);
            db.ReportShares.Add(share);
            scope.Audit("report_share.created", "report_share", share.Id, new
            {
                report_run_id = run.Id, expires_at = share.ExpiresAt, watermark = share.Watermark,
            }, orgId);
            await db.SaveChangesAsync(ct);
            return WriteResult.Of(201, ToDto(share));
        }, ct);
    }

    public Task<IReadOnlyList<ReportShareDto>> ListSharesAsync(Principal principal, Guid orgId, Guid ledgerId, Guid runId, CancellationToken ct) =>
        reads.RunAsync<IReadOnlyList<ReportShareDto>>(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            var exists = await db.ReportRuns.AnyAsync(r => r.Id == runId && r.LedgerId == ledgerId, ct);
            if (!exists)
            {
                throw BooksProblem.NotFound();
            }
            return (await db.ReportShares.AsNoTracking().Where(s => s.ReportRunId == runId).OrderByDescending(s => s.CreatedAt).ToListAsync(ct))
                .Select(ToDto).ToList();
        }, ct);

    public async Task<WriteResult> RevokeAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, Guid shareId, CancellationToken ct)
    {
        return await writes.RunAsync(principal, new WriteRequest("report_share.revoke", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var share = await db.ReportShares.SingleOrDefaultAsync(s => s.Id == shareId
                && db.ReportRuns.Any(r => r.Id == s.ReportRunId && r.LedgerId == ledgerId), ct) ?? throw BooksProblem.NotFound();
            share.Revoke(scope.Now);
            scope.Audit("report_share.revoked", "report_share", share.Id, new { report_run_id = share.ReportRunId }, orgId);
            await db.SaveChangesAsync(ct);
            return WriteResult.Of(200, ToDto(share));
        }, ct);
    }

    /// <summary>Resolves a public token without a signed-in user: 404 when unknown, 410 when expired or revoked.</summary>
    public async Task<ShareAccess> ResolveAsync(string token, DateTimeOffset now, CancellationToken ct)
    {
        var share = await db.ReportShares.AsNoTracking().SingleOrDefaultAsync(s => s.Token == token, ct) ?? throw BooksProblem.NotFound();
        if (share.RevokedAt is not null)
        {
            throw new BooksProblem(410, "SHARE_REVOKED", "This share link was revoked.");
        }
        if (share.ExpiresAt <= now)
        {
            throw new BooksProblem(410, "SHARE_EXPIRED", "This share link has expired.");
        }
        var run = await db.ReportRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == share.ReportRunId, ct) ?? throw BooksProblem.NotFound();
        return new ShareAccess(share, run);
    }

    public async Task<Stream> OpenShareAsync(ShareAccess access, CancellationToken ct) => await storage.OpenReadAsync(access.Run.StorageKey, ct);

    /// <summary>Records who downloaded a shared copy (anonymous downloads keep a null actor in the changes).</summary>
    public async Task AuditShareDownloadAsync(ShareAccess access, Principal? principal, string requestId, CancellationToken ct)
    {
        db.AuditEvents.Add(new AuditEvent(access.Run.OrgId, principal?.UserId ?? Guid.Empty, principal?.ClientId, "report_share.downloaded",
            "report_share", access.Share.Id, BooksJson.Serialize(new
            {
                report_run_id = access.Run.Id, watermark = access.Share.Watermark, anonymous = principal is null,
            }), requestId, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(ct);
    }

    private static string Token() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static ReportRunDto Deserialize(WriteResult result) =>
        System.Text.Json.JsonSerializer.Deserialize<ReportRunDto>(result.Json, BooksJson.Options)!;

    private static ReportRunDto ToDto(ReportRun r) => new(r.Id, r.Kind, r.TemplateId, r.Period, r.GeneratedBy, r.GeneratedAt, r.FileKind,
        r.Status, r.Hash, r.Size, r.FileName, r.ContentType);

    private static ReportShareDto ToDto(ReportShare s) => new(s.Id, s.ReportRunId, s.Token, $"/share/{s.Token}", s.ExpiresAt, s.RevokedAt,
        s.CreatedBy, s.CreatedAt, s.Watermark);
}
