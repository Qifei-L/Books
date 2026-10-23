using System.Net;
using System.Text.Json;
using Books.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Books.Tests.Access;

/// <summary>Rules 2, 3, 5 and 7: members reach only granted ledgers, through current grants only.</summary>
public sealed class LedgerGrantTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_member_sees_only_granted_ledgers_with_the_granted_access()
    {
        var (admin, member, orgId, membershipId) = await SetUpAsync();
        var granted = await admin.CreateLedgerAsync(orgId, "Granted");
        var hidden = await admin.CreateLedgerAsync(orgId, "Hidden");

        await SetAccessAsync(admin, orgId, membershipId, "member", [new { ledger_id = granted, access = "read" }], version: 1);

        var ledgers = await (await member.GetAsync($"/api/v1/organizations/{orgId}/ledgers", Ct)).DataAsync();
        var only = Assert.Single(ledgers.EnumerateArray());
        Assert.Equal(granted, only.Id());
        Assert.Equal("read", only.GetProperty("effective_access").GetString());
        await (await member.GetAsync($"/api/v1/organizations/{orgId}/ledgers/{hidden}", Ct)).AssertProblemAsync(HttpStatusCode.NotFound, "NOT_FOUND");
        Assert.Single((await (await member.GetAsync($"/api/v1/organizations/{orgId}/companies", Ct)).DataAsync()).EnumerateArray());
    }

    [Fact]
    public async Task Members_even_with_edit_access_cannot_manage_access()
    {
        var (admin, member, orgId, membershipId) = await SetUpAsync();
        var ledger = await admin.CreateLedgerAsync(orgId);
        await SetAccessAsync(admin, orgId, membershipId, "member", [new { ledger_id = ledger, access = "edit" }], version: 1);

        var selfPromote = await member.WriteAsync(HttpMethod.Put, $"/api/v1/organizations/{orgId}/memberships/{membershipId}/access",
            new { role = "admin", grants = Array.Empty<object>() }, ifMatch: 2);
        var list = await member.GetAsync($"/api/v1/organizations/{orgId}/memberships", Ct);
        var createCompany = await member.PostAsync($"/api/v1/organizations/{orgId}/companies", new { name = "Mine" });

        await selfPromote.AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
        await list.AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
        await createCompany.AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task A_ledger_from_another_organization_cannot_be_granted()
    {
        var (admin, _, orgId, membershipId) = await SetUpAsync();
        using var other = host.Client(Ids.Subject("other"));
        var otherOrg = await other.CreateOrganizationAsync();
        var foreignLedger = await other.CreateLedgerAsync(otherOrg);

        var response = await admin.WriteAsync(HttpMethod.Put, $"/api/v1/organizations/{orgId}/memberships/{membershipId}/access",
            new { role = "member", grants = new[] { new { ledger_id = foreignLedger, access = "edit" } } }, ifMatch: 1);

        await response.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
    }

    [Fact]
    public async Task Promotion_clears_grants_and_a_later_demotion_does_not_bring_them_back()
    {
        var (admin, member, orgId, membershipId) = await SetUpAsync();
        var ledger = await admin.CreateLedgerAsync(orgId);
        await SetAccessAsync(admin, orgId, membershipId, "member", [new { ledger_id = ledger, access = "edit" }], version: 1);

        await SetAccessAsync(admin, orgId, membershipId, "admin", [], version: 2);
        await SetAccessAsync(admin, orgId, membershipId, "member", [], version: 3);

        var ledgers = await (await member.GetAsync($"/api/v1/organizations/{orgId}/ledgers", Ct)).DataAsync();
        Assert.Equal(0, ledgers.GetArrayLength());
    }

    [Fact]
    public async Task A_grant_from_an_earlier_membership_generation_gives_no_access()
    {
        var (admin, member, orgId, membershipId) = await SetUpAsync();
        var ledger = await admin.CreateLedgerAsync(orgId);
        await SetAccessAsync(admin, orgId, membershipId, "member", [new { ledger_id = ledger, access = "edit" }], version: 1);

        await using (var db = host.CreateDbContext())
        {
            // As if the member had been removed and rejoined: the generation moves on, the old grant row stays.
            await db.Database.ExecuteSqlAsync($"UPDATE memberships SET generation = generation + 1 WHERE id = {membershipId}", Ct);
        }

        await (await member.GetAsync($"/api/v1/organizations/{orgId}/ledgers/{ledger}", Ct)).AssertProblemAsync(HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Fact]
    public async Task The_only_administrator_cannot_demote_themself()
    {
        using var admin = host.Client(Ids.Subject());
        var orgId = await admin.CreateOrganizationAsync();
        var me = await admin.MembershipIdAsync(orgId);

        var response = await admin.WriteAsync(HttpMethod.Put, $"/api/v1/organizations/{orgId}/memberships/{me}/access",
            new { role = "member", grants = Array.Empty<object>() }, ifMatch: 1);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, "LAST_ADMIN");
    }

    [Fact]
    public async Task A_removed_member_loses_access_immediately()
    {
        var (admin, member, orgId, membershipId) = await SetUpAsync();
        var ledger = await admin.CreateLedgerAsync(orgId);
        await SetAccessAsync(admin, orgId, membershipId, "member", [new { ledger_id = ledger, access = "read" }], version: 1);

        await (await admin.PostAsync($"/api/v1/organizations/{orgId}/memberships/{membershipId}/remove", null, ifMatch: 2)).DataAsync();

        await (await member.GetAsync($"/api/v1/organizations/{orgId}", Ct)).AssertProblemAsync(HttpStatusCode.NotFound, "NOT_FOUND");
        await (await member.GetAsync($"/api/v1/organizations/{orgId}/ledgers/{ledger}", Ct)).AssertProblemAsync(HttpStatusCode.NotFound, "NOT_FOUND");
        await using var db = host.CreateDbContext();
        Assert.Equal(0, await db.LedgerGrants.CountAsync(g => g.MembershipId == membershipId, Ct));
    }

    [Fact]
    public async Task Leaving_replays_its_receipt_after_access_is_gone()
    {
        var (_, member, orgId, _) = await SetUpAsync();
        var key = Guid.NewGuid();

        var first = await (await member.PostAsync($"/api/v1/organizations/{orgId}/memberships/me/leave", null, key, ifMatch: 1)).DataAsync();
        var replay = await (await member.PostAsync($"/api/v1/organizations/{orgId}/memberships/me/leave", null, key, ifMatch: 1)).DataAsync();

        Assert.Equal("removed", replay.GetProperty("status").GetString());
        Assert.Equal(first.Id(), replay.Id());
    }

    private async Task<(HttpClient Admin, HttpClient Member, Guid OrgId, Guid MembershipId)> SetUpAsync()
    {
        var admin = host.Client(Ids.Subject("admin"));
        var member = host.Client(Ids.Subject("member"));
        var orgId = await admin.CreateOrganizationAsync();
        var membershipId = await host.AddMemberAsync(orgId, member);
        return (admin, member, orgId, membershipId);
    }

    private static async Task<JsonElement> SetAccessAsync(HttpClient admin, Guid orgId, Guid membershipId, string role, object[] grants, long version) =>
        await (await admin.WriteAsync(HttpMethod.Put, $"/api/v1/organizations/{orgId}/memberships/{membershipId}/access",
            new { role, grants }, ifMatch: version)).DataAsync();
}
