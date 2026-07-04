namespace EOQuoter.Domain;

public enum IdempotencyStatus { Processing, Completed, Failed }

/// <summary>
/// HTTP-edge idempotency: claim-first row. Inserted (Processing) BEFORE any work — the unique
/// key constraint makes the insert the atomic claim — then UPDATED through its lifecycle.
/// Same key + different fingerprint ⇒ 409: a key names one specific intent, not a channel.
/// The key maps to the resulting entity id; it never becomes the entity id.
/// </summary>
public record IdempotencyRecord(
    string Key,
    string RequestFingerprint,
    IdempotencyStatus Status,
    string? ResponseSnapshot,
    int? StatusCode,
    Guid? ResultId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

/// <summary>Outbox: the domain change and this row commit in ONE transaction; a dispatcher
/// publishes, then marks sent. Closes the dual-write gap.</summary>
public record OutboxMessage(
    Guid Id,
    string Type,
    string Payload,
    string DedupeKey,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DispatchedAt);
