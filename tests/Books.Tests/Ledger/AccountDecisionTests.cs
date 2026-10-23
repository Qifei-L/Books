using System.Net;
using Books.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Books.Tests.Ledger;

/// <summary>Owner decisions of 2026-10-23: case-insensitive codes (rule 16) and deleting unused accounts (rule 19).</summary>
public sealed class AccountDecisionTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Codes_that_differ_only_in_case_are_the_same_code()
    {
        var (admin, orgId, ledger) = await new AccountTests(host).LedgerAsync();
        var a100 = await AccountTests.CreateAsync(admin, orgId, ledger, "A100", "Cash", "assets");
        await AccountTests.CreateAsync(admin, orgId, ledger, "B200", "Bank", "assets");

        var create = await AccountTests.PostAsync(admin, orgId, ledger, new { code = "a100", name = "Copy", type = "assets" });
        var rename = await admin.WriteAsync(HttpMethod.Patch, AccountUrl(orgId, ledger, a100.Id()), new { code = "b200" }, ifMatch: 1);
        var ownCase = await admin.WriteAsync(HttpMethod.Patch, AccountUrl(orgId, ledger, a100.Id()), new { code = "a100" }, ifMatch: 1);

        await create.AssertProblemAsync(HttpStatusCode.Conflict, "ACCOUNT_CODE_TAKEN");
        await rename.AssertProblemAsync(HttpStatusCode.Conflict, "ACCOUNT_CODE_TAKEN");
        Assert.Equal("a100", (await ownCase.DataAsync()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_database_rejects_a_code_differing_only_in_case()
    {
        var (admin, orgId, ledger) = await new AccountTests(host).LedgerAsync();
        await AccountTests.CreateAsync(admin, orgId, ledger, "A100", "Cash", "assets");
        await using var db = host.CreateDbContext();

        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO accounts (id, org_id, ledger_id, code, name, type, is_control, is_bank, active, created_at, version)
            VALUES ({Guid.NewGuid()}, {orgId}, {ledger}, 'a100', 'Copy', 'assets', false, false, true, now(), 1)
            """, Ct));

        Assert.Equal("23505", error.SqlState);
    }

    [Fact]
    public async Task An_unused_account_can_be_deleted_with_the_current_version_and_is_audited()
    {
        var (admin, orgId, ledger) = await new AccountTests(host).LedgerAsync();
        var cash = await AccountTests.CreateAsync(admin, orgId, ledger, "1000", "Cash", "assets");
        var url = AccountUrl(orgId, ledger, cash.Id());

        await (await admin.WriteAsync(HttpMethod.Delete, url, null)).AssertProblemAsync(HttpStatusCode.PreconditionRequired, "PRECONDITION_REQUIRED");
        await (await admin.WriteAsync(HttpMethod.Delete, url, null, ifMatch: 1)).DataAsync();

        Assert.Equal(0, (await AccountTests.ListAsync(admin, orgId, ledger)).GetArrayLength());
        await using var db = host.CreateDbContext();
        Assert.Equal(1, await db.AuditEvents.CountAsync(a => a.TargetId == cash.Id() && a.Action == "account.deleted", Ct));
    }

    private static string AccountUrl(Guid orgId, Guid ledgerId, Guid accountId) =>
        $"/api/v1/organizations/{orgId}/ledgers/{ledgerId}/accounts/{accountId}";
}
