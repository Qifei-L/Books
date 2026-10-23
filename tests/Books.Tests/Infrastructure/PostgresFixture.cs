using Books.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Books.Tests.Infrastructure.PostgresFixture))]

namespace Books.Tests.Infrastructure;

/// <summary>
/// One migrated PostgreSQL database per test run. Uses Testcontainers by default.
/// Set BOOKS_TEST_POSTGRES to an admin connection string to use an existing server instead
/// (a fresh database is created on it).
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public string ConnectionString { get; private set; } = "";

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

        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public BooksDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<BooksDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new BooksDbContext(options);
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}
