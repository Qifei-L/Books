using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Books.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Books.Tests.Access;

public sealed class AuthenticationTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Missing_token_is_rejected_with_problem_code()
    {
        using var client = host.Anonymous();

        var response = await client.GetAsync("/api/v1/me", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("UNAUTHENTICATED", body.GetProperty("code").GetString());
    }

    public static TheoryData<string> BadTokens => ["wrong-audience", "wrong-issuer", "unknown-key", "expired"];

    [Theory]
    [MemberData(nameof(BadTokens))]
    public async Task Invalid_tokens_are_rejected(string kind)
    {
        var subject = Ids.Subject();
        using var otherKey = RSA.Create(2048);
        var token = kind switch
        {
            "wrong-audience" => host.Issuer.CreateToken(subject, "some-other-api"),
            "wrong-issuer" => host.Issuer.CreateToken(subject, TestHost.Audience, issuer: "https://other-issuer.books.test"),
            "unknown-key" => host.Issuer.CreateToken(subject, TestHost.Audience, signingKey: new RsaSecurityKey(otherKey)),
            "expired" => host.Issuer.CreateToken(subject, TestHost.Audience, lifetime: TimeSpan.FromMinutes(-2)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        using var client = host.WithToken(token);

        var response = await client.GetAsync("/api/v1/me", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task First_login_creates_one_user_keyed_by_issuer_and_subject()
    {
        var subject = Ids.Subject();
        using var client = host.Client(subject, "Ann@Example.TEST");

        var first = await client.GetFromJsonAsync<JsonElement>("/api/v1/me", Ct);
        var second = await client.GetFromJsonAsync<JsonElement>("/api/v1/me", Ct);

        Assert.Equal(first.GetProperty("data").GetProperty("id").GetString(), second.GetProperty("data").GetProperty("id").GetString());
        Assert.Equal("Ann@example.test", first.GetProperty("data").GetProperty("email").GetString());
        await using var db = host.CreateDbContext();
        Assert.Equal(1, await db.Identities.CountAsync(i => i.Subject == subject, Ct));
    }

    [Fact]
    public async Task Concurrent_first_logins_create_exactly_one_user()
    {
        var subject = Ids.Subject();
        var clients = Enumerable.Range(0, 8).Select(_ => host.Client(subject)).ToList();

        var responses = await Task.WhenAll(clients.Select(c => c.GetAsync("/api/v1/me", Ct)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        await using var db = host.CreateDbContext();
        var userIds = await db.Identities.Where(i => i.Subject == subject).Select(i => i.UserId).ToListAsync(Ct);
        Assert.Single(userIds);
        var orphanUsers = await db.Users.CountAsync(u => !db.Identities.Any(i => i.UserId == u.Id), Ct);
        Assert.Equal(0, orphanUsers);
        clients.ForEach(c => c.Dispose());
    }

    [Fact]
    public async Task Unverified_email_is_not_stored()
    {
        using var client = host.Client(Ids.Subject(), "someone@example.test", emailVerified: false);

        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me", Ct);

        Assert.False(me.GetProperty("data").GetProperty("email_verified").GetBoolean());
        Assert.Equal(JsonValueKind.Null, me.GetProperty("data").GetProperty("email").ValueKind);
    }
}
