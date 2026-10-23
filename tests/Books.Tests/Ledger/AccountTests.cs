using System.Net;
using System.Text.Json;
using Books.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Books.Tests.Ledger;

/// <summary>Rules 15 and 16: a per-ledger tree of typed accounts; codes unique within a ledger.</summary>
public sealed class AccountTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_duplicate_code_in_the_same_ledger_is_rejected_but_other_ledgers_may_reuse_it()
    {
        var (admin, orgId, ledger) = await LedgerAsync();
        var sibling = await admin.CreateLedgerAsync(orgId, "Sibling");
        using var other = host.Client(Ids.Subject("other"));
        var otherOrg = await other.CreateOrganizationAsync();
        var foreign = await other.CreateLedgerAsync(otherOrg);

        await CreateAsync(admin, orgId, ledger, "1000", "Cash", "assets");
        var duplicate = await PostAsync(admin, orgId, ledger, new { code = " 1000 ", name = "Cash again", type = "assets" });
        await CreateAsync(admin, orgId, sibling, "1000", "Cash", "assets");
        await CreateAsync(other, otherOrg, foreign, "1000", "Cash", "assets");

        await duplicate.AssertProblemAsync(HttpStatusCode.Conflict, "ACCOUNT_CODE_TAKEN");
    }

    [Fact]
    public async Task The_database_rejects_a_duplicate_code_even_when_the_service_is_bypassed()
    {
        var (admin, orgId, ledger) = await LedgerAsync();
        var cash = await CreateAsync(admin, orgId, ledger, "1000", "Cash", "assets");
        await using var db = host.CreateDbContext();

        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO accounts (id, org_id, ledger_id, code, name, type, is_control, is_bank, active, created_at, version)
            VALUES ({Guid.NewGuid()}, {orgId}, {ledger}, '1000', 'Copy', 'assets', false, false, true, now(), 1)
            """, Ct));

        Assert.Equal("23505", error.SqlState);
        Assert.NotEqual(Guid.Empty, cash.Id());
    }

    [Fact]
    public async Task Only_leaf_accounts_are_postable()
    {
        var (admin, orgId, ledger) = await LedgerAsync();
        var parent = await CreateAsync(admin, orgId, ledger, "1000", "Current assets", "assets");

        var child = await CreateAsync(admin, orgId, ledger, "1010", "Cash", "assets", parent.Id());

        var accounts = (await ListAsync(admin, orgId, ledger)).EnumerateArray().ToDictionary(a => a.GetProperty("code").GetString()!);
        Assert.False(accounts["1000"].GetProperty("is_leaf").GetBoolean());
        Assert.False(accounts["1000"].GetProperty("postable").GetBoolean());
        Assert.True(accounts["1010"].GetProperty("postable").GetBoolean());
        Assert.Equal(parent.Id(), child.GetProperty("parent_id").GetGuid());
    }

    [Fact]
    public async Task A_parent_must_be_in_the_same_ledger_and_of_the_same_type()
    {
        var (admin, orgId, ledger) = await LedgerAsync();
        var otherLedger = await admin.CreateLedgerAsync(orgId, "Other");
        var assets = await CreateAsync(admin, orgId, ledger, "1000", "Assets", "assets");
        var elsewhere = await CreateAsync(admin, orgId, otherLedger, "1000", "Assets", "assets");

        var wrongType = await PostAsync(admin, orgId, ledger, new { code = "4000", name = "Sales", type = "income", parent_id = assets.Id() });
        var wrongLedger = await PostAsync(admin, orgId, ledger, new { code = "1100", name = "Bank", type = "assets", parent_id = elsewhere.Id() });

        await wrongType.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        await wrongLedger.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
    }

    [Fact]
    public async Task The_database_keeps_a_parent_inside_its_ledger()
    {
        var (admin, orgId, ledger) = await LedgerAsync();
        var otherLedger = await admin.CreateLedgerAsync(orgId, "Other");
        var elsewhere = await CreateAsync(admin, orgId, otherLedger, "1000", "Assets", "assets");
        await using var db = host.CreateDbContext();

        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO accounts (id, org_id, ledger_id, parent_id, code, name, type, is_control, is_bank, active, created_at, version)
            VALUES ({Guid.NewGuid()}, {orgId}, {ledger}, {elsewhere.Id()}, '1100', 'Bank', 'assets', false, false, true, now(), 1)
            """, Ct));

        Assert.Equal("23503", error.SqlState);
    }

    [Fact]
    public async Task Ledger_access_controls_who_reads_and_who_changes_accounts()
    {
        var (admin, orgId, ledger) = await LedgerAsync();
        var hidden = await admin.CreateLedgerAsync(orgId, "Hidden");
        using var reader = host.Client(Ids.Subject("reader"));
        var readerMembership = await host.AddMemberAsync(orgId, reader);
        await (await admin.WriteAsync(HttpMethod.Put, $"/api/v1/organizations/{orgId}/memberships/{readerMembership}/access",
            new { role = "member", grants = new[] { new { ledger_id = ledger, access = "read" } } }, ifMatch: 1)).DataAsync();
        await CreateAsync(admin, orgId, ledger, "1000", "Cash", "assets");
        using var outsider = host.Client(Ids.Subject("outsider"));
        await outsider.CreateOrganizationAsync();

        Assert.Equal(1, (await ListAsync(reader, orgId, ledger)).GetArrayLength());
        await (await PostAsync(reader, orgId, ledger, new { code = "2000", name = "Loan", type = "liabilities" })).AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
        await (await reader.GetAsync($"/api/v1/organizations/{orgId}/ledgers/{hidden}/accounts", Ct)).AssertProblemAsync(HttpStatusCode.NotFound, "NOT_FOUND");
        await (await outsider.GetAsync($"/api/v1/organizations/{orgId}/ledgers/{ledger}/accounts", Ct)).AssertProblemAsync(HttpStatusCode.NotFound, "NOT_FOUND");
    }

    internal async Task<(HttpClient Admin, Guid OrgId, Guid LedgerId)> LedgerAsync(string? subject = null)
    {
        var admin = host.Client(subject ?? Ids.Subject("admin"));
        var orgId = await admin.CreateOrganizationAsync();
        return (admin, orgId, await admin.CreateLedgerAsync(orgId));
    }

    internal static Task<HttpResponseMessage> PostAsync(HttpClient client, Guid orgId, Guid ledgerId, object body) =>
        client.PostAsync($"/api/v1/organizations/{orgId}/ledgers/{ledgerId}/accounts", body);

    internal static async Task<JsonElement> CreateAsync(HttpClient client, Guid orgId, Guid ledgerId, string code, string name, string type, Guid? parentId = null) =>
        await (await PostAsync(client, orgId, ledgerId, new { code, name, type, parent_id = parentId })).DataAsync(HttpStatusCode.Created);

    internal static async Task<JsonElement> ListAsync(HttpClient client, Guid orgId, Guid ledgerId) =>
        await (await client.GetAsync($"/api/v1/organizations/{orgId}/ledgers/{ledgerId}/accounts", Ct)).DataAsync();
}
