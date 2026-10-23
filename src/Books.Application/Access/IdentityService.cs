using Books.Application.Common;
using Books.Domain.Access;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Access;

/// <summary>Claims taken from a validated access token.</summary>
public sealed record ExternalIdentity(string Issuer, string Subject, string? ClientId, string? Email, bool EmailVerified, string? Name);

/// <summary>The verified caller of an API or MCP request.</summary>
public sealed record Principal(Guid UserId, string? ClientId, string? VerifiedEmail);

public sealed class IdentityService(IBooksDb db, TimeProvider clock)
{
    /// <summary>Maps a validated token to a Books user, creating the user on first login.</summary>
    public async Task<Principal> ResolveAsync(ExternalIdentity external, CancellationToken ct)
    {
        var email = external.EmailVerified && !string.IsNullOrWhiteSpace(external.Email)
            ? EmailKey.Normalize(external.Email)
            : null;

        for (var attempt = 0; ; attempt++)
        {
            var found = await db.Identities
                .Where(i => i.Issuer == external.Issuer && i.Subject == external.Subject)
                .Join(db.Users, i => i.UserId, u => u.Id, (i, u) => new { i.UserId, i.VerifiedEmail, u.Status })
                .SingleOrDefaultAsync(ct);

            if (found is not null)
            {
                if (found.Status != UserStatus.Active)
                {
                    throw new BooksProblem(403, "USER_DISABLED", "This user is disabled.");
                }
                if (found.VerifiedEmail != email)
                {
                    await db.Database.ExecuteSqlAsync(
                        $"UPDATE identities SET verified_email = {email} WHERE issuer = {external.Issuer} AND subject = {external.Subject}", ct);
                }
                return new Principal(found.UserId, external.ClientId, email);
            }

            if (attempt > 0)
            {
                throw new InvalidOperationException("Identity could not be provisioned.");
            }
            await ProvisionAsync(external, email, ct);
        }
    }

    private async Task ProvisionAsync(ExternalIdentity external, string? email, CancellationToken ct)
    {
        var userId = Guid.CreateVersion7();
        var displayName = string.IsNullOrWhiteSpace(external.Name) ? (email ?? "User") : external.Name.Trim();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync(
            $"INSERT INTO users (id, status, display_name, created_at, version) VALUES ({userId}, 'active', {displayName}, {clock.GetUtcNow()}, 1)", ct);
        var inserted = await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO identities (id, user_id, issuer, subject, verified_email)
            VALUES ({Guid.CreateVersion7()}, {userId}, {external.Issuer}, {external.Subject}, {email})
            ON CONFLICT (issuer, subject) DO NOTHING
            """, ct);
        if (inserted == 1)
        {
            await tx.CommitAsync(ct);
        }
        // Otherwise a concurrent first request created it: roll back our user row and read theirs.
    }
}

public static class EmailKey
{
    /// <summary>Trims and lower-cases the whole address: emails match ignoring case (owner decision 2026-10-23).</summary>
    public static string Normalize(string email) => email.Trim().ToLowerInvariant();
}
