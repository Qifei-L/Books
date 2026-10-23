using System.Net;
using Microsoft.EntityFrameworkCore;

namespace Books.Tests.Infrastructure;

/// <summary>Builds test situations through the API, and through SQL only where the API has no shortcut yet.</summary>
public static class Seed
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<Guid> UserIdAsync(this HttpClient client) =>
        (await (await client.GetAsync("/api/v1/me", Ct)).DataAsync()).Id();

    /// <summary>Creates a company and returns its ledger id.</summary>
    public static async Task<Guid> CreateLedgerAsync(this HttpClient admin, Guid orgId, string name = "Company") =>
        (await (await admin.PostAsync($"/api/v1/organizations/{orgId}/companies", new { name })).DataAsync(HttpStatusCode.Created)).Id("ledger");

    /// <summary>Adds the client's user to an organization directly in the database and returns the membership id.</summary>
    public static async Task<Guid> AddMemberAsync(this TestHost host, Guid orgId, HttpClient client, string role = "member")
    {
        var userId = await client.UserIdAsync();
        var membershipId = Guid.NewGuid();
        await using var db = host.CreateDbContext();
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO memberships (id, org_id, user_id, role, status, generation, version, joined_at)
            VALUES ({membershipId}, {orgId}, {userId}, {role}, 'active', 1, 1, now())
            """, Ct);
        return membershipId;
    }

    public static async Task<Guid> MembershipIdAsync(this HttpClient client, Guid orgId) =>
        (await (await client.GetAsync($"/api/v1/organizations/{orgId}/memberships/me", Ct)).DataAsync()).Id();

    public static async Task<long> MembershipVersionAsync(this HttpClient client, Guid orgId) =>
        long.Parse((await (await client.GetAsync($"/api/v1/organizations/{orgId}/memberships/me", Ct)).DataAsync()).GetProperty("version").GetString()!);
}
