namespace Books.Domain.Access;

public enum UserStatus { Active, Disabled }

/// <summary>A person known to Books. Created on the first valid login; holds no credentials.</summary>
public sealed class User
{
    private User() { }

    public Guid Id { get; private set; }
    public UserStatus Status { get; private set; } = UserStatus.Active;
    public string DisplayName { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public long Version { get; private set; } = 1;
}

/// <summary>An external OIDC identity. Books stores only issuer + subject and the verified email, never credentials.</summary>
public sealed class UserIdentity
{
    private UserIdentity() { }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Issuer { get; private set; } = "";
    public string Subject { get; private set; } = "";

    /// <summary>Email as last asserted verified by the issuer; null when the issuer did not verify one.</summary>
    public string? VerifiedEmail { get; private set; }
}
