namespace Books.Api.Auth;

public static class DevTokenEndpoint
{
    /// <param name="Mcp">True for a token for the MCP endpoint instead of the HTTP API.</param>
    public sealed record DevTokenRequest(string Subject, string? Email, string? Name, bool? Mcp);

    /// <summary>
    /// Development only: issues a short-lived token from the in-memory local issuer so the API
    /// can be tried without an identity provider. Not mapped in any other environment.
    /// </summary>
    public static void MapDevTokenEndpoint(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment() || app.Services.GetService<LocalTokenIssuer>() is not { } issuer)
        {
            return;
        }
        var options = app.Services.GetRequiredService<AuthOptions>();
        app.MapPost("/dev/token", (DevTokenRequest request) =>
        {
            var audience = request.Mcp == true ? options.McpAudience : options.Audience;
            return Results.Ok(new { access_token = issuer.CreateToken(request.Subject, audience, request.Email, name: request.Name) });
        });
    }
}
