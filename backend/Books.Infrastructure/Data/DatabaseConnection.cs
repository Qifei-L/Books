using Microsoft.Extensions.Configuration;

namespace Books.Infrastructure.Data;

public static class DatabaseConnection
{
    public static string GetDatabaseConnectionString(IConfiguration configuration)
    {
        var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
        if (!string.IsNullOrWhiteSpace(databaseUrl))
        {
            return IsPostgresUrl(databaseUrl)
                ? ConvertDatabaseUrlToNpgsql(databaseUrl)
                : databaseUrl;
        }

        var envConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (!string.IsNullOrWhiteSpace(envConnectionString))
        {
            return IsPostgresUrl(envConnectionString)
                ? ConvertDatabaseUrlToNpgsql(envConnectionString)
                : envConnectionString;
        }

        return configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Database connection string is not configured.");
    }

    private static bool IsPostgresUrl(string value)
    {
        return value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase);
    }

    private static string ConvertDatabaseUrlToNpgsql(string databaseUrl)
    {
        var uri = new Uri(databaseUrl);
        var userInfo = uri.UserInfo.Split(':', 2);
        var username = Uri.UnescapeDataString(userInfo[0]);
        var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;
        var database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        var port = uri.Port > 0 ? uri.Port : 5432;

        return $"Host={uri.Host};Port={port};Database={database};Username={username};Password={password};SSL Mode=Require;Trust Server Certificate=true";
    }
}
