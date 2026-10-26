using System.Net;
using Books.Tests.Infrastructure;
using Books.Tests.Ledger;

namespace Books.Tests.Documents;

/// <summary>
/// GDT has VAT, not a separate State Charge tax. "State Charge" is only a print label for VAT paid on behalf of the
/// state; the accounting and the return use the normal output VAT code and boxes.
/// </summary>
public sealed class StateChargeTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task State_charge_is_not_a_tax_kind()
    {
        var s = await new SalesInvoiceTests(host).SetUpAsync();

        var response = await s.Books.Admin.PostAsync($"{s.LedgerUrl}/tax-codes", new
        {
            name = "State Charge 10%", kind = "state_charge", rate = "10", account_id = s.Account("2400"),
        });

        await response.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
    }

    [Fact]
    public async Task A_state_charge_print_is_the_normal_VAT_invoice()
    {
        var s = await new SalesInvoiceTests(host).SetUpAsync();
        var invoice = await (await s.Books.Admin.PostAsync(s.Url, new
        {
            customer_id = s.Customer, date = "2026-10-15",
            lines = new object[] { new { account_id = s.Services, amount = "100.00", tax_code_id = s.Taxes["VAT 10% (sales)"] } },
        })).DataAsync(HttpStatusCode.Created);
        Assert.Equal("10.00", invoice.GetProperty("tax_total").GetString());
        Assert.Equal("110.00", invoice.GetProperty("gross_total").GetString());
        Assert.False(invoice.TryGetProperty("state_charge_total", out _));

        var pdf = await s.Books.Admin.GetAsync($"{s.Url}/{invoice.Id()}/pdf?stateCharge=true", Ct);
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        Assert.Equal("tax", pdf.Headers.GetValues("Invoice-Style").Single());
        Assert.True((await pdf.Content.ReadAsByteArrayAsync(Ct)).Length > 1000);

        var boxes = TaxReturnTests.Boxes(await (await s.Books.Admin.GetAsync($"{s.LedgerUrl}/tax-returns/2026-10", Ct)).DataAsync());
        Assert.Equal("10.00", boxes["t701_15"].Usd);
        Assert.False(boxes.ContainsKey("t701_state_charge"));
    }

    [Fact]
    public async Task State_charge_is_not_a_return_category_either()
    {
        var s = await new SalesInvoiceTests(host).SetUpAsync();

        var response = await s.Books.Admin.PostAsync($"{s.LedgerUrl}/tax-codes", new
        {
            name = "VAT 10% with a state charge category", kind = "vat_output", rate = "10",
            account_id = s.Account("2100"), return_category = "state_charge",
        });

        await response.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
    }

    private static string Text(System.Text.Json.JsonElement element, string property) => element.GetProperty(property).GetString()!;
}
