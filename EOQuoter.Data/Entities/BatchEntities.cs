using EOQuoter.Domain;

namespace EOQuoter.Data.Entities;

public class BatchJobRow
{
    public Guid JobId { get; set; }
    public JobType Type { get; set; }
    public JobStatus Status { get; set; }
    /// <summary>First day of the bordereau month.</summary>
    public DateOnly Period { get; set; }
    public int AttemptCount { get; set; }
    public string? Checkpoint { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public class BordereauEntryRow
{
    public long Id { get; set; }
    public Guid BatchId { get; set; }
    public DateOnly Period { get; set; }
    public string PolicyNumber { get; set; } = null!;
    public string ExternalReferenceId { get; set; } = null!;
    public decimal GrossPremium { get; set; }
    public decimal Commission { get; set; }
    public decimal NetToCarrier { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
}

/// <summary>The simulated carrier statement the bordereau reconciles against. Seeded with deliberate
/// discrepancies so the reconciliation has something real to catch.</summary>
public class CarrierStatementRow
{
    public long Id { get; set; }
    public DateOnly Period { get; set; }
    public string ExternalReferenceId { get; set; } = null!;
    public string? PolicyNumber { get; set; }
    public decimal GrossPremium { get; set; }
}

public class ReconciliationResultRow
{
    public long Id { get; set; }
    public Guid BatchId { get; set; }
    public DateOnly Period { get; set; }
    public string? ExternalReferenceId { get; set; }
    /// <summary>Matched | PremiumMismatch | MissingAtCarrier | MissingAtMga</summary>
    public string Category { get; set; } = null!;
    public decimal? MgaGross { get; set; }
    public decimal? CarrierGross { get; set; }
    public string Detail { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
}

public class RejectedItemRow
{
    public long Id { get; set; }
    public Guid RejectsRef { get; set; }
    public Guid BatchId { get; set; }
    public string ExternalReferenceId { get; set; } = null!;
    public string Reason { get; set; } = null!;
    public string RawPayload { get; set; } = null!;
    public DateTimeOffset RejectedAt { get; set; }
    public DateTimeOffset? ReprocessedAt { get; set; }
}
