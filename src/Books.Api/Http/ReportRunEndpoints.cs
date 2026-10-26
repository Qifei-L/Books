using Books.Api.Auth;
using Books.Api.Reporting;
using Books.Application.Common;
using Books.Application.Reporting;

namespace Books.Api.Http;

public static class ReportRunEndpoints
{
    public sealed record ShareRequest(int? Days, bool? Watermark);

    public static void MapReportRunEndpoints(this RouteGroupBuilder api)
    {
        var ledger = api.MapGroup("/organizations/{orgId:guid}/ledgers/{ledgerId:guid}");

        ledger.MapGet("/report-runs", async (Guid orgId, Guid ledgerId, PrincipalAccessor principals, ReportRunService runs, HttpContext http,
            CancellationToken ct) =>
            Envelope.Ok(http, await runs.ListAsync(await principals.GetAsync(ct), orgId, ledgerId, ct)));

        ledger.MapGet("/report-runs/{id:guid}", async (Guid orgId, Guid ledgerId, Guid id, PrincipalAccessor principals, ReportRunService runs,
            HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await runs.GetAsync(await principals.GetAsync(ct), orgId, ledgerId, id, ct)));

        ledger.MapGet("/report-runs/{id:guid}/download", async (Guid orgId, Guid ledgerId, Guid id, PrincipalAccessor principals,
            ReportRunService runs, CancellationToken ct) =>
        {
            var (run, stream) = await runs.OpenAsync(await principals.GetAsync(ct), orgId, ledgerId, id, ct);
            return Results.File(stream, run.ContentType, run.FileName);
        });

        ledger.MapGet("/report-runs/{id:guid}/shares", async (Guid orgId, Guid ledgerId, Guid id, PrincipalAccessor principals,
            ReportRunService runs, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await runs.ListSharesAsync(await principals.GetAsync(ct), orgId, ledgerId, id, ct)));

        // A share needs edit access to the ledger; the link itself is read-only and needs no sign-in.
        ledger.MapPost("/report-runs/{id:guid}/shares", async (Guid orgId, Guid ledgerId, Guid id, ShareRequest? body, PrincipalAccessor principals,
            ReportRunService runs, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await runs.CreateShareAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, id,
                new ReportShareInput(body?.Days, body?.Watermark), ct)));

        ledger.MapDelete("/report-shares/{id:guid}", async (Guid orgId, Guid ledgerId, Guid id, PrincipalAccessor principals,
            ReportRunService runs, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await runs.RevokeAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, null), orgId, ledgerId, id, ct)));
    }

    /// <summary>Public share routes, mapped without authentication: the token is the only credential.</summary>
    public static void MapShareEndpoints(this WebApplication app)
    {
        app.MapGet("/share/{token}", (string token, ReportRunService runs, HttpContext http, CancellationToken ct) =>
            ShareFileAsync(token, download: false, runs, http, ct));
        app.MapGet("/share/{token}/download", (string token, ReportRunService runs, HttpContext http, CancellationToken ct) =>
            ShareFileAsync(token, download: true, runs, http, ct));
    }

    private static async Task<IResult> ShareFileAsync(string token, bool download, ReportRunService runs, HttpContext http, CancellationToken ct)
    {
        var access = await runs.ResolveAsync(token, DateTimeOffset.UtcNow, ct);
        await runs.AuditShareDownloadAsync(access, null, http.TraceIdentifier, ct);
        // A shared PDF is re-rendered from the run's snapshot with a visible watermark; other files stream unchanged.
        var rendered = access.Share.Watermark && access.Run.FileKind == "pdf" ? ReportSnapshots.WithWatermark(access.Run) : null;
        if (rendered is not null)
        {
            return download
                ? Results.File(rendered, access.Run.ContentType, access.Run.FileName)
                : Results.Bytes(rendered, access.Run.ContentType);
        }
        var stream = await runs.OpenShareAsync(access, ct);
        return download
            ? Results.File(stream, access.Run.ContentType, access.Run.FileName)
            : Results.Stream(stream, access.Run.ContentType);
    }
}
