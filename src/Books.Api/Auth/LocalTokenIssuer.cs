using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Books.Api.Auth;

/// <summary>
/// Signs tokens for Development and Testing only. The key is generated in memory at startup,
/// so no secret exists in source or configuration. Never enabled in other environments.
/// </summary>
public sealed class LocalTokenIssuer : IDisposable
{
    public const string Issuer = "https://issuer.books.test";

    private readonly RSA _rsa = RSA.Create(2048);

    public LocalTokenIssuer() => SigningKey = new RsaSecurityKey(_rsa) { KeyId = Guid.NewGuid().ToString("N") };

    public SecurityKey SigningKey { get; }

    public string CreateToken(
        string subject,
        string audience,
        string? email = null,
        bool emailVerified = true,
        string? name = null,
        string clientId = "books-local-client",
        TimeSpan? lifetime = null,
        string issuer = Issuer,
        SecurityKey? signingKey = null)
    {
        var claims = new List<Claim> { new("sub", subject), new("azp", clientId) };
        if (email is not null)
        {
            claims.Add(new Claim("email", email));
            claims.Add(new Claim("email_verified", emailVerified ? "true" : "false", ClaimValueTypes.Boolean));
        }
        if (name is not null)
        {
            claims.Add(new Claim("name", name));
        }

        var now = DateTime.UtcNow;
        var expires = now + (lifetime ?? TimeSpan.FromMinutes(5));
        var token = new JwtSecurityToken(
            issuer,
            audience,
            claims,
            notBefore: lifetime < TimeSpan.Zero ? expires.AddMinutes(-5) : now,
            expires: expires,
            signingCredentials: new SigningCredentials(signingKey ?? SigningKey, SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public void Dispose() => _rsa.Dispose();
}
