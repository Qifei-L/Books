using System.Net;
using System.Text.Json;
using Books.Tests.Access;
using Books.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Books.Tests.Ledger;

/// <summary>Rule 1: each company has exactly one ledger, in the same organization.</summary>
public sealed class CompanyLedgerTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Creating_a_company_creates_its_only_ledger_with_defaults()
    {
        using var admin = host.Client(Ids.Subject());
        var orgId = await admin.CreateOrganizationAsync();

        var data = await (await admin.PostAsync($"/api/v1/organizations/{orgId}/companies", new { name = "Acme Trading" })).DataAsync(HttpStatusCode.Created);

        var ledger = data.GetProperty("ledger");
        Assert.Equal(data.GetProperty("company").GetProperty("ledger_id").GetGuid(), ledger.Id());
        Assert.Equal("USD", ledger.GetProperty("base_currency").GetString());
        Assert.Equal(1, ledger.GetProperty("fy_start_month").GetInt32());
        Assert.Equal(JsonValueKind.Null, ledger.GetProperty("closed_through").ValueKind);
        Assert.Equal("edit", ledger.GetProperty("effective_access").GetString());
        var ledgers = await (await admin.GetAsync($"/api/v1/organizations/{orgId}/ledgers", Ct)).DataAsync();
        Assert.Equal(1, ledgers.GetArrayLength());
    }

    [Theory]
    [InlineData("EUR", 1)]
    [InlineData("USD", 13)]
    public async Task Unsupported_currency_or_fiscal_month_is_rejected(string currency, int month)
    {
        using var admin = host.Client(Ids.Subject());
        var orgId = await admin.CreateOrganizationAsync();

        var response = await admin.PostAsync($"/api/v1/organizations/{orgId}/companies", new { name = "X", base_currency = currency, fy_start_month = month });

        await response.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
    }

    [Fact]
    public async Task A_company_cannot_be_committed_without_its_ledger()
    {
        var (orgId, _) = await SeedOrganizationAsync();
        await using var db = host.CreateDbContext();
        await using var tx = await db.Database.BeginTransactionAsync(Ct);
        await db.Database.ExecuteSqlAsync(
            $"INSERT INTO companies (id, org_id, name, created_at, version) VALUES ({Guid.NewGuid()}, {orgId}, 'Lonely', now(), 1)", Ct);

        var error = await Assert.ThrowsAsync<PostgresException>(() => tx.CommitAsync(Ct));

        Assert.Equal("23503", error.SqlState);
    }

    [Fact]
    public async Task A_ledger_cannot_be_committed_without_its_company()
    {
        var (orgId, _) = await SeedOrganizationAsync();
        await using var db = host.CreateDbContext();
        await using var tx = await db.Database.BeginTransactionAsync(Ct);
        await InsertLedgerAsync(db, orgId, Guid.NewGuid());

        var error = await Assert.ThrowsAsync<PostgresException>(() => tx.CommitAsync(Ct));

        Assert.Equal("23503", error.SqlState);
    }

    [Fact]
    public async Task A_company_cannot_get_a_second_ledger_or_a_ledger_from_another_organization()
    {
        var (orgA, _) = await SeedOrganizationAsync();
        var (orgB, _) = await SeedOrganizationAsync();
        await using var db = host.CreateDbContext();
        var companyId = Guid.NewGuid();
        await using (var tx = await db.Database.BeginTransactionAsync(Ct))
        {
            await db.Database.ExecuteSqlAsync(
                $"INSERT INTO companies (id, org_id, name, created_at, version) VALUES ({companyId}, {orgA}, 'Paired', now(), 1)", Ct);
            await InsertLedgerAsync(db, orgA, companyId);
            await tx.CommitAsync(Ct);
        }

        var second = await Assert.ThrowsAsync<PostgresException>(() => InsertLedgerAsync(db, orgA, companyId));
        var otherCompany = Guid.NewGuid();
        await using var crossTx = await db.Database.BeginTransactionAsync(Ct);
        await db.Database.ExecuteSqlAsync(
            $"INSERT INTO companies (id, org_id, name, created_at, version) VALUES ({otherCompany}, {orgA}, 'Split', now(), 1)", Ct);
        await InsertLedgerAsync(db, orgB, otherCompany);
        var crossOrg = await Assert.ThrowsAsync<PostgresException>(() => crossTx.CommitAsync(Ct));

        Assert.Equal("23505", second.SqlState);
        Assert.Equal("23503", crossOrg.SqlState);
    }

    [Fact]
    public async Task Another_organizations_administrator_cannot_see_or_create_companies()
    {
        using var alice = host.Client(Ids.Subject("alice"));
        using var bob = host.Client(Ids.Subject("bob"));
        var aliceOrg = await alice.CreateOrganizationAsync();
        await bob.CreateOrganizationAsync();
        var created = await (await alice.PostAsync($"/api/v1/organizations/{aliceOrg}/companies", new { name = "Secret" })).DataAsync(HttpStatusCode.Created);
        var companyId = created.Id("company");
        var ledgerId = created.Id("ledger");

        await (await bob.GetAsync($"/api/v1/organizations/{aliceOrg}/companies", Ct)).AssertProblemAsync(HttpStatusCode.NotFound, "NOT_FOUND");
        await (await bob.GetAsync($"/api/v1/organizations/{aliceOrg}/companies/{companyId}", Ct)).AssertProblemAsync(HttpStatusCode.NotFound, "NOT_FOUND");
        await (await bob.GetAsync($"/api/v1/organizations/{aliceOrg}/ledgers/{ledgerId}", Ct)).AssertProblemAsync(HttpStatusCode.NotFound, "NOT_FOUND");
        await (await bob.PostAsync($"/api/v1/organizations/{aliceOrg}/companies", new { name = "Intruder" })).AssertProblemAsync(HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Fact]
    public async Task A_ledger_id_from_another_organization_is_not_found_under_ones_own()
    {
        using var alice = host.Client(Ids.Subject("alice"));
        using var bob = host.Client(Ids.Subject("bob"));
        var aliceOrg = await alice.CreateOrganizationAsync();
        var bobOrg = await bob.CreateOrganizationAsync();
        var aliceLedger = (await (await alice.PostAsync($"/api/v1/organizations/{aliceOrg}/companies", new { name = "A" })).DataAsync(HttpStatusCode.Created)).Id("ledger");

        var response = await bob.GetAsync($"/api/v1/organizations/{bobOrg}/ledgers/{aliceLedger}", Ct);

        await response.AssertProblemAsync(HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Fact]
    public async Task Rename_company_requires_version_and_is_audited()
    {
        using var admin = host.Client(Ids.Subject());
        var orgId = await admin.CreateOrganizationAsync();
        var companyId = (await (await admin.PostAsync($"/api/v1/organizations/{orgId}/companies", new { name = "Old" })).DataAsync(HttpStatusCode.Created)).Id("company");
        var url = $"/api/v1/organizations/{orgId}/companies/{companyId}";

        await (await admin.WriteAsync(HttpMethod.Patch, url, new { name = "New" })).AssertProblemAsync(HttpStatusCode.PreconditionRequired, "PRECONDITION_REQUIRED");
        var renamed = await (await admin.WriteAsync(HttpMethod.Patch, url, new { name = "New" }, ifMatch: 1)).DataAsync();

        Assert.Equal("New", renamed.GetProperty("name").GetString());
        await using var db = host.CreateDbContext();
        Assert.Equal(1, await db.AuditEvents.CountAsync(a => a.TargetId == companyId && a.Action == "company.renamed", Ct));
    }

    private async Task<(Guid OrgId, Guid UserId)> SeedOrganizationAsync()
    {
        await using var db = host.CreateDbContext();
        var userId = await AccessSchemaTests.InsertUserAsync(db);
        var orgId = Guid.NewGuid();
        await db.Database.ExecuteSqlAsync(
            $"INSERT INTO organizations (id, name, state, created_by, created_at, version) VALUES ({orgId}, 'Org', 'active', {userId}, now(), 1)", Ct);
        await AccessSchemaTests.InsertMembershipAsync(db, orgId, userId);
        return (orgId, userId);
    }

    private static Task InsertLedgerAsync(DbContext db, Guid orgId, Guid companyId) =>
        db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO ledgers (id, org_id, company_id, base_currency, fy_start_month, created_at, version)
            VALUES ({Guid.NewGuid()}, {orgId}, {companyId}, 'USD', 1, now(), 1)
            """, Ct);
}
