using EOQuoter.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EOQuoter.Data;

/// <summary>
/// Deliberately a SEPARATE database with its own connection string. The worker writes execution
/// logs around sproc calls, outside the work transaction, so a rolled-back batch still leaves a
/// Failed row here. Keeping it in the work DB on the same connection would let the evidence of a
/// failure vanish with the failure.
/// </summary>
public class ExecutionTrackingDbContext(DbContextOptions<ExecutionTrackingDbContext> options) : DbContext(options)
{
    public DbSet<SprocExecutionLogRow> SprocExecutions => Set<SprocExecutionLogRow>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<SprocExecutionLogRow>(e =>
        {
            e.HasKey(x => x.RunId);
            e.Property(x => x.SprocName).HasMaxLength(200);
            e.HasIndex(x => x.BatchId);
        });
        SnakeCase.Apply(b);
    }
}
