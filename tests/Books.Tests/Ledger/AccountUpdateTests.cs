using System.Net;
using System.Text.Json;
using Books.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Books.Tests.Ledger;

/// <summary>Rules 15 and 16 for changes: stable IDs, unique codes, and a tree without cycles.</summary>
public sealed class AccountUpdateTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Changing_a_code_keeps_the_account_id_and_cannot_take_another_accounts_code()
    {
        var (admin, orgId, ledger) = await new AccountTests(host).LedgerAsync();
        var cash = await AccountTests.CreateAsync(admin, orgId, ledger, "1000", "Cash", "assets");
        await AccountTests.CreateAsync(admin, orgId, ledger, "1100", "Bank", "assets");

        var renamed = await (await PatchAsync(admin, orgId, ledger, cash.Id(), new { code = "1010", name = "Petty cash" }, 1)).DataAsync();
        var taken = await PatchAsync(admin, orgId, ledger, cash.Id(), new { code = "1100" }, 2);

        Assert.Equal(cash.Id(), renamed.Id());
        Assert.Equal("1010", renamed.GetProperty("code").GetString());
        await taken.AssertProblemAsync(HttpStatusCode.Conflict, "ACCOUNT_CODE_TAKEN");
    }

    [Fact]
    public async Task An_account_cannot_move_under_itself_or_a_descendant()
    {
        var (admin, orgId, ledger) = await new AccountTests(host).LedgerAsync();
        var top = await AccountTests.CreateAsync(admin, orgId, ledger, "1000", "Assets", "assets");
        var middle = await AccountTests.CreateAsync(admin, orgId, ledger, "1100", "Current", "assets", top.Id());
        var bottom = await AccountTests.CreateAsync(admin, orgId, ledger, "1110", "Cash", "assets", middle.Id());

        var self = await PatchAsync(admin, orgId, ledger, top.Id(), new { parent_id = top.Id() }, 1);
        var cycle = await PatchAsync(admin, orgId, ledger, top.Id(), new { parent_id = bottom.Id() }, 1);
        var toTop = await (await PatchAsync(admin, orgId, ledger, bottom.Id(), new { parent_id = (Guid?)null }, 1)).DataAsync();

        await self.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        await cycle.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        Assert.Equal(JsonValueKind.Null, toTop.GetProperty("parent_id").ValueKind);
        var accounts = (await AccountTests.ListAsync(admin, orgId, ledger)).EnumerateArray().ToDictionary(a => a.GetProperty("code").GetString()!);
        Assert.True(accounts["1100"].GetProperty("is_leaf").GetBoolean());
    }

    [Fact]
    public async Task A_parent_of_another_type_is_rejected_and_types_change_only_on_leaves()
    {
        var (admin, orgId, ledger) = await new AccountTests(host).LedgerAsync();
        var assets = await AccountTests.CreateAsync(admin, orgId, ledger, "1000", "Assets", "assets");
        await AccountTests.CreateAsync(admin, orgId, ledger, "1010", "Cash", "assets", assets.Id());
        var sales = await AccountTests.CreateAsync(admin, orgId, ledger, "4000", "Sales", "income");

        var wrongParent = await PatchAsync(admin, orgId, ledger, sales.Id(), new { parent_id = assets.Id() }, 1);
        var parentType = await PatchAsync(admin, orgId, ledger, assets.Id(), new { type = "liabilities" }, 1);
        var leafType = await (await PatchAsync(admin, orgId, ledger, sales.Id(), new { type = "cost_and_expense" }, 1)).DataAsync();

        await wrongParent.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        await parentType.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        Assert.Equal("cost_and_expense", leafType.GetProperty("type").GetString());
    }

    [Fact]
    public async Task A_deactivated_account_is_not_postable()
    {
        var (admin, orgId, ledger) = await new AccountTests(host).LedgerAsync();
        var cash = await AccountTests.CreateAsync(admin, orgId, ledger, "1000", "Cash", "assets");

        var updated = await (await PatchAsync(admin, orgId, ledger, cash.Id(), new { active = false }, 1)).DataAsync();

        Assert.False(updated.GetProperty("active").GetBoolean());
        Assert.False(updated.GetProperty("postable").GetBoolean());
    }

    [Fact]
    public async Task Updates_need_the_current_version_edit_access_and_known_fields_and_are_audited()
    {
        var (admin, orgId, ledger) = await new AccountTests(host).LedgerAsync();
        var cash = await AccountTests.CreateAsync(admin, orgId, ledger, "1000", "Cash", "assets");
        using var reader = host.Client(Ids.Subject("reader"));
        var readerMembership = await host.AddMemberAsync(orgId, reader);
        await (await admin.WriteAsync(HttpMethod.Put, $"/api/v1/organizations/{orgId}/memberships/{readerMembership}/access",
            new { role = "member", grants = new[] { new { ledger_id = ledger, access = "read" } } }, ifMatch: 1)).DataAsync();

        await (await PatchAsync(admin, orgId, ledger, cash.Id(), new { name = "X" }, null)).AssertProblemAsync(HttpStatusCode.PreconditionRequired, "PRECONDITION_REQUIRED");
        await (await PatchAsync(admin, orgId, ledger, cash.Id(), new { name = "X" }, 9)).AssertProblemAsync(HttpStatusCode.PreconditionFailed, "VERSION_CONFLICT");
        await (await PatchAsync(admin, orgId, ledger, cash.Id(), new { ledger_id = Guid.NewGuid() }, 1)).AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        await (await PatchAsync(reader, orgId, ledger, cash.Id(), new { name = "X" }, 1)).AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
        await (await PatchAsync(admin, orgId, ledger, cash.Id(), new { name = "Cash on hand" }, 1)).DataAsync();

        await using var db = host.CreateDbContext();
        var audit = await db.AuditEvents.SingleAsync(a => a.TargetId == cash.Id() && a.Action == "account.updated", Ct);
        Assert.Contains("Cash on hand", audit.Changes);
    }

    private static Task<HttpResponseMessage> PatchAsync(HttpClient client, Guid orgId, Guid ledgerId, Guid accountId, object body, long? version) =>
        client.WriteAsync(HttpMethod.Patch, $"/api/v1/organizations/{orgId}/ledgers/{ledgerId}/accounts/{accountId}", body, ifMatch: version);
}
