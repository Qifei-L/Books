using System.Net;
using Books.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Books.Tests;

public sealed class HealthTests(TestHost host)
{
    [Fact]
    public async Task Health_reports_healthy_when_database_is_reachable()
    {
        using var client = host.Anonymous();

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Migrations_apply_to_an_empty_database_with_nothing_pending()
    {
        await using var db = host.CreateDbContext();

        var pending = await db.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken);

        Assert.Empty(pending);
    }
}
