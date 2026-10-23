using System.Net;
using System.Text.Json;
using Books.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Books.Tests.Ledger;

/// <summary>Rule 16 for changes: stable IDs and unique codes.</summary>
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
