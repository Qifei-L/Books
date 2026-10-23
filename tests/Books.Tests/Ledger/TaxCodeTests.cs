using System.Net;
using Books.Domain.Access;
using Books.Domain.Ledgers;
using Books.Tests.Infrastructure;
using Books.Tests.Journal;

namespace Books.Tests.Ledger;

/// <summary>Rule 34 and owner decisions: tax codes by kind, per-line rounding at 0.005, overrides kept and flagged.</summary>
public sealed class TaxCodeTests(TestHost host)
{
    [Theory]
    [InlineData("100.05", "10", 2, "10.01")]   // 10.005 rounds up
    [InlineData("-100.05", "10", 2, "-10.01")] // and away from zero when negative
    [InlineData("100.04", "10", 2, "10.00")]   // 10.004 rounds down
    [InlineData("12345", "10", 0, "1235")]     // KHR: 1234.5 → 1235
    [InlineData("33.33", "4", 2, "1.33")]      // fuel 4%
    public void Tax_is_rounded_per_line_half_away_from_zero(string net, string rate, int minor, string expected)
    {
        var tax = TaxCalculator.ForLine(D(net), D(rate), minor);

        Assert.Equal(D(expected), tax.Amount);
        Assert.False(tax.Adjusted);
    }

    [Fact]
    public void Rounding_per_line_then_summing_differs_from_rounding_the_total()
    {
        var lines = new[] { 0.05m, 0.05m, 0.05m }.Select(net => TaxCalculator.ForLine(net, 10m, 2).Amount).Sum();

        Assert.Equal(0.03m, lines); // 3 × round(0.005) = 0.03, whereas round(0.015) would be 0.02
    }

    [Fact]
    public void An_override_keeps_both_amounts_and_an_optional_reason()
    {
        var withReason = TaxCalculator.ForLine(100m, 10m, 2, overrideAmount: 9.98m, reason: " Supplier invoice shows 9.98 ");
        var withoutReason = TaxCalculator.ForLine(100m, 10m, 2, overrideAmount: 25m);

        Assert.Equal((10m, 9.98m, "Supplier invoice shows 9.98", true), (withReason.Calculated, withReason.Amount, withReason.Reason, withReason.Adjusted));
        Assert.True(withoutReason.Adjusted);
        Assert.Null(withoutReason.Reason);
        Assert.Throws<DomainException>(() => TaxCalculator.ForLine(100m, 10m, 2, overrideAmount: 9.985m));
    }

    [Fact]
    public async Task Administrators_create_tax_codes_with_the_right_account_type()
    {
        var books = await JournalBooks.CreateAsync(host);
        var vatPayable = await AccountTests.CreateAsync(books.Admin, books.OrgId, books.LedgerId, "2100", "VAT payable", "liabilities");
        var vatReceivable = await AccountTests.CreateAsync(books.Admin, books.OrgId, books.LedgerId, "1300", "VAT receivable", "assets");

        var output = await (await Create(books, new { name = "VAT 10% sales", kind = "vat_output", rate = "10", account_id = vatPayable.Id() })).DataAsync(HttpStatusCode.Created);
        var input = await (await Create(books, new { name = "VAT 10% purchases", kind = "vat_input", rate = "10", account_id = vatReceivable.Id() })).DataAsync(HttpStatusCode.Created);
        var zero = await (await Create(books, new { name = "VAT 0%", kind = "vat_zero_rated", rate = "0" })).DataAsync(HttpStatusCode.Created);
        var nonVat = await (await Create(books, new { name = "Non-VAT", kind = "non_vat", rate = "0" })).DataAsync(HttpStatusCode.Created);
        var wrongAccount = await Create(books, new { name = "VAT in", kind = "vat_input", rate = "10", account_id = vatPayable.Id() });
        var zeroWithRate = await Create(books, new { name = "VAT 0%", kind = "vat_zero_rated", rate = "10" });

        Assert.Equal(["vat_output", "vat_input", "vat_zero_rated", "non_vat"], new[] { output, input, zero, nonVat }.Select(t => t.GetProperty("kind").GetString()));
        await wrongAccount.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        await zeroWithRate.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
    }

    [Fact]
    public async Task Members_with_edit_access_can_read_but_not_change_tax_codes()
    {
        var books = await JournalBooks.CreateAsync(host);
        using var editor = await books.AddMemberAsync("edit");

        var create = await editor.PostAsync(Url(books), new { name = "VAT 0%", kind = "vat_zero_rated", rate = "0" });
        var list = await editor.GetAsync(Url(books), TestContext.Current.CancellationToken);

        await create.AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
    }

    [Fact]
    public async Task Effective_dates_limit_when_a_code_applies()
    {
        var books = await JournalBooks.CreateAsync(host);
        var vatPayable = await AccountTests.CreateAsync(books.Admin, books.OrgId, books.LedgerId, "2100", "VAT payable", "liabilities");

        var fuel = await (await Create(books, new { name = "VAT 4% fuel", kind = "vat_output", rate = "4", account_id = vatPayable.Id(), effective_from = "2026-03-01" }))
            .DataAsync(HttpStatusCode.Created);
        var backwards = await Create(books, new { name = "Bad", kind = "non_vat", rate = "0", effective_from = "2026-03-01", effective_to = "2026-02-01" });

        Assert.Equal("4", fuel.GetProperty("rate").GetString());
        Assert.Equal("2026-03-01", fuel.GetProperty("effective_from").GetString());
        await backwards.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
    }

    private static decimal D(string s) => decimal.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

    private static string Url(JournalBooks books) => $"/api/v1/organizations/{books.OrgId}/ledgers/{books.LedgerId}/tax-codes";

    private static Task<HttpResponseMessage> Create(JournalBooks books, object body) => books.Admin.PostAsync(Url(books), body);
}
