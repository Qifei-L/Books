using Books.Application.Access;

namespace Books.Api.Auth;

/// <summary>Resolves the verified caller once per request, from the validated token only.</summary>
public sealed class PrincipalAccessor(IHttpContextAccessor http, IdentityService identities, AuthOptions options)
{
    private Principal? _principal;

    public async Task<Principal> GetAsync(CancellationToken ct)
    {
        if (_principal is not null)
        {
            return _principal;
        }

        var user = http.HttpContext?.User;
        var issuer = user?.FindFirst("iss")?.Value;
        var subject = user?.FindFirst("sub")?.Value;
        if (user?.Identity?.IsAuthenticated != true || string.IsNullOrEmpty(issuer) || string.IsNullOrEmpty(subject))
        {
            throw new InvalidOperationException("Endpoint must require authorization.");
        }

        var external = new ExternalIdentity(
            issuer,
            subject,
            user.FindFirst("azp")?.Value ?? user.FindFirst("client_id")?.Value,
            user.FindFirst("email")?.Value,
            string.Equals(user.FindFirst("email_verified")?.Value, "true", StringComparison.OrdinalIgnoreCase),
            user.FindFirst("name")?.Value);
        var principal = await identities.ResolveAsync(external, ct);
        var isAgent = user.FindAll("aud").Any(a => a.Value == options.McpAudience);
        return _principal = principal with { IsAgent = isAgent };
    }
}
