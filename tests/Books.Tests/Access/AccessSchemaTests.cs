using Books.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Books.Tests.Access;

/// <summary>Database-level guarantees that hold even if application code is bypassed.</summary>
public sealed class AccessSchemaTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Audit_events_cannot_be_updated_deleted_or_truncated()
    {
        await using var db = host.CreateDbContext();
        var userId = await InsertUserAsync(db);
        var auditId = Guid.NewGuid();
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO audit_events (id, actor_user_id, action, target_type, target_id, changes, request_id, occurred_at)
            VALUES ({auditId}, {userId}, 'test.event', 'user', {userId}, CAST({"{}"} AS jsonb), 'req', now())
            """, Ct);

        var update = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlAsync($"UPDATE audit_events SET action = 'changed' WHERE id = {auditId}", Ct));
        var delete = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlAsync($"DELETE FROM audit_events WHERE id = {auditId}", Ct));
        var truncate = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync("TRUNCATE audit_events", Ct));

        Assert.All([update, delete, truncate], e => Assert.Equal("42501", e.SqlState));
        Assert.Equal(1, await db.AuditEvents.CountAsync(a => a.Id == auditId, Ct));
    }

    [Fact]
    public async Task A_user_has_at_most_one_membership_per_organization()
    {
        await using var db = host.CreateDbContext();
        var userId = await InsertUserAsync(db);
        var orgId = Guid.NewGuid();
        await db.Database.ExecuteSqlAsync(
            $"INSERT INTO organizations (id, name, state, created_by, created_at, version) VALUES ({orgId}, 'Org', 'active', {userId}, now(), 1)", Ct);
        await InsertMembershipAsync(db, orgId, userId);

        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => InsertMembershipAsync(db, orgId, userId));

        Assert.Equal("23505", duplicate.SqlState);
    }

    internal static async Task<Guid> InsertUserAsync(DbContext db)
    {
        var userId = Guid.NewGuid();
        await db.Database.ExecuteSqlAsync(
            $"INSERT INTO users (id, status, display_name, created_at, version) VALUES ({userId}, 'active', 'Test', now(), 1)", Ct);
        return userId;
    }

    internal static Task InsertMembershipAsync(DbContext db, Guid orgId, Guid userId, string role = "admin") =>
        db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO memberships (id, org_id, user_id, role, status, generation, version, joined_at)
            VALUES ({Guid.NewGuid()}, {orgId}, {userId}, {role}, 'active', 1, 1, now())
            """, Ct);
}
