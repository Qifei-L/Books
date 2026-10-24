using System.Net.Http.Headers;
using Books.Api.Auth;
using Books.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Books.Tests.Infrastructure.TestHost))]

namespace Books.Tests.Infrastructure;

/// <summary>
/// One migrated PostgreSQL database and one API host per test run. Uses Testcontainers by default.
/// Set BOOKS_TEST_POSTGRES to an admin connection string to use an existing server instead
/// (a fresh database is created on it). Tests isolate themselves by creating their own users and organizations.
/// </summary>
public sealed class TestHost : IAsyncLifetime
{
    public const string Audience = "books-api";
    public const string McpAudience = "books-mcp";

    private PostgreSqlContainer? _container;
    private WebApplicationFactory<Program>? _factory;

    public string ConnectionString { get; private set; } = "";

    public WebApplicationFactory<Program> Factory => _factory ?? throw new InvalidOperationException("Not initialized.");

    public LocalTokenIssuer Issuer => Factory.Services.GetRequiredService<LocalTokenIssuer>();

    public async ValueTask InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable("BOOKS_TEST_POSTGRES");
        if (string.IsNullOrEmpty(external))
        {
            _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
        }
        else
        {
            var database = $"books_test_{Guid.NewGuid():N}";
            await using (var admin = new NpgsqlConnection(external))
            {
                await admin.OpenAsync();
                await using var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin);
                await create.ExecuteNonQueryAsync();
            }
            ConnectionString = new NpgsqlConnectionStringBuilder(external) { Database = database }.ConnectionString;
        }

        await using (var db = CreateDbContext())
        {
            await db.Database.MigrateAsync();
        }

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Books", ConnectionString);
            builder.UseSetting("Auth:UseLocalIssuer", "true");
            builder.UseSetting("Auth:Audience", Audience);
            builder.UseSetting("Auth:McpAudience", McpAudience);
            builder.UseSetting("Logging:LogLevel:Default", "Warning");
        });
        // Start the server now: lazy start from parallel tests is not thread-safe and could build two hosts with different keys.
        _ = _factory.Server;
    }

    public BooksDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<BooksDbContext>().UseNpgsql(ConnectionString).UseSnakeCaseNamingConvention().Options);

    /// <summary>An HTTP client without credentials.</summary>
    public HttpClient Anonymous() => Factory.CreateClient();

    /// <summary>An HTTP client signed in as the given subject with a verified email.</summary>
    public HttpClient Client(string subject, string? email = null, bool emailVerified = true) =>
        WithToken(Issuer.CreateToken(subject, Audience, email ?? $"{subject}@example.test", emailVerified));

    public HttpClient WithToken(string token)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
        else if (Environment.GetEnvironmentVariable("BOOKS_TEST_POSTGRES") is { Length: > 0 } external)
        {
            // A shared server would otherwise collect one database per run and slow down.
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(external);
            await admin.OpenAsync();
            var database = new NpgsqlConnectionStringBuilder(ConnectionString).Database;
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {database} WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}

public static class Ids
{
    /// <summary>A unique subject per test, so tests never share users.</summary>
    public static string Subject(string prefix = "user") => $"{prefix}-{Guid.NewGuid():N}";
}
