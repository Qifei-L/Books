using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Books.Tests.Infrastructure;

namespace Books.Tests.Documents;

/// <summary>
/// Return definitions as versioned data (owner decision): drafted by editors or AI, previewed on real months,
/// published by administrators from a month on; filed months keep their version.
/// </summary>
public sealed class ReturnDefinitionTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_draft_previews_on_real_months_and_applies_from_its_month_once_published()
    {
        var s = await new TaxReturnTests(host).SeedAsync();
        using var editor = await s.Books.AddMemberAsync("edit");
        var spec = await BuiltinAsync(s);
        spec["name"] = "Cambodia monthly return, 2026-11 form";
        ((JsonArray)spec["boxes"]!).Add(JsonNode.Parse("""{"key": "export_sales", "section": "VAT", "label": "Exports", "lines": {"side": "sale", "tax_kinds": ["vat_zero_rated"]}}"""));

        var draft = await (await editor.PostAsync(Url(s), new { effective_from = "2026-11", definition = spec })).DataAsync(HttpStatusCode.Created);
        var preview = await TaxReturnTests.GetAsync(s, "tax-returns/2026-10?definitionVersion=1");
        var octoberBefore = await TaxReturnTests.GetAsync(s, "tax-returns/2026-10");
        var byEditor = await editor.WriteAsync(HttpMethod.Post, $"{Url(s)}/1/publish", null, ifMatch: 1);
        var published = await (await s.Books.Admin.WriteAsync(HttpMethod.Post, $"{Url(s)}/1/publish", null, ifMatch: 1)).DataAsync();
        var november = await TaxReturnTests.GetAsync(s, "tax-returns/2026-11");
        var october = await TaxReturnTests.GetAsync(s, "tax-returns/2026-10");
        var change = await s.Books.Admin.WriteAsync(HttpMethod.Put, $"{Url(s)}/1", new { effective_from = "2026-12", definition = spec }, ifMatch: 2);

        Assert.Equal((1, "draft"), (draft.GetProperty("number").GetInt32(), draft.GetProperty("status").GetString()));
        Assert.Equal("250.00", TaxReturnTests.Boxes(preview)["export_sales"].Usd);
        Assert.False(TaxReturnTests.Boxes(octoberBefore).ContainsKey("export_sales"));
        await byEditor.AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
        Assert.Equal("published", published.GetProperty("status").GetString());
        Assert.Equal((1, true), (november.GetProperty("definition_version").GetInt32(), TaxReturnTests.Boxes(november).ContainsKey("export_sales")));
        Assert.Equal(0, october.GetProperty("definition_version").GetInt32());
        await change.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
    }

    [Fact]
    public async Task A_definition_with_errors_is_refused_with_all_of_them()
    {
        var s = await new TaxReturnTests(host).SeedAsync();
        var spec = await BuiltinAsync(s);
        var boxes = (JsonArray)spec["boxes"]!;
        boxes.Add(JsonNode.Parse("""{"key": "output_vat", "section": "VAT", "label": "Again", "lines": {"side": "sale", "amount": "tax"}}"""));
        boxes.Add(JsonNode.Parse("""{"key": "bad", "section": "VAT", "label": "Bad", "formula": {"terms": [{"box": "nowhere"}]}}"""));

        var response = await s.Books.Admin.PostAsync(Url(s), new { effective_from = "2026-11", definition = spec });

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var detail = body.GetProperty("detail").GetString()!;
        Assert.Contains("appears more than once", detail);
        Assert.Contains("nowhere", detail);
    }

    [Fact]
    public async Task A_version_cannot_start_in_a_filed_month_and_new_categories_reach_tax_codes()
    {
        var s = await new TaxReturnTests(host).SeedAsync();
        await TaxReturnTests.DeleteDraftsAsync(s);
        await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/tax-returns/2026-10/file", new { })).DataAsync(HttpStatusCode.Created);
        var spec = await BuiltinAsync(s);
        ((JsonArray)spec["withholding_categories"]!).Add(JsonNode.Parse("""{"key": "dividends", "label": "Dividends (14%)"}"""));
        await (await s.Books.Admin.PostAsync(Url(s), new { effective_from = "2026-10", definition = spec })).DataAsync(HttpStatusCode.Created);
        await (await s.Books.Admin.PostAsync(Url(s), new { effective_from = "2026-11", definition = spec })).DataAsync(HttpStatusCode.Created);

        var inFiledMonth = await s.Books.Admin.WriteAsync(HttpMethod.Post, $"{Url(s)}/1/publish", null, ifMatch: 1);
        var before = await s.Books.Admin.PostAsync($"{s.LedgerUrl}/tax-codes", new { name = "WHT 14% dividends", kind = "withholding", rate = "14", account_id = s.Account("2200"), return_category = "dividends" });
        await (await s.Books.Admin.WriteAsync(HttpMethod.Post, $"{Url(s)}/2/publish", null, ifMatch: 1)).DataAsync();
        var after = await s.Books.Admin.PostAsync($"{s.LedgerUrl}/tax-codes", new { name = "WHT 14% dividends", kind = "withholding", rate = "14", account_id = s.Account("2200"), return_category = "dividends" });

        await inFiledMonth.AssertProblemAsync(HttpStatusCode.Conflict, "MONTH_FILED");
        await before.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        Assert.Equal(HttpStatusCode.Created, after.StatusCode);
    }

    private static string Url(SalesInvoiceTests.Setup s) => $"{s.LedgerUrl}/tax-return-definitions";

    private static async Task<JsonObject> BuiltinAsync(SalesInvoiceTests.Setup s) =>
        JsonNode.Parse((await TaxReturnTests.GetAsync(s, "tax-return-definitions/0")).GetProperty("definition").GetRawText())!.AsObject();
}
