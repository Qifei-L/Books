using System.Net;
using Books.Tests.Documents;
using Books.Tests.Infrastructure;

namespace Books.Tests.Access;

/// <summary>
/// Organization role plus ledger role gives capabilities: a bookkeeper edits, an accountant also controls AR/AP,
/// a manager also changes settings. Existing read/edit grants keep working without a role.
/// </summary>
public sealed class LedgerRoleTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_accountant_may_post_a_manual_journal_to_AP_but_a_bookkeeper_may_not()
    {
        var s = await new PurchaseInvoiceTests(host).SetUpAsync();
        using var accountant = await MemberAsync(s, "accountant");
        using var bookkeeper = await MemberAsync(s, "bookkeeper");

        var allowed = await accountant.PostAsync($"{s.LedgerUrl}/journals", Body(s));
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
        Assert.Equal("posted", (await allowed.DataAsync(HttpStatusCode.Created)).GetProperty("status").GetString());

        await (await bookkeeper.PostAsync($"{s.LedgerUrl}/journals", Body(s)))
            .AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task The_membership_response_names_the_ledger_role()
    {
        var s = await new PurchaseInvoiceTests(host).SetUpAsync();
        using var accountant = await MemberAsync(s, "accountant");

        var mine = await (await accountant.GetAsync($"/api/v1/organizations/{s.Books.OrgId}/memberships/me", Ct)).DataAsync();
        Assert.Equal("accountant", mine.GetProperty("grants")[0].GetProperty("role").GetString());
        Assert.Equal("edit", mine.GetProperty("grants")[0].GetProperty("access").GetString());
    }

    [Fact]
    public async Task A_viewer_cannot_take_edit_access()
    {
        var s = await new PurchaseInvoiceTests(host).SetUpAsync();
        using var member = host.Client(Ids.Subject("viewer"));
        var membership = await host.AddMemberAsync(s.Books.OrgId, member);

        var response = await s.Books.Admin.WriteAsync(HttpMethod.Put,
            $"/api/v1/organizations/{s.Books.OrgId}/memberships/{membership}/access",
            new { role = "member", grants = new[] { new { ledger_id = s.Books.LedgerId, access = "edit", role = "viewer" } } }, ifMatch: 1);

        await response.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
    }

    private async Task<HttpClient> MemberAsync(PurchaseInvoiceTests.Setup s, string role)
    {
        var member = host.Client(Ids.Subject(role));
        var membership = await host.AddMemberAsync(s.Books.OrgId, member);
        await (await s.Books.Admin.WriteAsync(HttpMethod.Put,
            $"/api/v1/organizations/{s.Books.OrgId}/memberships/{membership}/access",
            new { role = "member", grants = new[] { new { ledger_id = s.Books.LedgerId, access = "edit", role } } }, ifMatch: 1)).DataAsync();
        return member;
    }

    private static object Body(PurchaseInvoiceTests.Setup s) => new
    {
        accounting_date = "2026-10-25", post = true, reference = "MJ-ROLE",
        lines = new object[]
        {
            new { account_id = s.Account("2000"), credit = "10.00", party_id = s.Supplier, description = "Manual AP" },
            new { account_id = s.Rent, debit = "10.00", description = "Rent" },
        },
    };
}
