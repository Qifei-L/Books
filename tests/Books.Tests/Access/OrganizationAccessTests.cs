using System.Net;
using Books.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Books.Tests.Access;

/// <summary>Rule 4: a role in one organization gives nothing in another, and guessed IDs grant nothing.</summary>
public sealed class OrganizationAccessTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_administrator_of_one_organization_cannot_read_or_change_another()
    {
        using var alice = host.Client(Ids.Subject("alice"));
        using var bob = host.Client(Ids.Subject("bob"));
        var aliceOrg = await alice.CreateOrganizationAsync("Alice Co");
        await bob.CreateOrganizationAsync("Bob Co");

        var read = await bob.GetAsync($"/api/v1/organizations/{aliceOrg}", Ct);
        var rename = await bob.WriteAsync(HttpMethod.Patch, $"/api/v1/organizations/{aliceOrg}", new { name = "Taken" }, ifMatch: 1);

        await read.AssertProblemAsync(HttpStatusCode.NotFound, "NOT_FOUND");
        await rename.AssertProblemAsync(HttpStatusCode.NotFound, "NOT_FOUND");
        var current = await (await alice.GetAsync($"/api/v1/organizations/{aliceOrg}", Ct)).DataAsync();
        Assert.Equal("Alice Co", current.GetProperty("name").GetString());
    }

    [Fact]
    public async Task A_guessed_organization_id_looks_the_same_as_one_that_does_not_exist()
    {
        using var client = host.Client(Ids.Subject());
        await client.CreateOrganizationAsync();

        var response = await client.GetAsync($"/api/v1/organizations/{Guid.NewGuid()}", Ct);

        await response.AssertProblemAsync(HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Fact]
    public async Task Rename_requires_the_current_version()
    {
        using var client = host.Client(Ids.Subject());
        var orgId = await client.CreateOrganizationAsync("Before");
        var url = $"/api/v1/organizations/{orgId}";

        var missing = await client.WriteAsync(HttpMethod.Patch, url, new { name = "After" });
        var stale = await client.WriteAsync(HttpMethod.Patch, url, new { name = "After" }, ifMatch: 7);
        var ok = await client.WriteAsync(HttpMethod.Patch, url, new { name = "After" }, ifMatch: 1);

        await missing.AssertProblemAsync(HttpStatusCode.PreconditionRequired, "PRECONDITION_REQUIRED");
        await stale.AssertProblemAsync(HttpStatusCode.PreconditionFailed, "VERSION_CONFLICT");
        var data = await ok.DataAsync();
        Assert.Equal("2", data.GetProperty("version").GetString());
        Assert.Equal("\"v2\"", ok.Headers.ETag?.ToString());
    }

    [Fact]
    public async Task Rename_is_audited_with_before_and_after_and_failed_attempts_leave_no_audit()
    {
        using var client = host.Client(Ids.Subject());
        var orgId = await client.CreateOrganizationAsync("Old");
        var url = $"/api/v1/organizations/{orgId}";

        await client.WriteAsync(HttpMethod.Patch, url, new { name = "Stale" }, ifMatch: 5);
        await (await client.WriteAsync(HttpMethod.Patch, url, new { name = "New" }, ifMatch: 1)).DataAsync();

        await using var db = host.CreateDbContext();
        var renames = await db.AuditEvents.Where(a => a.OrgId == orgId && a.Action == "organization.renamed").ToListAsync(Ct);
        var only = Assert.Single(renames);
        Assert.Contains("\"before\": \"Old\"", only.Changes);
        Assert.Contains("\"after\": \"New\"", only.Changes);
    }
}
