using EOQuoter.Domain;

namespace EOQuoter.Data.Entities;

public class IdempotencyRow
{
    public string Key { get; set; } = null!;
    public string RequestFingerprint { get; set; } = null!;
    public IdempotencyStatus Status { get; set; }
    public string? ResponseSnapshot { get; set; }
    public int? StatusCode { get; set; }
    public Guid? ResultId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public class OutboxRow
{
    public Guid Id { get; set; }
    /// <summary>Dispatch order: UTC ticks at enqueue time (see NewSeq). A plain bigint so ordering
    /// behaves identically on SQL Server and the SQLite test provider.</summary>
    public long Seq { get; set; }

    public static long NewSeq() => DateTimeOffset.UtcNow.UtcTicks;
    public string Type { get; set; } = null!;
    public string Payload { get; set; } = null!;
    public string DedupeKey { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DispatchedAt { get; set; }
    public int AttemptCount { get; set; }
}

/// <summary>Consumer-side dedupe: at-least-once delivery, effect applied exactly once.</summary>
public class ProcessedMessageRow
{
    public string DedupeKey { get; set; } = null!;
    public DateTimeOffset ProcessedAt { get; set; }
}

public class DeadLetterRow
{
    public Guid Id { get; set; }
    public Guid OutboxId { get; set; }
    public string Type { get; set; } = null!;
    public string Payload { get; set; } = null!;
    public string DedupeKey { get; set; } = null!;
    public string Error { get; set; } = null!;
    public int AttemptCount { get; set; }
    public DateTimeOffset DeadLetteredAt { get; set; }
}
