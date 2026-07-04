using EOQuoter.Domain;

namespace EOQuoter.Data.Entities;

/// <summary>
/// Lives in the SEPARATE execution-tracking database. The worker writes this row before and after
/// each sproc call on its own connection, outside the work transaction, so it survives a per-item
/// or per-batch rollback. See the decision log: SQL Server has no autonomous transactions, so
/// logging from inside the sproc would roll back with the work.
/// </summary>
public class SprocExecutionLogRow
{
    public Guid RunId { get; set; }
    public Guid BatchId { get; set; }
    public string SprocName { get; set; } = null!;
    public string Params { get; set; } = null!;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public ExecutionStatus Status { get; set; }
    public long RowsAffected { get; set; }
    public string? Error { get; set; }
    public Guid? RejectsRef { get; set; }
}
