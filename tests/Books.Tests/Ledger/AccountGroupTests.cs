using System.Net;
using System.Text.Json;
using Books.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Books.Tests.Ledger;

/// <summary>
/// Rule 15 (owner decision 2026-10-23): groups are headings that never take postings; accounts sit under a group
/// of the same type in the same ledger and never contain other accounts.
/// </summary>
public sealed class AccountGroupTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Accounts_sit_under_a_group_and_every_active_account_is_postable()
    {
        var (admin, orgId, ledger) = await new AccountTests(host).LedgerAsync();
        var current = await CreateGroupAsync(admin, orgId, ledger, "Current assets", "assets");

        var cash = await AccountTests.CreateAsync(admin, orgId, ledger, "1000", "Cash", "assets", current.Id());

        Assert.Equal(current.Id(), cash.GetProperty("group_id").GetGuid());
        Assert.True(cash.GetProperty("postable").GetBoolean());
        var groups = await (await admin.GetAsync(GroupsUrl(orgId, ledger), Ct)).DataAsync();
        Assert.False(Assert.Single(groups.EnumerateArray()).TryGetProperty("postable", out _));
    }

    [Fact]
    public async Task Accounts_cannot_be_placed_under_accounts()
    {
        var (admin, orgId, ledger) = await new AccountTests(host).LedgerAsync();
        var cash = await AccountTests.CreateAsync(admin, orgId, ledger, "1000", "Cash", "assets");

        var underAccount = await AccountTests.PostAsync(admin, orgId, ledger, new { code = "1010", name = "Petty", type = "assets", group_id = cash.Id() });
        var oldField = await AccountTests.PostAsync(admin, orgId, ledger, new { code = "1020", name = "Till", type = "assets", parent_id = cash.Id() });

        await underAccount.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        await oldField.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
    }

    [Fact]
    public async Task A_group_must_be_in_the_same_ledger_and_of_the_same_type()
    {
        var (admin, orgId, ledger) = await new AccountTests(host).LedgerAsync();
        var otherLedger = await admin.CreateLedgerAsync(orgId, "Other");
        var assets = await CreateGroupAsync(admin, orgId, ledger, "Assets", "assets");
        var elsewhere = await CreateGroupAsync(admin, orgId, otherLedger, "Assets", "assets");
        var sales = await AccountTests.CreateAsync(admin, orgId, ledger, "4000", "Sales", "income");

        var wrongType = await AccountTests.PostAsync(admin, orgId, ledger, new { code = "4100", name = "Fees", type = "income", group_id = assets.Id() });
        var wrongLedger = await AccountTests.PostAsync(admin, orgId, ledger, new { code = "1100", name = "Bank", type = "assets", group_id = elsewhere.Id() });
        var moveWrongType = await PatchAccountAsync(admin, orgId, ledger, sales.Id(), new { group_id = assets.Id() });
        var subgroupWrongType = await admin.PostAsync(GroupsUrl(orgId, ledger), new { name = "Income", type = "income", parent_id = assets.Id() });

        await wrongType.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        await wrongLedger.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        await moveWrongType.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        await subgroupWrongType.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
    }

    [Fact]
    public async Task The_database_keeps_an_accounts_group_inside_its_ledger()
    {
        var (admin, orgId, ledger) = await new AccountTests(host).LedgerAsync();
        var otherLedger = await admin.CreateLedgerAsync(orgId, "Other");
        var elsewhere = await CreateGroupAsync(admin, orgId, otherLedger, "Assets", "assets");
        await using var db = host.CreateDbContext();

        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO accounts (id, org_id, ledger_id, group_id, code, name, type, is_control, is_bank, active, created_at, version)
            VALUES ({Guid.NewGuid()}, {orgId}, {ledger}, {elsewhere.Id()}, '1100', 'Bank', 'assets', false, false, true, now(), 1)
            """, Ct));

        Assert.Equal("23503", error.SqlState);
    }

    [Fact]
    public async Task Groups_nest_without_cycles_and_accounts_can_leave_their_group()
    {
        var (admin, orgId, ledger) = await new AccountTests(host).LedgerAsync();
        var top = await CreateGroupAsync(admin, orgId, ledger, "Assets", "assets");
        var inner = await CreateGroupAsync(admin, orgId, ledger, "Current", "assets", top.Id());
        var cash = await AccountTests.CreateAsync(admin, orgId, ledger, "1000", "Cash", "assets", inner.Id());

        var self = await PatchGroupAsync(admin, orgId, ledger, top.Id(), new { parent_id = top.Id() });
        var cycle = await PatchGroupAsync(admin, orgId, ledger, top.Id(), new { parent_id = inner.Id() });
        var ungrouped = await (await PatchAccountAsync(admin, orgId, ledger, cash.Id(), new { group_id = (Guid?)null })).DataAsync();

        await self.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        await cycle.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        Assert.Equal(JsonValueKind.Null, ungrouped.GetProperty("group_id").ValueKind);
    }

    [Fact]
    public async Task Changing_an_accounts_type_requires_a_group_of_that_type()
    {
        var (admin, orgId, ledger) = await new AccountTests(host).LedgerAsync();
        var income = await CreateGroupAsync(admin, orgId, ledger, "Income", "income");
        var expenses = await CreateGroupAsync(admin, orgId, ledger, "Expenses", "cost_and_expense");
        var fees = await AccountTests.CreateAsync(admin, orgId, ledger, "4000", "Fees", "income", income.Id());

        var typeOnly = await PatchAccountAsync(admin, orgId, ledger, fees.Id(), new { type = "cost_and_expense" });
        var withGroup = await (await PatchAccountAsync(admin, orgId, ledger, fees.Id(), new { type = "cost_and_expense", group_id = expenses.Id() })).DataAsync();

        await typeOnly.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        Assert.Equal("cost_and_expense", withGroup.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Only_empty_groups_can_be_deleted()
    {
        var (admin, orgId, ledger) = await new AccountTests(host).LedgerAsync();
        var holding = await CreateGroupAsync(admin, orgId, ledger, "Holding", "assets");
        var empty = await CreateGroupAsync(admin, orgId, ledger, "Empty", "assets");
        await AccountTests.CreateAsync(admin, orgId, ledger, "1000", "Cash", "assets", holding.Id());

        var notEmpty = await admin.WriteAsync(HttpMethod.Delete, $"{GroupsUrl(orgId, ledger)}/{holding.Id()}", null, ifMatch: 1);
        await (await admin.WriteAsync(HttpMethod.Delete, $"{GroupsUrl(orgId, ledger)}/{empty.Id()}", null, ifMatch: 1)).DataAsync();

        await notEmpty.AssertProblemAsync(HttpStatusCode.Conflict, "GROUP_NOT_EMPTY");
        Assert.Single((await (await admin.GetAsync(GroupsUrl(orgId, ledger), Ct)).DataAsync()).EnumerateArray());
    }

    [Fact]
    public async Task Readers_see_groups_but_cannot_change_the_chart()
    {
        var (admin, orgId, ledger) = await new AccountTests(host).LedgerAsync();
        var group = await CreateGroupAsync(admin, orgId, ledger, "Assets", "assets");
        var cash = await AccountTests.CreateAsync(admin, orgId, ledger, "1000", "Cash", "assets", group.Id());
        using var reader = host.Client(Ids.Subject("reader"));
        var membership = await host.AddMemberAsync(orgId, reader);
        await (await admin.WriteAsync(HttpMethod.Put, $"/api/v1/organizations/{orgId}/memberships/{membership}/access",
            new { role = "member", grants = new[] { new { ledger_id = ledger, access = "read" } } }, ifMatch: 1)).DataAsync();

        Assert.Single((await (await reader.GetAsync(GroupsUrl(orgId, ledger), Ct)).DataAsync()).EnumerateArray());
        await (await reader.PostAsync(GroupsUrl(orgId, ledger), new { name = "X", type = "assets" })).AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
        await (await reader.WriteAsync(HttpMethod.Delete, $"/api/v1/organizations/{orgId}/ledgers/{ledger}/accounts/{cash.Id()}", null, ifMatch: 1))
            .AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    private static string GroupsUrl(Guid orgId, Guid ledgerId) => $"/api/v1/organizations/{orgId}/ledgers/{ledgerId}/groups";

    private static async Task<JsonElement> CreateGroupAsync(HttpClient client, Guid orgId, Guid ledgerId, string name, string type, Guid? parentId = null) =>
        await (await client.PostAsync(GroupsUrl(orgId, ledgerId), new { name, type, parent_id = parentId })).DataAsync(HttpStatusCode.Created);

    private static Task<HttpResponseMessage> PatchGroupAsync(HttpClient client, Guid orgId, Guid ledgerId, Guid groupId, object body) =>
        client.WriteAsync(HttpMethod.Patch, $"{GroupsUrl(orgId, ledgerId)}/{groupId}", body, ifMatch: 1);

    private static Task<HttpResponseMessage> PatchAccountAsync(HttpClient client, Guid orgId, Guid ledgerId, Guid accountId, object body) =>
        client.WriteAsync(HttpMethod.Patch, $"/api/v1/organizations/{orgId}/ledgers/{ledgerId}/accounts/{accountId}", body, ifMatch: 1);
}
