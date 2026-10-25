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

    [Fact]
    public async Task A_document_line_class_flows_to_the_journal_and_GL()
    {
        var s = await new PurchaseInvoiceTests(host).SetUpAsync();
        var classId = (await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/classes", new { name = "Projects" })).DataAsync(HttpStatusCode.Created)).Id();
        var invoice = await PostAsync(s.Books.Admin, s.Url, new
        {
            supplier_id = s.Supplier, date = "2026-10-15", reference = "CLASS-1",
            lines = new object[] { new { account_id = s.Rent, amount = "100.00", class_id = classId } },
        });

        Assert.Equal(classId, invoice.GetProperty("lines")[0].GetProperty("class_id").GetGuid());
        var gl = await GetAsync(s.Books.Admin, $"{s.LedgerUrl}/reports/gl-detail?accountId={s.Rent}&from=2026-10-01&to=2026-10-31");
        Assert.Equal(classId, gl.GetProperty("lines")[0].GetProperty("class_id").GetGuid());
    }

    [Fact]
    public async Task Editing_a_tax_journal_does_not_duplicate_the_generated_tax_line()
    {
        var s = await new PurchaseInvoiceTests(host).SetUpAsync();
        var tax = s.Taxes["VAT 10% (purchases)"];
        var body = new
        {
            accounting_date = "2026-10-25", post = true, reference = "MJ-TAX-EDIT",
            lines = new object[]
            {
                new { account_id = s.Rent, debit = "110.00", tax_code_id = tax, description = "Rent gross" },
                new { account_id = s.Books.Cash, credit = "110.00", description = "Paid" },
            },
        };
        var posted = await PostAsync(s.Books.Admin, $"{s.LedgerUrl}/journals", body);
        var edited = await (await s.Books.Admin.WriteAsync(HttpMethod.Put, $"{s.LedgerUrl}/journals/{posted.Id()}",
            body, ifMatch: long.Parse(Text(posted, "version")))).DataAsync();

        Assert.Equal("posted", edited.GetProperty("status").GetString());
        Assert.Equal(3, edited.GetProperty("lines").GetArrayLength());
        Assert.Single(edited.GetProperty("lines").EnumerateArray(), (l) => l.TryGetProperty("tax_line", out var t) && t.GetBoolean());
    }

    [Fact]
    public async Task Reports_filter_by_class_without_changing_the_unfiltered_totals()
    {
        var books = await JournalBooks.CreateAsync(host);
        var ledger = $"/api/v1/organizations/{books.OrgId}/ledgers/{books.LedgerId}";
        var retail = (await (await books.Admin.PostAsync($"{ledger}/classes", new { name = "Retail" })).DataAsync(HttpStatusCode.Created)).Id();
        var wholesale = (await (await books.Admin.PostAsync($"{ledger}/classes", new { name = "Wholesale" })).DataAsync(HttpStatusCode.Created)).Id();
        await PostAsync(books.Admin, $"{ledger}/journals", new
        {
            accounting_date = "2026-10-15", post = true,
            lines = new object[]
            {
                new { account_id = books.Cash, debit = "100.00", class_id = retail },
                new { account_id = books.Sales, credit = "100.00", class_id = retail },
            },
        });
        await PostAsync(books.Admin, $"{ledger}/journals", new
        {
            accounting_date = "2026-10-16", post = true,
            lines = new object[]
            {
                new { account_id = books.Cash, debit = "200.00", class_id = wholesale },
                new { account_id = books.Sales, credit = "200.00", class_id = wholesale },
            },
        });

        var all = await GetAsync(books.Admin, $"{ledger}/reports/trial-balance?from=2026-10-01&to=2026-10-31");
        var retailTb = await GetAsync(books.Admin, $"{ledger}/reports/trial-balance?from=2026-10-01&to=2026-10-31&classId={retail}");
        var cash = retailTb.GetProperty("rows").EnumerateArray().Single((r) => r.GetProperty("account_id").GetGuid() == books.Cash);
        Assert.Equal("100.00", Text(cash, "period_debit"));
        Assert.Equal("300.00", Text(all.GetProperty("totals"), "period_debit"));

        var pl = await GetAsync(books.Admin, $"{ledger}/reports/profit-and-loss?from=2026-10-01&to=2026-10-31&classId={retail}");
        Assert.Equal("100.00", Text(pl, "net_profit"));
        var gl = await GetAsync(books.Admin, $"{ledger}/reports/gl-detail?accountId={books.Cash}&from=2026-10-01&to=2026-10-31&classId={retail}");
        Assert.Equal("100.00", Text(gl, "period_debit"));
        var bs = await GetAsync(books.Admin, $"{ledger}/reports/balance-sheet?asOf=2026-10-31&classId={retail}");
        Assert.Equal("100.00", Text(bs, "total_assets"));
    }

    private static async Task<JsonElement> PostAsync(HttpClient client, string url, object body) =>
        await (await client.PostAsync(url, body)).DataAsync(HttpStatusCode.Created);

    private static async Task<JsonElement> GetAsync(HttpClient client, string url) =>
        await (await client.GetAsync(url, Ct)).DataAsync();

    private static string Text(JsonElement element, string property) => element.GetProperty(property).GetString()!;
}
