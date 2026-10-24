using System.Net;
using Books.Tests.Infrastructure;
using Books.Tests.Journal;
using Books.Tests.Ledger;
using Npgsql;

namespace Books.Tests.Access;

/// <summary>
/// Writes inside a ledger lock that ledger and share the organization: other ledgers keep working, the same
/// ledger stays serialized, and organization changes wait for writes in flight.
/// </summary>
public sealed class WriteLockTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_busy_ledger_blocks_only_itself()
    {
        var books = await JournalBooks.CreateAsync(host);
        var other = await SecondLedgerAsync(books);
        await using var tx = await HoldAsync($"SELECT 1 FROM ledgers WHERE id = '{books.LedgerId}' FOR UPDATE");

        var otherLedger = await AccountTests.CreateAsync(books.Admin, books.OrgId, other, "1100", "Bank", "assets");
        var sameLedger = await books.Admin.PostAsync($"/api/v1/organizations/{books.OrgId}/ledgers/{books.LedgerId}/accounts",
            new { code = "1100", name = "Bank", type = "assets" });

        Assert.NotEqual(Guid.Empty, otherLedger.Id());
        await sameLedger.AssertProblemAsync(HttpStatusCode.ServiceUnavailable, "TEMPORARILY_UNAVAILABLE");
    }

    [Fact]
    public async Task Organization_changes_wait_for_ledger_writes_in_flight()
    {
        var books = await JournalBooks.CreateAsync(host);
        // What a ledger write holds on the organization while it runs.
        await using var tx = await HoldAsync($"SELECT 1 FROM organizations WHERE id = '{books.OrgId}' FOR SHARE");

        var ledgerWrite = await AccountTests.CreateAsync(books.Admin, books.OrgId, books.LedgerId, "1100", "Bank", "assets");
        var rename = await books.Admin.WriteAsync(HttpMethod.Patch, $"/api/v1/organizations/{books.OrgId}", new { name = "Renamed" }, ifMatch: 1);

        Assert.NotEqual(Guid.Empty, ledgerWrite.Id());
        await rename.AssertProblemAsync(HttpStatusCode.ServiceUnavailable, "TEMPORARILY_UNAVAILABLE");
    }

    [Fact]
    public async Task Concurrent_postings_number_each_ledger_without_gaps_or_repeats()
    {
        var books = await JournalBooks.CreateAsync(host);
        var otherLedger = await SecondLedgerAsync(books);
        var cash = (await AccountTests.CreateAsync(books.Admin, books.OrgId, otherLedger, "1000", "Cash", "assets")).Id();
        var sales = (await AccountTests.CreateAsync(books.Admin, books.OrgId, otherLedger, "4000", "Sales", "income")).Id();
        var other = books with { LedgerId = otherLedger, Cash = cash, Sales = sales };

        var posts = Enumerable.Range(0, 8).SelectMany(_ => new[] { books, other })
            .Select(async b => (b.LedgerId, No: (await (await b.SaveAsync(true, ("1.00", null), (null, "1.00"))).DataAsync(HttpStatusCode.Created)).GetProperty("journal_no").GetInt64()));
        var numbers = await Task.WhenAll(posts);

        foreach (var ledger in numbers.GroupBy(n => n.LedgerId))
        {
            Assert.Equal(Enumerable.Range(1, 8).Select(i => (long)i), ledger.Select(n => n.No).Order());
        }
    }

    private async Task<Guid> SecondLedgerAsync(JournalBooks books) =>
        (await (await books.Admin.PostAsync($"/api/v1/organizations/{books.OrgId}/companies", new { name = "Second Co", base_currency = "USD" }))
            .DataAsync(HttpStatusCode.Created)).Id("ledger");

    /// <summary>Opens a transaction holding the given lock until disposed.</summary>
    private async Task<Held> HoldAsync(string sql)
    {
        var connection = new NpgsqlConnection(host.ConnectionString);
        await connection.OpenAsync(Ct);
        var tx = await connection.BeginTransactionAsync(Ct);
        await using (var command = new NpgsqlCommand(sql, connection, tx))
        {
            await command.ExecuteNonQueryAsync(Ct);
        }
        return new Held(connection, tx);
    }

    private sealed class Held(NpgsqlConnection connection, NpgsqlTransaction tx) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await tx.RollbackAsync();
            await connection.DisposeAsync();
        }
    }
}
