namespace Books.Domain.Access;

public enum OrganizationState { Active, LockedRecovery }

public enum MembershipRole { Admin, Member }

public enum MembershipStatus { Active, Removed }

/// <summary>A team workspace. Its row is also the lock that serializes every write in the organization.</summary>
public sealed class Organization
{
    private Organization() { }

    public Organization(Guid id, string name, Guid createdBy, DateTimeOffset createdAt)
    {
        Id = id;
        Name = Names.Require(name);
        CreatedBy = createdBy;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public OrganizationState State { get; private set; } = OrganizationState.Active;
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public long Version { get; private set; } = 1;

    public void Rename(string name)
    {
        Name = Names.Require(name);
        Version++;
    }
}

/// <summary>
/// A user's role in one organization. Generation increases each time a removed member rejoins,
/// so grants from an earlier membership never come back.
/// </summary>
public sealed class Membership
{
    private Membership() { }

    public Membership(Guid id, Guid orgId, Guid userId, MembershipRole role, DateTimeOffset joinedAt)
    {
        Id = id;
        OrgId = orgId;
        UserId = userId;
        Role = role;
        JoinedAt = joinedAt;
    }

    public Guid Id { get; private set; }
    public Guid OrgId { get; private set; }
    public Guid UserId { get; private set; }
    public MembershipRole Role { get; private set; }
    public MembershipStatus Status { get; private set; } = MembershipStatus.Active;
    public int Generation { get; private set; } = 1;
    public long Version { get; private set; } = 1;
    public DateTimeOffset JoinedAt { get; private set; }
    public DateTimeOffset? RemovedAt { get; private set; }

    public bool IsActiveAdmin => Status == MembershipStatus.Active && Role == MembershipRole.Admin;
}

public static class Names
{
    public const int MaxLength = 200;

    /// <summary>Trims and checks a display name: 1–200 characters.</summary>
    public static string Require(string? value, string field = "name")
    {
        var trimmed = value?.Trim() ?? "";
        if (trimmed.Length is 0 or > MaxLength)
        {
            throw new DomainException($"{field} must be 1–{MaxLength} characters.");
        }
        return trimmed;
    }
}

/// <summary>A business rule violation in the domain; the API maps it to 400 INVALID_ARGUMENT.</summary>
public sealed class DomainException(string message) : Exception(message);

/// <summary>Access to one ledger. Edit includes read; neither includes granting access (rule 3).</summary>
public enum LedgerAccess { Read = 1, Edit = 2 }
