using System.Net;
using System.Text.Json;
using Books.Tests.Infrastructure;
using Books.Tests.Journal;

namespace Books.Tests.Documents;

/// <summary>Bank reconciliation (owner decision, Manager.io style): cleared postings against the statement balance; cleared postings are soft-locked (P9).</summary>
public sealed class BankReconciliationTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_statement_balance_matches_the_cleared_balance_and_pending_postings_are_listed()
    {
        var (s, bank, deposit, payment) = await SeedAsync();
        await ClearAsync(s, bank, "2026-10-16", deposit);

        var r = await (await s.Books.Admin.GetAsync($"{Url(s, bank)}/reconciliation?asOf=2026-10-31&statementBalance=500.00", Ct)).DataAsync();
        var early = await (await s.Books.Admin.GetAsync($"{Url(s, bank)}/reconciliation?asOf=2026-10-15", Ct)).DataAsync();

        Assert.Equal(("470.00", "500.00", "0.00", true), (Get(r, "book_balance"), Get(r, "cleared_balance"), Get(r, "difference"), r.GetProperty("reconciled").GetBoolean()));
        var pending = Assert.Single(r.GetProperty("pending").EnumerateArray());
        Assert.Equal((payment, "payment", "-30.00"), (pending.GetProperty("journal_id").GetGuid(), Get(pending, "source"), Get(pending, "amount")));
        Assert.Equal("0.00", Get(early, "cleared_balance"));                                  // cleared on the 16th
    }

    [Fact]
    public async Task Cleared_postings_are_locked_until_uncleared()
    {
        var (s, bank, deposit, payment) = await SeedAsync();
        var paymentDoc = (await (await s.Books.Admin.GetAsync($"{s.LedgerUrl}/payments", Ct)).DataAsync())[0];
        await ClearAsync(s, bank, "2026-10-21", deposit, payment);

        var editJournal = await s.Books.Admin.WriteAsync(HttpMethod.Put, $"{s.Books.JournalsUrl}/{deposit}", JournalBooks.Body(s.Books, true, ("600.00", null), (null, "600.00")), ifMatch: 1);
        var deletePayment = await s.Books.Admin.WriteAsync(HttpMethod.Delete, $"{s.LedgerUrl}/payments/{paymentDoc.Id()}", null, ifMatch: 1);
        await (await s.Books.Admin.PostAsync($"{Url(s, bank)}/clearings/remove", new { journal_ids = new[] { payment } })).DataAsync();
        var deleted = await s.Books.Admin.WriteAsync(HttpMethod.Delete, $"{s.LedgerUrl}/payments/{paymentDoc.Id()}", null, ifMatch: 1);

        await editJournal.AssertProblemAsync(HttpStatusCode.Conflict, "BANK_CLEARED");
        await deletePayment.AssertProblemAsync(HttpStatusCode.Conflict, "BANK_CLEARED");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
    }

    [Fact]
    public async Task A_posting_cannot_clear_before_its_date_or_on_another_account()
    {
        var (s, bank, deposit, _) = await SeedAsync();

        var early = await s.Books.Admin.PostAsync($"{Url(s, bank)}/clearings", new { cleared_on = "2026-10-01", journal_ids = new[] { deposit } });
        var wrongAccount = await s.Books.Admin.PostAsync($"{Url(s, s.Books.Sales)}/clearings", new { cleared_on = "2026-10-31", journal_ids = new[] { deposit } });

        await early.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        await wrongAccount.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
    }

    /// <summary>A 500 deposit by journal on 15 Oct and a 30 payment on 20 Oct, on the books' Cash account.</summary>
    private async Task<(PurchaseInvoiceTests.Setup, Guid Bank, Guid Deposit, Guid Payment)> SeedAsync()
    {
        var s = await new PurchaseInvoiceTests(host).SetUpAsync();
        var bank = await PaymentTests.BankAsync(s.Books);
        var deposit = (await (await s.Books.SaveAsync(true, ("500.00", null), (null, "500.00"))).DataAsync(HttpStatusCode.Created)).Id();
        var payment = await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/payments", new
        {
            bank_account_id = bank, date = "2026-10-20", lines = new[] { new { account_id = s.Supplies, amount = "30.00" } },
        })).DataAsync(HttpStatusCode.Created);
        return (s, bank, deposit, payment.GetProperty("journal_id").GetGuid());
    }

    private static async Task ClearAsync(PurchaseInvoiceTests.Setup s, Guid bank, string on, params Guid[] journals) =>
        await (await s.Books.Admin.PostAsync($"{Url(s, bank)}/clearings", new { cleared_on = on, journal_ids = journals })).DataAsync();

    private static string Url(PurchaseInvoiceTests.Setup s, Guid account) => $"{s.LedgerUrl}/bank-accounts/{account}";

    private static string Get(JsonElement e, string property) => e.GetProperty(property).GetString()!;
}
