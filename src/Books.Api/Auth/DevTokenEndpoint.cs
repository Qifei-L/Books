namespace Books.Api.Auth;

public static class DevTokenEndpoint
{
    public sealed record DevTokenRequest(string Subject, string? Email, string? Name);

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
        var audience = app.Services.GetRequiredService<AuthOptions>().Audience;
        app.MapPost("/dev/token", (DevTokenRequest request) =>
            Results.Ok(new { access_token = issuer.CreateToken(request.Subject, audience, request.Email, name: request.Name) }));
    }
}
