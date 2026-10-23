using System.Net;
using Books.Tests.Infrastructure;
using Books.Tests.Journal;

namespace Books.Tests.Ledger;

/// <summary>Rule 23: items in categories suggest an account and tax code; lines keep the account chosen.</summary>
public sealed class ItemTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Items_have_a_category_defaults_and_can_be_named_on_journal_lines()
    {
        var books = await JournalBooks.CreateAsync(host);
        var category = await (await books.Admin.PostAsync(Url(books, "item-categories"), new { name = "Consulting" })).DataAsync(HttpStatusCode.Created);
        var item = await (await books.Admin.PostAsync(Url(books, "items"), new
        {
            name = "Advisory hour", unit = "hour", category_id = category.Id(), default_account_id = books.Sales,
        })).DataAsync(HttpStatusCode.Created);

        // The person chose Cash for this line, not the item's default; the chosen account is what is saved.
        var journal = await (await books.Admin.PostAsync(books.JournalsUrl, new
        {
            accounting_date = "2026-10-15",
            post = true,
            lines = new object[]
            {
                new { account_id = books.Cash, debit = "150.00" },
                new { account_id = books.Sales, credit = "150.00", item_id = item.Id(), quantity = "1.5" },
            },
        })).DataAsync(HttpStatusCode.Created);

        Assert.Equal(category.Id(), item.GetProperty("category_id").GetGuid());
        Assert.Equal(books.Sales, item.GetProperty("default_account_id").GetGuid());
        var line = journal.GetProperty("lines")[1];
        Assert.Equal((item.Id(), "1.5"), (line.GetProperty("item_id").GetGuid(), line.GetProperty("quantity").GetString()));
        var listed = await (await books.Admin.GetAsync(Url(books, "items"), Ct)).DataAsync();
        Assert.True(Assert.Single(listed.EnumerateArray()).GetProperty("used").GetBoolean());
    }

    [Fact]
    public async Task Items_and_their_defaults_stay_inside_the_ledger()
    {
        var books = await JournalBooks.CreateAsync(host);
        var other = await JournalBooks.CreateAsync(host);

        var foreignAccount = await books.Admin.PostAsync(Url(books, "items"), new { name = "Bad", default_account_id = other.Cash });
        var unknownItem = await books.Admin.PostAsync(books.JournalsUrl, new
        {
            accounting_date = "2026-10-15",
            post = true,
            lines = new object[] { new { account_id = books.Cash, debit = "1.00" }, new { account_id = books.Sales, credit = "1.00", item_id = Guid.NewGuid() } },
        });

        await foreignAccount.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        await unknownItem.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
    }

    private static string Url(JournalBooks books, string resource) => $"/api/v1/organizations/{books.OrgId}/ledgers/{books.LedgerId}/{resource}";
}
