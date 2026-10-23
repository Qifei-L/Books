namespace Books.Domain.Access;

public enum InvitationStatus { Pending, Accepted, Revoked, Expired, Invalidated }

/// <summary>
/// An administrator's offer of membership to one verified email (rule 6). It is not a membership:
/// the recipient must sign in with that verified email and explicitly accept within seven days.
/// </summary>
public sealed class Invitation
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    private Invitation() { }

    public Invitation(Guid id, Membership inviter, string targetEmailKey, MembershipRole role, DateTimeOffset createdAt)
    {
        Id = id;
        OrgId = inviter.OrgId;
        InviterMembershipId = inviter.Id;
        TargetEmailKey = targetEmailKey;
        ProposedRole = role;
        CreatedAt = createdAt;
        ExpiresAt = createdAt + Lifetime;
    }

    public Guid Id { get; private set; }
    public Guid OrgId { get; private set; }
    public Guid InviterMembershipId { get; private set; }
    public string TargetEmailKey { get; private set; } = "";
    public MembershipRole ProposedRole { get; private set; }
    public InvitationStatus Status { get; private set; } = InvitationStatus.Pending;
    public DateTimeOffset ExpiresAt { get; private set; }
    public Guid? AcceptedBy { get; private set; }
    public DateTimeOffset? AcceptedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public long Version { get; private set; } = 1;

    /// <summary>Expiry follows the clock, not a cleanup job.</summary>
    public InvitationStatus StatusAt(DateTimeOffset now) =>
        Status == InvitationStatus.Pending && now >= ExpiresAt ? InvitationStatus.Expired : Status;

    public void MarkExpired() => SetStatus(InvitationStatus.Expired);

    public void Revoke() => SetStatus(InvitationStatus.Revoked);

    /// <summary>The inviter stopped being an administrator, or the target was removed.</summary>
    public void Invalidate() => SetStatus(InvitationStatus.Invalidated);

    public void Accept(Guid userId, DateTimeOffset at)
    {
        SetStatus(InvitationStatus.Accepted);
        AcceptedBy = userId;
        AcceptedAt = at;
    }

    private void SetStatus(InvitationStatus status)
    {
        Status = status;
        Version++;
    }
}

public sealed class InvitationGrant
{
    private InvitationGrant() { }

    public InvitationGrant(Invitation invitation, Guid ledgerId, LedgerAccess access)
    {
        OrgId = invitation.OrgId;
        InvitationId = invitation.Id;
        LedgerId = ledgerId;
        Access = access;
    }

    public Guid OrgId { get; private set; }
    public Guid InvitationId { get; private set; }
    public Guid LedgerId { get; private set; }
    public LedgerAccess Access { get; private set; }
}
