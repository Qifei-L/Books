using System.Net;
using Books.Domain.Journals;
using Books.Tests.Infrastructure;
using Books.Tests.Journal;

namespace Books.Tests.Ledger;

/// <summary>Rules 18 and 23: one Party record per customer or supplier; journal lines may name one.</summary>
public sealed class PartyTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Parties_are_created_updated_and_listed()
    {
        var books = await JournalBooks.CreateAsync(host);

        var acme = await CreateAsync(books, new { name = "Acme Co", tax_id = "K001-123456789", is_customer = true, contact_name = "Sokha", payment_terms_days = 30 });
        var badTerms = await books.Admin.PostAsync(PartiesUrl(books), new { name = "Other", payment_terms_days = -1 });
        var updated = await (await books.Admin.WriteAsync(HttpMethod.Patch, $"{PartiesUrl(books)}/{acme.Id()}", new { is_supplier = true, phone = "012 345 678" }, ifMatch: 1)).DataAsync();

        await badTerms.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        Assert.Equal(30, updated.GetProperty("payment_terms_days").GetInt32());
        Assert.Equal("Sokha", updated.GetProperty("contact_name").GetString());
        Assert.True(updated.GetProperty("is_customer").GetBoolean());
        Assert.True(updated.GetProperty("is_supplier").GetBoolean());
        Assert.Equal("K001-123456789", updated.GetProperty("tax_id").GetString());
        Assert.Single((await (await books.Admin.GetAsync(PartiesUrl(books), Ct)).DataAsync()).EnumerateArray());
    }

    [Fact]
    public async Task A_journal_line_can_name_a_party_and_then_the_party_cannot_be_deleted()
    {
        var books = await JournalBooks.CreateAsync(host);
        var acme = await CreateAsync(books, new { name = "Acme Co", is_customer = true });

        var journal = await (await books.Admin.PostAsync(books.JournalsUrl, Body(books, acme.Id()))).DataAsync(HttpStatusCode.Created);
        var delete = await books.Admin.WriteAsync(HttpMethod.Delete, $"{PartiesUrl(books)}/{acme.Id()}", null, ifMatch: 1);

        Assert.Equal(acme.Id(), journal.GetProperty("lines")[1].GetProperty("party_id").GetGuid());
        await delete.AssertProblemAsync(HttpStatusCode.Conflict, "PARTY_IN_USE");
    }

    [Fact]
    public async Task Unknown_or_inactive_parties_block_posting()
    {
        var books = await JournalBooks.CreateAsync(host);
        var inactive = await CreateAsync(books, new { name = "Gone Ltd", active = false });

        var unknown = await books.Admin.PostAsync(books.JournalsUrl, Body(books, Guid.NewGuid()));
        var gone = await (await books.Admin.PostAsync(books.JournalsUrl, Body(books, inactive.Id()))).DataAsync(HttpStatusCode.UnprocessableEntity);

        // An unknown party cannot even be stored in an Error revision, so it is rejected as bad input.
        await unknown.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        Assert.Equal("INACTIVE_PARTY", gone.GetProperty("issues")[0].GetProperty("code").GetString());
    }

    [Fact]
    public void Control_accounts_need_a_party_in_documents_and_manual_journals()
    {
        var receivable = Guid.NewGuid();
        var sales = Guid.NewGuid();
        var party = Guid.NewGuid();
        var accounts = new Dictionary<Guid, AccountFacts> { [receivable] = new(receivable, true, true), [sales] = new(sales, true, false) };
        var parties = new Dictionary<Guid, bool> { [party] = true };
        JournalDraft Draft(Guid? partyId) => new(new DateOnly(2026, 10, 1), [new DraftLine(receivable, 10m, PartyId: partyId), new DraftLine(sales, -10m)]);
        string[] Codes(JournalDraft draft, bool fromDocument) =>
            JournalValidator.Validate(draft, new JournalContext(2, null, accounts, null, parties, fromDocument)).Select(i => i.Code).ToArray();

        Assert.Empty(Codes(Draft(party), fromDocument: false));
        Assert.Equal(["PARTY_REQUIRED"], Codes(Draft(null), fromDocument: false));
        Assert.Equal(["PARTY_REQUIRED"], Codes(Draft(null), fromDocument: true));
        Assert.Empty(Codes(Draft(party), fromDocument: true));
    }

    private static string PartiesUrl(JournalBooks books) => $"/api/v1/organizations/{books.OrgId}/ledgers/{books.LedgerId}/parties";

    private static async Task<System.Text.Json.JsonElement> CreateAsync(JournalBooks books, object body) =>
        await (await books.Admin.PostAsync(PartiesUrl(books), body)).DataAsync(HttpStatusCode.Created);

    private static object Body(JournalBooks books, Guid party) => new
    {
        accounting_date = "2026-10-15",
        post = true,
        lines = new object[]
        {
            new { account_id = books.Cash, debit = "10.00" },
            new { account_id = books.Sales, credit = "10.00", party_id = party },
        },
    };
}
