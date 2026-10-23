using System.Data;
using System.Data.Common;
using System.Text.Json;
using Books.Application.Access;
using Books.Domain.Access;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Common;

public static class BooksJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static string Serialize(object value) => JsonSerializer.Serialize(value, Options);
}

/// <summary>Per-request write metadata from the transport (HTTP headers or MCP arguments).</summary>
public sealed record WriteMeta(Guid IdempotencyKey, string Fingerprint, long? ExpectedVersion, string RequestId);

/// <summary>A committed write's response. Json is the <c>data</c> payload, replayed verbatim on retry.</summary>
public sealed record WriteResult(int Status, string Json, long? Version)
{
    public static WriteResult Of(int status, object data, long? version = null) => new(status, BooksJson.Serialize(data), version);
}

/// <param name="Operation">Stable operation name, part of the idempotency scope.</param>
/// <param name="OrgId">Organization whose row is locked; null for global operations such as creating an organization.</param>
/// <param name="ReplayRequiresMembership">False only for receipts the actor may see after losing access (leaving).</param>
public sealed record WriteRequest(string Operation, Guid? OrgId, WriteMeta Meta, bool ReplayRequiresMembership = true);

/// <summary>
/// Runs every business write the same way, in one READ COMMITTED transaction:
/// lock the user, lock the organization, replay or reject a reused Idempotency-Key,
/// run the handler (which checks current access and version), then save the change,
/// its audit events and the idempotency record together.
/// </summary>
public sealed class WriteRunner(IBooksDb db, TimeProvider clock)
{
    public static readonly TimeSpan IdempotencyRetention = TimeSpan.FromDays(7);
    private const int MaxAttempts = 3;

    public async Task<WriteResult> RunAsync(Principal principal, WriteRequest request, Func<WriteScope, Task<WriteResult>> handler, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            db.ChangeTracker.Clear();
            try
            {
                return await RunOnceAsync(principal, request, handler, ct);
            }
            catch (Exception ex) when (Find<DbException>(ex) is { SqlState: "40001" or "40P01" } && attempt < MaxAttempts)
            {
                // Serialization failure or deadlock: retry the whole transaction with the same key.
            }
            catch (Exception ex) when (Find<DbException>(ex) is { SqlState: "55P03" or "57014" })
            {
                throw new BooksProblem(503, "TEMPORARILY_UNAVAILABLE", "The operation timed out; retry with the same Idempotency-Key.");
            }
        }
    }

    private async Task<WriteResult> RunOnceAsync(Principal principal, WriteRequest request, Func<WriteScope, Task<WriteResult>> handler, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '3s'; SET LOCAL statement_timeout = '10s'", ct);
        var now = clock.GetUtcNow();

        // Lock order everywhere: user, organization, then rows inside it.
        // Global operations lock the user exclusively so the same key cannot run twice concurrently.
        var userStatus = request.OrgId is null
            ? await db.Database.SqlQuery<string>($"SELECT status AS \"Value\" FROM users WHERE id = {principal.UserId} FOR UPDATE").ToListAsync(ct)
            : await db.Database.SqlQuery<string>($"SELECT status AS \"Value\" FROM users WHERE id = {principal.UserId} FOR SHARE").ToListAsync(ct);
        if (userStatus is not ["active"])
        {
            throw new BooksProblem(403, "USER_DISABLED", "This user is disabled.");
        }

        Organization? organization = null;
        Membership? actor = null;
        if (request.OrgId is { } orgId)
        {
            organization = (await db.Organizations.FromSql($"SELECT * FROM organizations WHERE id = {orgId} FOR UPDATE").ToListAsync(ct)).SingleOrDefault();
            actor = organization is null
                ? null
                : await db.Memberships.SingleOrDefaultAsync(
                    m => m.OrgId == orgId && m.UserId == principal.UserId && m.Status == MembershipStatus.Active, ct);
        }
        var scope = new WriteScope(db, principal, request, organization, actor, now);

        var clientId = principal.ClientId ?? "";
        var scopeKey = request.OrgId?.ToString() ?? "global";
        var key = request.Meta.IdempotencyKey;
        await db.IdempotencyRecords
            .Where(r => r.ActorUserId == principal.UserId && r.ClientId == clientId && r.ScopeKey == scopeKey
                && r.Operation == request.Operation && r.Key == key && r.ExpiresAt <= now)
            .ExecuteDeleteAsync(ct);
        var previous = await db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(
            r => r.ActorUserId == principal.UserId && r.ClientId == clientId && r.ScopeKey == scopeKey
                && r.Operation == request.Operation && r.Key == key, ct);
        if (previous is not null)
        {
            if (previous.RequestHash != request.Meta.Fingerprint)
            {
                throw BooksProblem.Conflict("IDEMPOTENCY_KEY_REUSED", "This Idempotency-Key was already used for a different request.");
            }
            if (request.ReplayRequiresMembership && request.OrgId is not null)
            {
                scope.RequireMember();
            }
            return new WriteResult(previous.ResponseStatus, previous.ResponseBody, null);
        }

        var result = await handler(scope);

        db.IdempotencyRecords.Add(new IdempotencyRecord(
            principal.UserId, clientId, scopeKey, request.Operation, key, request.Meta.Fingerprint,
            result.Status, result.Json, now, now + IdempotencyRetention));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return result;
    }

    private static T? Find<T>(Exception? ex) where T : Exception
    {
        for (; ex is not null; ex = ex.InnerException)
        {
            if (ex is T match)
            {
                return match;
            }
        }
        return null;
    }
}

/// <summary>What a write handler sees: the locked organization, the actor's current membership and the audit sink.</summary>
public sealed class WriteScope(IBooksDb db, Principal principal, WriteRequest request, Organization? organization, Membership? actor, DateTimeOffset now)
{
    public IBooksDb Db => db;
    public Principal Principal => principal;
    public DateTimeOffset Now => now;

    /// <summary>The actor's active membership, or 404 so other organizations' existence is never revealed.</summary>
    public Membership RequireMember()
    {
        if (organization is null || actor is null)
        {
            throw BooksProblem.NotFound();
        }
        if (organization.State != OrganizationState.Active)
        {
            throw new BooksProblem(403, "ORG_RECOVERY_REQUIRED", "This organization is locked for recovery.");
        }
        return actor;
    }

    public Membership RequireAdmin()
    {
        var member = RequireMember();
        return member.Role == MembershipRole.Admin ? member : throw BooksProblem.Forbidden();
    }

    public Organization Organization => organization ?? throw BooksProblem.NotFound();

    /// <summary>Rejects a write without If-Match (428) or with a stale version (412).</summary>
    public void CheckVersion(long current)
    {
        if (request.Meta.ExpectedVersion is not { } expected)
        {
            throw new BooksProblem(428, "PRECONDITION_REQUIRED", "If-Match with the current version is required.");
        }
        if (expected != current)
        {
            throw new BooksProblem(412, "VERSION_CONFLICT", "The resource has changed; read it again and retry.");
        }
    }

    public void Audit(string action, string targetType, Guid targetId, object changes, Guid? orgId = null) =>
        db.AuditEvents.Add(new AuditEvent(
            orgId ?? request.OrgId, principal.UserId, principal.ClientId, action, targetType, targetId,
            BooksJson.Serialize(changes), request.Meta.RequestId, now));
}
