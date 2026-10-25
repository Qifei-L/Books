using System.Net;
using System.Text.Json;
using Books.Tests.Infrastructure;
using Books.Tests.Mcp;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Books.Tests.Documents;

/// <summary>
/// Stock adjustments and counts (M10.3): write-offs and count differences go out at the average cost, stock found
/// comes in at the entered or average cost, and the stock report keeps equalling the inventory account.
/// </summary>
public sealed class StockAdjustmentTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_write_off_and_a_count_go_out_at_the_average_cost()
    {
        var (s, item) = await SetUpAsync();
        await BuyAsync(s, item, "2026-10-01", "10", "50.00");
        await BuyAsync(s, item, "2026-10-05", "10", "70.00");

        var writeOff = await SaveAsync(s, new { date = "2026-10-10", description = "Damaged cases", lines = new[] { new { item_id = item, quantity = "-2" } } });
        var count = await SaveAsync(s, new { date = "2026-10-20", reference = "COUNT-10", lines = new[] { new { item_id = item, counted_quantity = "15" } } });

        Assert.Equal(("-2", "-12.00", "20"), (Line(writeOff, "quantity"), Line(writeOff, "value"), Line(writeOff, "book_quantity")));
        Assert.Equal(("-3", "-18.00", "18", "15"), (Line(count, "quantity"), Line(count, "value"), Line(count, "book_quantity"), Line(count, "counted_quantity")));
        var lines = await JournalAsync(s, writeOff);
        Assert.Equal(("12.00", "12.00"), (lines[s.Account("5800")].Dr, lines[s.Account("1400")].Cr));
        var row = await ReportRowAsync(s);
        Assert.Equal(("15", "6.00", "90.00"), (Get(row.Row, "on_hand"), Get(row.Row, "average_cost"), Get(row.Row, "value")));
        Assert.True(row.MatchesControl);
    }

    [Fact]
    public async Task Stock_found_comes_in_at_its_cost_and_counting_to_zero_clears_the_whole_value()
    {
        var (s, item) = await SetUpAsync();
        await BuyAsync(s, item, "2026-10-01", "3", "10.00");                       // average 3.3333…

        var found = await SaveAsync(s, new { date = "2026-10-02", lines = new[] { new { item_id = item, quantity = "2", unit_cost = "4.00" } } });
        var atAverage = await SaveAsync(s, new { date = "2026-10-03", lines = new[] { new { item_id = item, quantity = "1" } } });
        var cleared = await SaveAsync(s, new { date = "2026-10-04", lines = new[] { new { item_id = item, counted_quantity = "0" } } });

        Assert.Equal(("8.00", "3.60"), (Line(found, "value"), Line(atAverage, "value")));  // (10 + 8) / 5 = 3.60
        Assert.Equal(("-6", "-21.60"), (Line(cleared, "quantity"), Line(cleared, "value")));
        var row = await ReportRowAsync(s);
        Assert.Equal(("0", "0.00"), (Get(row.Row, "on_hand"), Get(row.Row, "value")));
        Assert.True(row.MatchesControl);
    }

    [Fact]
    public async Task Adjustments_are_checked_and_editing_or_deleting_one_changes_the_stock()
    {
        var (s, item) = await SetUpAsync();
        await BuyAsync(s, item, "2026-10-01", "10", "50.00");
        var plain = (await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/items", new { name = "Service hours" })).DataAsync(HttpStatusCode.Created)).Id();
        async Task Refused(object line) =>
            await (await s.Books.Admin.PostAsync(Url(s), new { date = "2026-10-10", lines = new[] { line } })).AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");

        await Refused(new { item_id = item, quantity = "-1", counted_quantity = "9" });
        await Refused(new { item_id = item, quantity = "-1", unit_cost = "5.00" });
        await Refused(new { item_id = item, counted_quantity = "10" });
        await Refused(new { item_id = plain, quantity = "1" });
        await Refused(new { item_id = item, quantity = "-1", account_id = s.Account("1200") });
        var adjustment = await SaveAsync(s, new { date = "2026-10-10", lines = new[] { new { item_id = item, quantity = "-1" } } });
        var edited = await (await s.Books.Admin.WriteAsync(HttpMethod.Put, $"{Url(s)}/{adjustment.Id()}",
            new { date = "2026-10-10", lines = new[] { new { item_id = item, quantity = "-4" } } }, ifMatch: 1)).DataAsync();
        var afterEdit = await ReportRowAsync(s);
        await (await s.Books.Admin.WriteAsync(HttpMethod.Delete, $"{Url(s)}/{adjustment.Id()}", null, ifMatch: 2)).DataAsync();
        var afterDelete = await ReportRowAsync(s);

        Assert.Equal(("-4", "-20.00"), (Line(edited, "quantity"), Line(edited, "value")));
        Assert.Equal(("6", "30.00", true), (Get(afterEdit.Row, "on_hand"), Get(afterEdit.Row, "value"), afterEdit.MatchesControl));
        Assert.Equal(("10", "50.00", true), (Get(afterDelete.Row, "on_hand"), Get(afterDelete.Row, "value"), afterDelete.MatchesControl));
    }

    [Fact]
    public async Task An_ai_drafts_a_count_and_a_person_posts_it()
    {
        var subject = Ids.Subject();
        var (s, item) = await SetUpAsync(subject);
        await BuyAsync(s, item, "2026-10-01", "10", "50.00");
        await using var mcp = await new McpReadToolTests(host).ConnectAsync(subject);

        var result = await mcp.CallToolAsync("save_stock_adjustment_draft", new Dictionary<string, object?>
        {
            ["org_id"] = s.Books.OrgId, ["ledger_id"] = s.Books.LedgerId, ["idempotency_key"] = Guid.NewGuid(), ["date"] = "2026-10-31", ["reference"] = "Count Oct",
            ["lines"] = new object[] { new Dictionary<string, object?> { ["item_id"] = item, ["counted_quantity"] = "8" } },
        }, cancellationToken: Ct);
        var text = ((TextContentBlock)result.Content[0]).Text;
        Assert.False(result.IsError ?? false, text);
        var draft = JsonDocument.Parse(text).RootElement.GetProperty("data");
        var posted = await (await s.Books.Admin.WriteAsync(HttpMethod.Put, $"{Url(s)}/{draft.Id()}",
            new { date = "2026-10-31", reference = "Count Oct", lines = new[] { new { item_id = item, counted_quantity = "8" } } }, ifMatch: 1)).DataAsync();

        Assert.Equal(("draft", "-2", "-10.00"), (draft.GetProperty("status").GetString(), Line(draft, "quantity"), Line(draft, "value")));
        Assert.Equal("posted", posted.GetProperty("status").GetString());
        Assert.Equal("8", Get((await ReportRowAsync(s)).Row, "on_hand"));
    }

    [Fact]
    public async Task An_amount_alone_revalues_the_stock_into_cost_of_sales_and_the_posting_shows_where_it_went()
    {
        var (s, item) = await SetUpAsync();
        await BuyAsync(s, item, "2026-10-01", "10", "50.00");                     // 10 at 5.00

        var preview = await (await s.Books.Admin.PostAsync($"{Url(s)}/preview", new
        {
            date = "2026-10-31", description = "Write down to net realizable value", lines = new[] { new { item_id = item, amount = "-20.00" } },
        })).DataAsync();
        var posted = await SaveAsync(s, new { date = "2026-10-31", description = "Write down to net realizable value", lines = new[] { new { item_id = item, amount = "-20.00" } } });

        Assert.Equal(("0", "-20.00", "preview"), (Line(preview, "quantity"), Line(preview, "value"), Get(preview, "status")));
        Assert.Equal(["1400 0.00 20.00", "5800 20.00 0.00"], Posting(s, preview));
        var journal = await JournalAsync(s, posted);
        Assert.Equal(("20.00", "20.00"), (journal[s.Account("5800")].Dr, journal[s.Account("1400")].Cr));
        var row = await ReportRowAsync(s);
        Assert.Equal(("10", "3.00", "30.00", true), (Get(row.Row, "on_hand"), Get(row.Row, "average_cost"), Get(row.Row, "value"), row.MatchesControl));
    }

    [Fact]
    public async Task Entries_split_the_other_side_like_a_journal_and_must_balance()
    {
        var (s, item) = await SetUpAsync();
        await BuyAsync(s, item, "2026-10-01", "10", "50.00");
        var loss = (await Ledger.AccountTests.CreateAsync(s.Books.Admin, s.Books.OrgId, s.Books.LedgerId, "6900", "Stock losses", "cost_and_expense")).Id();
        var staff = (await Ledger.AccountTests.CreateAsync(s.Books.Admin, s.Books.OrgId, s.Books.LedgerId, "1250", "Staff advances", "assets")).Id();
        var unbalanced = await s.Books.Admin.PostAsync(Url(s), new
        {
            date = "2026-10-20", lines = new[] { new { item_id = item, quantity = "-4" } },
            entries = new[] { new { account_id = loss, debit = "15.00" } },
        });
        var toZero = await s.Books.Admin.PostAsync(Url(s), new { date = "2026-10-20", lines = new[] { new { item_id = item, counted_quantity = "0", amount = "-49.00" } } });

        var split = await SaveAsync(s, new
        {
            date = "2026-10-20", reference = "THEFT-1", lines = new[] { new { item_id = item, quantity = "-4" } },        // 4 at 5.00 = 20.00 out
            entries = new object[] { new { account_id = loss, debit = "15.00", description = "Not insured" }, new { account_id = staff, debit = "5.00", description = "Recovered from staff" } },
        });

        await unbalanced.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        await toZero.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        var journal = await JournalAsync(s, split);
        Assert.Equal(("20.00", "15.00", "5.00"), (journal[s.Account("1400")].Cr, journal[loss].Dr, journal[staff].Dr));
        Assert.Equal(2, split.GetProperty("entries").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, split.GetProperty("lines")[0].GetProperty("account_id").ValueKind);
    }

    [Fact]
    public async Task An_items_movements_show_each_document_with_the_running_balance()
    {
        var (s, item) = await SetUpAsync();
        await BuyAsync(s, item, "2026-10-01", "10", "50.00");
        await (await s.Books.Admin.PostAsync(s.Url, new
        {
            customer_id = s.Customer, date = "2026-10-05", reference = "INV-77",
            lines = new[] { new { account_id = s.Books.Sales, item_id = item, quantity = "4", unit_price = "9.00" } },
        })).DataAsync(HttpStatusCode.Created);
        await SaveAsync(s, new { date = "2026-10-20", reference = "WO-1", lines = new[] { new { item_id = item, quantity = "-1" } } });

        var report = await (await s.Books.Admin.GetAsync($"{s.LedgerUrl}/reports/inventory/{item}/movements?from=2026-10-03&to=2026-10-31", Ct)).DataAsync();

        Assert.Equal(("10", "50.00", "5", "25.00"), (Get(report, "opening_quantity"), Get(report, "opening_value"), Get(report, "closing_quantity"), Get(report, "closing_value")));
        Assert.Equal(["sales_invoice INV-77 out 4 -20.00 = 6 30.00", "stock_adjustment WO-1 out 1 -5.00 = 5 25.00"],
            report.GetProperty("lines").EnumerateArray().Select(l =>
                $"{Get(l, "kind")} {Get(l, "reference")} out {Get(l, "quantity_out")} {Get(l, "value")} = {Get(l, "balance_quantity")} {Get(l, "balance_value")}"));
    }

    [Fact]
    public async Task Stock_may_go_below_zero_with_a_warning()
    {
        var (s, item) = await SetUpAsync();
        await BuyAsync(s, item, "2026-10-01", "10", "50.00");

        var sale = await (await s.Books.Admin.PostAsync(s.Url, new
        {
            customer_id = s.Customer, date = "2026-10-10", lines = new[] { new { account_id = s.Books.Sales, item_id = item, quantity = "12", unit_price = "9.00" } },
        })).DataAsync(HttpStatusCode.Created);
        var adjustment = await SaveAsync(s, new { date = "2026-10-12", lines = new[] { new { item_id = item, quantity = "-1" } } });
        var fine = await SaveAsync(s, new { date = "2026-09-01", lines = new[] { new { item_id = item, quantity = "5", unit_cost = "5.00" } } });

        Assert.Equal("posted", Get(sale, "status"));
        Assert.Contains("goes below zero on 2026-10-10 (-2 on hand)", Assert.Single(sale.GetProperty("warnings").EnumerateArray()).GetString());
        Assert.Contains("(-3 on hand)", Assert.Single(adjustment.GetProperty("warnings").EnumerateArray()).GetString());
        Assert.Equal(JsonValueKind.Null, fine.GetProperty("warnings").ValueKind);
    }

    private static List<string> Posting(SalesInvoiceTests.Setup s, JsonElement adjustment) =>
        adjustment.GetProperty("posting").EnumerateArray()
            .Select(p => $"{s.Codes.Single(c => c.Value == p.GetProperty("account_id").GetGuid()).Key} {Get(p, "debit")} {Get(p, "credit")}").ToList();

    private static string Url(SalesInvoiceTests.Setup s) => $"{s.LedgerUrl}/stock-adjustments";

    private async Task<(SalesInvoiceTests.Setup, Guid Item)> SetUpAsync(string? subject = null)
    {
        var s = await new SalesInvoiceTests(host).SetUpAsync(subject);
        var item = (await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/items", new { name = "Bottled water", unit = "case", track_inventory = true }))
            .DataAsync(HttpStatusCode.Created)).Id();
        return (s, item);
    }

    private static async Task BuyAsync(SalesInvoiceTests.Setup s, Guid item, string date, string quantity, string amount)
    {
        var supplier = (await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/parties", new { name = $"Wholesaler {Guid.NewGuid():N}", is_supplier = true }))
            .DataAsync(HttpStatusCode.Created)).Id();
        await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/purchase-invoices", new
        {
            supplier_id = supplier, date, lines = new[] { new { account_id = s.Services, item_id = item, quantity, amount } },
        })).DataAsync(HttpStatusCode.Created);
    }

    private static async Task<JsonElement> SaveAsync(SalesInvoiceTests.Setup s, object body) =>
        await (await s.Books.Admin.PostAsync(Url(s), body)).DataAsync(HttpStatusCode.Created);

    private static async Task<(JsonElement Row, bool MatchesControl)> ReportRowAsync(SalesInvoiceTests.Setup s)
    {
        var report = await (await s.Books.Admin.GetAsync($"{s.LedgerUrl}/reports/inventory?asOf=2026-10-31", Ct)).DataAsync();
        return (Assert.Single(report.GetProperty("items").EnumerateArray()), report.GetProperty("matches_control").GetBoolean());
    }

    private static async Task<Dictionary<Guid, (string Dr, string Cr)>> JournalAsync(SalesInvoiceTests.Setup s, JsonElement document)
    {
        var journal = await (await s.Books.Admin.GetAsync($"{s.Books.JournalsUrl}/{document.GetProperty("journal_id").GetGuid()}", Ct)).DataAsync();
        return journal.GetProperty("lines").EnumerateArray().ToDictionary(l => l.GetProperty("account_id").GetGuid(), l => (Get(l, "debit"), Get(l, "credit")));
    }

    private static string Line(JsonElement adjustment, string property) => adjustment.GetProperty("lines")[0].GetProperty(property).GetString()!;

    private static string Get(JsonElement e, string property) => e.GetProperty(property).GetString()!;
}
