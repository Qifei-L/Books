using System.Net;
using System.Text.Json;
using Books.Tests.Infrastructure;
using Books.Tests.Ledger;

namespace Books.Tests.Documents;

/// <summary>State Charge is a tax kind with its own account, posted through the other-tax line.</summary>
public sealed class StateChargeTests(TestHost host)
{
    [Fact]
    public async Task A_state_charge_code_posts_on_a_sales_line_and_adds_to_the_gross()
    {
        var s = await new SalesInvoiceTests(host).SetUpAsync();
        var account = await AccountTests.CreateAsync(s.Books.Admin, s.Books.OrgId, s.Books.LedgerId, "2220", "State charge payable", "liabilities");
        var version = long.Parse(account.GetProperty("version").GetString()!);
        await (await s.Books.Admin.WriteAsync(HttpMethod.Put, $"{s.LedgerUrl}/accounts/{account.Id()}/usages",
            new { usages = new[] { "other_tax_payable" } }, ifMatch: version)).DataAsync();
        var code = await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/tax-codes", new
        {
            name = "State Charge 1%", kind = "state_charge", rate = "1", account_id = account.Id(),
        })).DataAsync(HttpStatusCode.Created);

        var invoice = await (await s.Books.Admin.PostAsync(s.Url, new
        {
            customer_id = s.Customer, date = "2026-10-15",
            lines = new object[] { new { account_id = s.Services, amount = "100.00", other_tax_code_id = code.Id() } },
        })).DataAsync(HttpStatusCode.Created);

        Assert.Equal("posted", invoice.GetProperty("status").GetString());
        Assert.Equal("1.00", invoice.GetProperty("other_tax_total").GetString());
        Assert.Equal("101.00", invoice.GetProperty("gross_total").GetString());
        Assert.Equal(code.Id(), invoice.GetProperty("lines")[0].GetProperty("other_tax_code_id").GetGuid());
    }
}
