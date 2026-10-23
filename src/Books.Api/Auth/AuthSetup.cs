using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Books.Api.Auth;

public sealed class AuthOptions
{
    /// <summary>OIDC issuer; its discovery document supplies the signing keys.</summary>
    public string? Issuer { get; set; }

    /// <summary>Audience the Books HTTP API accepts.</summary>
    public string Audience { get; set; } = "books-api";

    /// <summary>Audience the MCP endpoint accepts. Kept separate so a token for one entry point cannot be used at the other.</summary>
    public string McpAudience { get; set; } = "books-mcp";

    /// <summary>Use the in-memory <see cref="LocalTokenIssuer"/>. Allowed only in Development and Testing.</summary>
    public bool UseLocalIssuer { get; set; }
}

public static class AuthSetup
{
    public const string ApiPolicy = "api";
    public const string McpPolicy = "mcp";

    public static void AddBooksAuthentication(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();
        var localAllowed = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing");
        if (options.UseLocalIssuer && !localAllowed)
        {
            throw new InvalidOperationException("Auth:UseLocalIssuer is only allowed in Development and Testing.");
        }
        if (!options.UseLocalIssuer && string.IsNullOrWhiteSpace(options.Issuer))
        {
            throw new InvalidOperationException("Auth:Issuer is not configured.");
        }

        builder.Services.AddSingleton(options);
        if (options.UseLocalIssuer)
        {
            builder.Services.AddSingleton<LocalTokenIssuer>();
        }

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IServiceProvider>((jwt, services) =>
            {
                jwt.MapInboundClaims = false;
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = options.UseLocalIssuer ? LocalTokenIssuer.Issuer : options.Issuer,
                    ValidAudiences = [options.Audience, options.McpAudience],
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256, SecurityAlgorithms.EcdsaSha256],
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "sub",
                };
                if (options.UseLocalIssuer)
                {
                    jwt.TokenValidationParameters.IssuerSigningKey = services.GetRequiredService<LocalTokenIssuer>().SigningKey;
                }
                else
                {
                    jwt.Authority = options.Issuer;
                }
                jwt.Events = new JwtBearerEvents
                {
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        context.Response.Headers.WWWAuthenticate = "Bearer";
                        await Problems.WriteAsync(context.HttpContext, 401, "UNAUTHENTICATED", "Authentication is required.");
                    },
                    OnForbidden = context =>
                        Problems.WriteAsync(context.HttpContext, 403, "INSUFFICIENT_SCOPE", "This token is not issued for this endpoint."),
                };
            });
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(ApiPolicy, policy => policy.RequireAuthenticatedUser().RequireClaim("aud", options.Audience))
            .AddPolicy(McpPolicy, policy => policy.RequireAuthenticatedUser().RequireClaim("aud", options.McpAudience));
    }
}
