using System.Net;
using System.Text.Json;
using Books.Tests.Infrastructure;
using Books.Tests.Ledger;

namespace Books.Tests.Documents;

/// <summary>State Charge is its own sales-line field and return box, no longer reused through the other-tax field.</summary>
public sealed class StateChargeTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_state_charge_posts_on_its_own_line_and_adds_to_the_gross()
    {
        var s = await new SalesInvoiceTests(host).SetUpAsync();
        var (code, account) = await StateChargeCodeAsync(s);

        var invoice = await (await s.Books.Admin.PostAsync(s.Url, new
        {
            customer_id = s.Customer, date = "2026-10-15",
            lines = new object[] { new { account_id = s.Services, amount = "100.00", state_charge_code_id = code } },
        })).DataAsync(HttpStatusCode.Created);

        Assert.Equal("posted", invoice.GetProperty("status").GetString());
        Assert.Equal("0.00", invoice.GetProperty("other_tax_total").GetString());
        Assert.Equal("1.00", invoice.GetProperty("state_charge_total").GetString());
        Assert.Equal("101.00", invoice.GetProperty("gross_total").GetString());
        Assert.Equal(code, invoice.GetProperty("lines")[0].GetProperty("state_charge_code_id").GetGuid());
        Assert.Equal("1.00", invoice.GetProperty("lines")[0].GetProperty("state_charge_amount").GetString());
        Assert.Equal(JsonValueKind.Null, invoice.GetProperty("lines")[0].GetProperty("other_tax_code_id").ValueKind);

        var journal = await (await s.Books.Admin.GetAsync($"{s.Books.JournalsUrl}/{invoice.GetProperty("journal_id").GetGuid()}", Ct)).DataAsync();
        var lines = journal.GetProperty("lines").EnumerateArray().ToDictionary(l => l.GetProperty("account_id").GetGuid(), l => Get(l, "credit"));
        Assert.Equal("1.00", lines[account]);
        Assert.Contains(journal.GetProperty("lines").EnumerateArray(), l => l.GetProperty("description").GetString()!.Contains("state charge"));
    }

    [Fact]
    public async Task State_charge_has_its_own_return_box_and_the_other_tax_field_rejects_it()
    {
        var s = await new SalesInvoiceTests(host).SetUpAsync();
        var (code, _) = await StateChargeCodeAsync(s);
        await (await s.Books.Admin.PostAsync(s.Url, new
        {
            customer_id = s.Customer, date = "2026-10-15",
            lines = new object[] { new { account_id = s.Services, amount = "100.00", state_charge_code_id = code } },
        })).DataAsync(HttpStatusCode.Created);

        var boxes = TaxReturnTests.Boxes(await TaxReturnTests.GetAsync(s, "tax-returns/2026-10"));
        Assert.Equal("100.00", boxes["t701_state_charge_base"].Usd);
        Assert.Equal("1.00", boxes["t701_state_charge"].Usd);

        var reused = await s.Books.Admin.PostAsync(s.Url, new
        {
            customer_id = s.Customer, date = "2026-10-15",
            lines = new object[] { new { account_id = s.Services, amount = "100.00", other_tax_code_id = code } },
        });
        await reused.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
    }

    [Fact]
    public async Task A_state_charge_code_without_the_right_kind_is_rejected()
    {
        var s = await new SalesInvoiceTests(host).SetUpAsync();

        var response = await s.Books.Admin.PostAsync(s.Url, new
        {
            customer_id = s.Customer, date = "2026-10-15",
            lines = new object[] { new { account_id = s.Services, amount = "100.00", state_charge_code_id = s.Taxes["VAT 10% (sales)"] } },
        });

        await response.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
    }

    private static async Task<(Guid Code, Guid Account)> StateChargeCodeAsync(SalesInvoiceTests.Setup s)
    {
        var account = await AccountTests.CreateAsync(s.Books.Admin, s.Books.OrgId, s.Books.LedgerId, "2220", "State charge payable", "liabilities");
        var version = long.Parse(account.GetProperty("version").GetString()!);
        await (await s.Books.Admin.WriteAsync(HttpMethod.Put, $"{s.LedgerUrl}/accounts/{account.Id()}/usages",
            new { usages = new[] { "other_tax_payable" } }, ifMatch: version)).DataAsync();
        var code = await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/tax-codes", new
        {
            name = "State Charge 1%", kind = "state_charge", rate = "1", account_id = account.Id(), return_category = "state_charge",
        })).DataAsync(HttpStatusCode.Created);
        return (code.Id(), account.Id());
    }

    private static string Get(JsonElement element, string property) => element.GetProperty(property).GetString()!;
}
