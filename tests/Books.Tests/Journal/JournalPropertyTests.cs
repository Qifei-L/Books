using System.Globalization;
using System.Net;
using System.Text.Json;
using Books.Domain.Journals;
using Books.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Books.Tests.Journal;

/// <summary>
/// Random sequences of saves, failed saves, edits, deletes, Close changes and retries through the API, checked
/// against a simple in-memory model after every run. Seeds are fixed so a failure can be replayed.
/// Invariants: rules 22, 27, 28, 29, 37, 38.
/// </summary>
public sealed class JournalPropertyTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Expected(DateOnly Date, decimal Amount, long Version);

    private sealed record Sent(Guid Key, string Method, string Url, object Body, long? Version, HttpStatusCode Status, string Body1);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Random_operations_keep_the_books_consistent(int seed)
    {
        var random = new Random(seed);
        var books = await JournalBooks.CreateAsync(host);
        var model = new Dictionary<Guid, Expected>();
        var history = new List<Sent>();
        DateOnly? closed = null;
        long ledgerVersion = 1;

        for (var step = 0; step < 40; step++)
        {
            var date = new DateOnly(2026, 10, random.Next(1, 29));
            var amount = random.Next(1, 100_000) / 100m;
            switch (random.Next(7))
            {
                case 0 or 1:
                {
                    var (status, data) = await SendAsync(books, history, HttpMethod.Post, books.JournalsUrl, Body(books, date, amount, amount), null);
                    if (closed is { } c && date <= c)
                    {
                        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
                    }
                    else
                    {
                        Assert.Equal(HttpStatusCode.Created, status);
                        model[data.Id()] = new Expected(date, amount, 1);
                    }
                    break;
                }
                case 2:
                {
                    var (status, _) = await SendAsync(books, history, HttpMethod.Post, books.JournalsUrl, Body(books, date, amount, amount + 0.01m), null);
                    Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
                    break;
                }
                case 3 when model.Count > 0:
                {
                    var (id, current) = Pick(random, model);
                    var balanced = random.Next(4) > 0;
                    var (status, _) = await SendAsync(books, history, HttpMethod.Put, $"{books.JournalsUrl}/{id}", Body(books, date, amount, balanced ? amount : amount * 2), current.Version);
                    var open = closed is not { } c || (current.Date > c && date > c);
                    if (open && balanced)
                    {
                        Assert.Equal(HttpStatusCode.OK, status);
                        model[id] = new Expected(date, amount, current.Version + 1);
                    }
                    else
                    {
                        // A failed edit still adds a revision, so the version moves on while the posting stays.
                        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
                        model[id] = current with { Version = current.Version + 1 };
                    }
                    break;
                }
                case 4 when model.Count > 0:
                {
                    var (id, current) = Pick(random, model);
                    var (status, _) = await SendAsync(books, history, HttpMethod.Delete, $"{books.JournalsUrl}/{id}", new { }, current.Version);
                    if (closed is { } c && current.Date <= c)
                    {
                        Assert.Equal(HttpStatusCode.Conflict, status);
                    }
                    else
                    {
                        Assert.Equal(HttpStatusCode.OK, status);
                        model.Remove(id);
                    }
                    break;
                }
                case 5:
                {
                    DateOnly? next = random.Next(3) == 0 ? null : new DateOnly(2026, 10, random.Next(1, 29));
                    var (status, _) = await SendAsync(books, history, HttpMethod.Put, $"/api/v1/organizations/{books.OrgId}/ledgers/{books.LedgerId}/close",
                        new { closed_through = next?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }, ledgerVersion);
                    Assert.Equal(HttpStatusCode.OK, status);
                    closed = next;
                    ledgerVersion++;
                    break;
                }
                case 6 when history.Count > 0:
                {
                    // Rule 29: replaying an earlier request with its key gives the same answer and changes nothing.
                    var earlier = history[random.Next(history.Count)];
                    var replay = await books.Admin.WriteAsync(new HttpMethod(earlier.Method), earlier.Url, earlier.Body, earlier.Key, earlier.Version);
                    Assert.Equal(earlier.Status, replay.StatusCode);
                    Assert.Equal(DataOf(earlier.Body1), DataOf(await replay.Content.ReadAsStringAsync(Ct)));
                    break;
                }
            }
        }

        await AssertConsistentAsync(books, model);
    }

    private async Task AssertConsistentAsync(JournalBooks books, Dictionary<Guid, Expected> model)
    {
        // Rules 37, 38: the Trial Balance reflects exactly the model's effective journals, and balances.
        var tb = await (await books.Admin.GetAsync($"/api/v1/organizations/{books.OrgId}/ledgers/{books.LedgerId}/reports/trial-balance?from=2026-01-01&to=2026-12-31", Ct)).DataAsync();
        Assert.True(tb.GetProperty("balanced").GetBoolean());
        var cash = tb.GetProperty("rows").EnumerateArray().Where(r => r.GetProperty("name").GetString() == "Cash")
            .Select(r => decimal.Parse(r.GetProperty("closing_debit").GetString()!, CultureInfo.InvariantCulture)).SingleOrDefault();
        Assert.Equal(model.Values.Sum(e => e.Amount), cash);

        // Rules 22, 28: one effective posted revision per live journal, balanced, with the model's date and amount.
        await using var db = host.CreateDbContext();
        var journals = await db.Journals.Where(j => j.LedgerId == books.LedgerId && j.EffectiveRevisionId != null).ToListAsync(Ct);
        Assert.Equal(model.Keys.Order(), journals.Select(j => j.Id).Order());
        foreach (var journal in journals)
        {
            var revision = await db.JournalRevisions.SingleAsync(r => r.Id == journal.EffectiveRevisionId, Ct);
            var lines = await db.JournalLines.Where(l => l.RevisionId == revision.Id).ToListAsync(Ct);
            Assert.Equal(RevisionStatus.Posted, revision.Status);
            Assert.Equal(0m, lines.Sum(l => l.Amount));
            Assert.Equal(model[journal.Id].Date, revision.AccountingDate);
            Assert.Equal(model[journal.Id].Amount, lines.Where(l => l.AccountId == books.Cash).Sum(l => l.Amount));
            Assert.Equal(1, await db.JournalRevisions.CountAsync(r => r.JournalId == journal.Id && r.Status == RevisionStatus.Posted, Ct));
        }
    }

    private static async Task<(HttpStatusCode Status, JsonElement Data)> SendAsync(JournalBooks books, List<Sent> history, HttpMethod method, string url, object body, long? version)
    {
        var key = Guid.NewGuid();
        var response = await books.Admin.WriteAsync(method, url, body, key, version);
        var text = await response.Content.ReadAsStringAsync(Ct);
        history.Add(new Sent(key, method.Method, url, body, version, response.StatusCode, text));
        var root = JsonDocument.Parse(text).RootElement;
        return (response.StatusCode, root.TryGetProperty("data", out var data) ? data.Clone() : root.Clone());
    }

    private static string DataOf(string json) =>
        JsonDocument.Parse(json).RootElement.TryGetProperty("data", out var data) ? data.GetRawText() : JsonDocument.Parse(json).RootElement.GetProperty("code").GetString()!;

    private static (Guid, Expected) Pick(Random random, Dictionary<Guid, Expected> model)
    {
        var pair = model.ElementAt(random.Next(model.Count));
        return (pair.Key, pair.Value);
    }

    private static object Body(JournalBooks books, DateOnly date, decimal debit, decimal credit) => new
    {
        accounting_date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        post = true,
        lines = new object[]
        {
            new { account_id = books.Cash, debit = debit.ToString("F2", CultureInfo.InvariantCulture) },
            new { account_id = books.Sales, credit = credit.ToString("F2", CultureInfo.InvariantCulture) },
        },
    };
}
