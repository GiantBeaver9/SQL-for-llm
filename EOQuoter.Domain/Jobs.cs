namespace EOQuoter.Domain;

public enum JobType { BordereauGeneration, Reconciliation }
public enum JobStatus { Queued, Running, Completed, Failed, Retrying }

/// <summary>Durable job row — survives restart, and is itself an audit record (who ran what, when,
/// outcome). Resume = continue from Checkpoint, not redo the batch.</summary>
public record BatchJob(
    Guid JobId,
    JobType Type,
    JobStatus Status,
    DateOnly Period,
    int AttemptCount,
    string? Checkpoint,
    string? LastError,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);

public enum ExecutionStatus { Succeeded, Failed, PartialWithRejects }

/// <summary>
/// Execution tracking, written by the WORKER around each sproc call — separate connection,
/// separate DB — so it sits outside the work transaction and survives a rollback. Never log from
/// inside the sproc: SQL Server has no autonomous transaction, so that log would roll back too.
/// Distinct from the outbox, which is same-DB/same-tx by design; this is deliberately the opposite.
/// </summary>
public record SprocExecutionLog(
    Guid RunId,
    Guid BatchId,
    string SprocName,
    string Params,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    ExecutionStatus Status,
    long RowsAffected,
    string? Error,
    Guid? RejectsRef);

/// <summary>A captured per-item failure. Resume reprocesses these — never the whole period.</summary>
public record RejectedItem(
    Guid RejectsRef,
    Guid BatchId,
    string ExternalReferenceId,
    string Reason,
    string RawPayload,
    DateTimeOffset RejectedAt);
