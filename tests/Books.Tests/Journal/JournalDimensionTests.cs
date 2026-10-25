using System.Net;
using System.Text.Json;
using Books.Tests.Documents;
using Books.Tests.Infrastructure;

namespace Books.Tests.Journal;

/// <summary>Manual journals carry the same party, class, location and tax dimensions as document postings.</summary>
public sealed class JournalDimensionTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_manual_journal_controls_AP_with_a_party_and_ages_with_the_control_balance()
    {
        var s = await new PurchaseInvoiceTests(host).SetUpAsync();
        var posted = await PostAsync(s.Books.Admin, $"{s.LedgerUrl}/journals", new
        {
            accounting_date = "2026-10-25", post = true, reference = "MJ-AP",
            lines = new object[]
            {
                new { account_id = s.Account("2000"), credit = "100.00", party_id = s.Supplier, description = "Manual AP credit" },
                new { account_id = s.Rent, debit = "100.00", description = "Manual expense" },
            },
        });

        Assert.Equal("posted", posted.GetProperty("status").GetString());
        var aging = await GetAsync(s.Books.Admin, $"{s.LedgerUrl}/reports/ap-aging?asOf=2026-10-25");
        Assert.Equal("100.00", Text(aging, "control_balance"));
        Assert.True(aging.GetProperty("matches_control").GetBoolean());
        Assert.Equal("100.00", Text(aging.GetProperty("rows")[0], "total"));
    }

    [Fact]
    public async Task A_manual_journal_with_tax_posts_net_and_tax_and_keeps_the_direction()
    {
        var s = await new PurchaseInvoiceTests(host).SetUpAsync();
        var tax = s.Taxes["VAT 10% (purchases)"];
        var posted = await PostAsync(s.Books.Admin, $"{s.LedgerUrl}/journals", new
        {
            accounting_date = "2026-10-25", post = true, reference = "MJ-TAX",
            lines = new object[]
            {
                new { account_id = s.Rent, debit = "110.00", tax_code_id = tax, description = "Rent gross" },
                new { account_id = s.Books.Cash, credit = "110.00", description = "Paid" },
            },
        });

        Assert.Equal("posted", posted.GetProperty("status").GetString());
        var lines = posted.GetProperty("lines").EnumerateArray().ToList();
        Assert.Equal(3, lines.Count);
        var net = lines.Single((l) => l.TryGetProperty("tax_code_id", out var id) && id.ValueKind == JsonValueKind.String && id.GetGuid() == tax);
        Assert.Equal("100.00", Text(net, "debit"));
        Assert.Equal("10.00", Text(net, "tax_amount"));
        Assert.Equal("purchase", Text(net, "tax_direction"));
        var taxLine = lines.Single((l) => (!l.TryGetProperty("tax_code_id", out var id) || id.ValueKind == JsonValueKind.Null) && Text(l, "debit") == "10.00");
        Assert.Equal("10.00", Text(taxLine, "debit"));
    }

    [Fact]
    public async Task A_manual_journal_keeps_class_and_location_on_the_line_and_in_GL()
    {
        var books = await JournalBooks.CreateAsync(host);
        var ledger = $"/api/v1/organizations/{books.OrgId}/ledgers/{books.LedgerId}";
        var classId = (await (await books.Admin.PostAsync($"{ledger}/classes", new { name = "Retail", code = "R" })).DataAsync(HttpStatusCode.Created)).Id();
        var locationId = (await (await books.Admin.PostAsync($"{ledger}/locations", new { name = "Branch A" })).DataAsync(HttpStatusCode.Created)).Id();

        var posted = await PostAsync(books.Admin, $"{ledger}/journals", new
        {
            accounting_date = "2026-10-15", post = true,
            lines = new object[]
            {
                new { account_id = books.Cash, debit = "50.00", class_id = classId, location_id = locationId },
                new { account_id = books.Sales, credit = "50.00" },
            },
        });

        var cashLine = posted.GetProperty("lines")[0];
        Assert.Equal(classId, cashLine.GetProperty("class_id").GetGuid());
        Assert.Equal(locationId, cashLine.GetProperty("location_id").GetGuid());
        var gl = await GetAsync(books.Admin, $"{ledger}/reports/gl-detail?accountId={books.Cash}&from=2026-10-01&to=2026-10-31");
        Assert.Equal(classId, gl.GetProperty("lines")[0].GetProperty("class_id").GetGuid());
        Assert.Equal(locationId, gl.GetProperty("lines")[0].GetProperty("location_id").GetGuid());
    }

    private static async Task<JsonElement> PostAsync(HttpClient client, string url, object body) =>
        await (await client.PostAsync(url, body)).DataAsync(HttpStatusCode.Created);

    private static async Task<JsonElement> GetAsync(HttpClient client, string url) =>
        await (await client.GetAsync(url, Ct)).DataAsync();

    private static string Text(JsonElement element, string property) => element.GetProperty(property).GetString()!;
}
