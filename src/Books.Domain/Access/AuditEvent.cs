namespace Books.Domain.Access;

/// <summary>Append-only record of a committed change. The database rejects UPDATE and DELETE.</summary>
public sealed class AuditEvent
{
    private AuditEvent() { }

    public AuditEvent(
        Guid? orgId, Guid actorUserId, string? actorClientId, string action,
        string targetType, Guid targetId, string changes, string requestId, DateTimeOffset occurredAt)
    {
        Id = Guid.CreateVersion7();
        OrgId = orgId;
        ActorUserId = actorUserId;
        ActorClientId = actorClientId;
        Action = action;
        TargetType = targetType;
        TargetId = targetId;
        Changes = changes;
        RequestId = requestId;
        OccurredAt = occurredAt;
    }

    public Guid Id { get; private set; }
    public Guid? OrgId { get; private set; }
    public Guid ActorUserId { get; private set; }
    public string? ActorClientId { get; private set; }
    public string Action { get; private set; } = "";
    public string TargetType { get; private set; } = "";
    public Guid TargetId { get; private set; }

    /// <summary>JSON with safe before/after values. Never tokens or secrets.</summary>
    public string Changes { get; private set; } = "{}";
    public string RequestId { get; private set; } = "";
    public DateTimeOffset OccurredAt { get; private set; }
}

/// <summary>The committed result of a write, replayed when the same Idempotency-Key is retried.</summary>
public sealed class IdempotencyRecord
{
    private IdempotencyRecord() { }

    public IdempotencyRecord(
        Guid actorUserId, string clientId, string scopeKey, string operation, Guid key,
        string requestHash, int responseStatus, string responseBody, DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        ActorUserId = actorUserId;
        ClientId = clientId;
        ScopeKey = scopeKey;
        Operation = operation;
        Key = key;
        RequestHash = requestHash;
        ResponseStatus = responseStatus;
        ResponseBody = responseBody;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public Guid ActorUserId { get; private set; }
    public string ClientId { get; private set; } = "";
    public string ScopeKey { get; private set; } = "";
    public string Operation { get; private set; } = "";
    public Guid Key { get; private set; }
    public string RequestHash { get; private set; } = "";
    public int ResponseStatus { get; private set; }
    public string ResponseBody { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
}
