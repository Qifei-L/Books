using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Books.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Books.Tests.Access;

public sealed class OrganizationCreateTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Creator_becomes_the_first_administrator_and_both_changes_are_audited()
    {
        using var client = host.Client(Ids.Subject());

        var data = await (await client.PostAsync("/api/v1/organizations", new { name = "  Acme  " })).DataAsync(HttpStatusCode.Created);

        Assert.Equal("Acme", data.GetProperty("organization").GetProperty("name").GetString());
        Assert.Equal("admin", data.GetProperty("organization").GetProperty("role").GetString());
        Assert.Equal("1", data.GetProperty("membership").GetProperty("generation").ToString());
        var orgId = data.Id("organization");
        await using var db = host.CreateDbContext();
        var actions = await db.AuditEvents.Where(a => a.OrgId == orgId).Select(a => a.Action).ToListAsync(Ct);
        Assert.Equal(["membership.created", "organization.created"], actions.Order());
    }

    [Fact]
    public async Task Retrying_with_the_same_idempotency_key_creates_the_organization_once()
    {
        using var client = host.Client(Ids.Subject());
        var key = Guid.NewGuid();

        var first = await (await client.PostAsync("/api/v1/organizations", new { name = "Once" }, key)).DataAsync(HttpStatusCode.Created);
        var retry = await (await client.PostAsync("/api/v1/organizations", new { name = "Once" }, key)).DataAsync(HttpStatusCode.Created);

        Assert.Equal(first.Id("organization"), retry.Id("organization"));
        var mine = await client.GetFromJsonAsync<JsonElement>("/api/v1/me/organizations", Ct);
        Assert.Equal(1, mine.GetProperty("data").GetArrayLength());
    }

    [Fact]
    public async Task Concurrent_retries_with_the_same_key_take_effect_once()
    {
        using var client = host.Client(Ids.Subject());
        var key = Guid.NewGuid();

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => client.PostAsync("/api/v1/organizations", new { name = "Race" }, key)));

        var ids = new HashSet<Guid>();
        foreach (var response in responses)
        {
            ids.Add((await response.DataAsync(HttpStatusCode.Created)).Id("organization"));
        }
        Assert.Single(ids);
        var mine = await client.GetFromJsonAsync<JsonElement>("/api/v1/me/organizations", Ct);
        Assert.Equal(1, mine.GetProperty("data").GetArrayLength());
    }

    [Fact]
    public async Task Same_key_with_a_different_body_is_rejected()
    {
        using var client = host.Client(Ids.Subject());
        var key = Guid.NewGuid();
        await (await client.PostAsync("/api/v1/organizations", new { name = "First" }, key)).DataAsync(HttpStatusCode.Created);

        var response = await client.PostAsync("/api/v1/organizations", new { name = "Second" }, key);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, "IDEMPOTENCY_KEY_REUSED");
    }

    [Fact]
    public async Task A_write_without_an_idempotency_key_is_rejected()
    {
        using var client = host.Client(Ids.Subject());

        var response = await client.PostAsJsonAsync("/api/v1/organizations", new { name = "No key" }, Ct);

        await response.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
    }

    [Fact]
    public async Task Failed_validation_does_not_consume_the_key()
    {
        using var client = host.Client(Ids.Subject());
        var key = Guid.NewGuid();

        var invalid = await client.PostAsync("/api/v1/organizations", new { name = "   " }, key);
        await invalid.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        var valid = await client.PostAsync("/api/v1/organizations", new { name = "Fixed" }, key);

        await valid.DataAsync(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Unverified_users_cannot_create_organizations()
    {
        using var client = host.Client(Ids.Subject(), emailVerified: false);

        var response = await client.PostAsync("/api/v1/organizations", new { name = "Nope" });

        await response.AssertProblemAsync(HttpStatusCode.Forbidden, "EMAIL_NOT_VERIFIED");
    }

    [Fact]
    public async Task Unknown_fields_are_rejected()
    {
        using var client = host.Client(Ids.Subject());

        var response = await client.PostAsync("/api/v1/organizations", new { name = "X", owner_id = Guid.NewGuid() });

        await response.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
    }
}
