using System.Net;
using Books.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Books.Tests.Access;

/// <summary>Rule 8: an organization never loses its last administrator, including through concurrent changes.</summary>
public sealed class LastAdminTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_only_administrator_cannot_leave_demote_or_remove_themself()
    {
        using var admin = host.Client(Ids.Subject());
        var orgId = await admin.CreateOrganizationAsync();
        var me = await admin.MembershipIdAsync(orgId);

        var leave = await admin.PostAsync($"/api/v1/organizations/{orgId}/memberships/me/leave", null, ifMatch: 1);
        var demote = await admin.WriteAsync(HttpMethod.Put, $"/api/v1/organizations/{orgId}/memberships/{me}/access", new { role = "member", grants = Array.Empty<object>() }, ifMatch: 1);
        var remove = await admin.PostAsync($"/api/v1/organizations/{orgId}/memberships/{me}/remove", null, ifMatch: 1);

        await leave.AssertProblemAsync(HttpStatusCode.Conflict, "LAST_ADMIN");
        await demote.AssertProblemAsync(HttpStatusCode.Conflict, "LAST_ADMIN");
        await remove.AssertProblemAsync(HttpStatusCode.Conflict, "LAST_ADMIN");
    }

    [Fact]
    public async Task Two_administrators_leaving_at_the_same_time_leave_exactly_one()
    {
        for (var round = 0; round < 5; round++)
        {
            using var a = host.Client(Ids.Subject("a"));
            using var b = host.Client(Ids.Subject("b"));
            var orgId = await a.CreateOrganizationAsync();
            await host.AddMemberAsync(orgId, b, role: "admin");

            var results = await Task.WhenAll(
                a.PostAsync($"/api/v1/organizations/{orgId}/memberships/me/leave", null, ifMatch: 1),
                b.PostAsync($"/api/v1/organizations/{orgId}/memberships/me/leave", null, ifMatch: 1));

            Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
            Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
            Assert.Equal(1, await ActiveAdminsAsync(orgId));
        }
    }

    [Fact]
    public async Task Two_administrators_demoting_each_other_at_the_same_time_leave_one()
    {
        using var a = host.Client(Ids.Subject("a"));
        using var b = host.Client(Ids.Subject("b"));
        var orgId = await a.CreateOrganizationAsync();
        var aId = await a.MembershipIdAsync(orgId);
        var bId = await host.AddMemberAsync(orgId, b, role: "admin");
        var asMember = new { role = "member", grants = Array.Empty<object>() };

        var results = await Task.WhenAll(
            a.WriteAsync(HttpMethod.Put, $"/api/v1/organizations/{orgId}/memberships/{bId}/access", asMember, ifMatch: 1),
            b.WriteAsync(HttpMethod.Put, $"/api/v1/organizations/{orgId}/memberships/{aId}/access", asMember, ifMatch: 1));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(1, await ActiveAdminsAsync(orgId));
    }

    [Fact]
    public async Task The_database_rejects_removing_the_last_administrator_even_when_the_service_is_bypassed()
    {
        using var admin = host.Client(Ids.Subject());
        var orgId = await admin.CreateOrganizationAsync();
        await using var db = host.CreateDbContext();

        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlAsync($"UPDATE memberships SET role = 'member' WHERE org_id = {orgId}", Ct));

        Assert.Equal("23000", error.SqlState);
        Assert.Equal(1, await ActiveAdminsAsync(orgId));
    }

    [Fact]
    public async Task Concurrent_raw_demotions_cannot_both_commit()
    {
        using var a = host.Client(Ids.Subject("a"));
        using var b = host.Client(Ids.Subject("b"));
        var orgId = await a.CreateOrganizationAsync();
        var aId = await a.MembershipIdAsync(orgId);
        var bId = await host.AddMemberAsync(orgId, b, role: "admin");
        await using var first = new NpgsqlConnection(host.ConnectionString);
        await using var second = new NpgsqlConnection(host.ConnectionString);
        await first.OpenAsync(Ct);
        await second.OpenAsync(Ct);
        await using var tx1 = await first.BeginTransactionAsync(Ct);
        await using var tx2 = await second.BeginTransactionAsync(Ct);

        // Both demotions run before either commits; neither sees the other's uncommitted change.
        await Demote(first, tx1, aId);
        await Demote(second, tx2, bId);
        var commits = await Task.WhenAll(TryCommit(tx1), TryCommit(tx2));

        Assert.Single(commits, ok => ok);
        Assert.Equal(1, await ActiveAdminsAsync(orgId));

        static async Task Demote(NpgsqlConnection connection, NpgsqlTransaction tx, Guid membershipId)
        {
            await using var command = new NpgsqlCommand("UPDATE memberships SET role = 'member' WHERE id = @id", connection, tx);
            command.Parameters.AddWithValue("id", membershipId);
            await command.ExecuteNonQueryAsync(Ct);
        }

        static async Task<bool> TryCommit(NpgsqlTransaction tx)
        {
            try
            {
                await tx.CommitAsync(Ct);
                return true;
            }
            catch (PostgresException e) when (e.SqlState == "23000")
            {
                return false;
            }
        }
    }

    private async Task<int> ActiveAdminsAsync(Guid orgId)
    {
        await using var db = host.CreateDbContext();
        return await db.Memberships.CountAsync(
            m => m.OrgId == orgId && m.Role == Books.Domain.Access.MembershipRole.Admin && m.Status == Books.Domain.Access.MembershipStatus.Active, Ct);
    }
}
