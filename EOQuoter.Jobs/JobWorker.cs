using System.Text.Json;
using EOQuoter.Data;
using EOQuoter.Data.Entities;
using EOQuoter.Domain;
using Microsoft.EntityFrameworkCore;

namespace EOQuoter.Jobs;

/// <summary>
/// The durable batch worker. Jobs are SQL rows — they survive restart and are themselves audit
/// records. Deliberately a scheduler/worker, not a message broker: bordereau is periodic batch,
/// and a broker here would be infrastructure for its own sake.
///
/// Retry lives HERE (not in rating — rating is pure). A failed job retries with exponential
/// backoff; a partially-complete run resumes from Checkpoint rather than redoing the batch.
/// </summary>
public class JobWorker(IServiceScopeFactory scopeFactory, ILogger<JobWorker> logger) : BackgroundService
{
    public const int MaxAttempts = 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<QuoterDbContext>();
                var runner = scope.ServiceProvider.GetRequiredService<SprocRunner>();
                await ProcessOneAsync(db, runner, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Job scan cycle failed");
            }
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }

    internal async Task ProcessOneAsync(QuoterDbContext db, SprocRunner runner, CancellationToken ct)
    {
        // Atomic claim: only rows still Queued/Retrying flip to Running, so two workers can't
        // both take the same job.
        var candidate = await db.BatchJobs.AsNoTracking()
            .Where(j => j.Status == JobStatus.Queued || j.Status == JobStatus.Retrying)
            .OrderBy(j => j.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (candidate is null) return;

        var claimed = await db.BatchJobs
            .Where(j => j.JobId == candidate.JobId && (j.Status == JobStatus.Queued || j.Status == JobStatus.Retrying))
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, JobStatus.Running)
                .SetProperty(j => j.StartedAt, DateTimeOffset.UtcNow)
                .SetProperty(j => j.AttemptCount, j => j.AttemptCount + 1), ct);
        if (claimed == 0) return; // lost the race

        var job = await db.BatchJobs.SingleAsync(j => j.JobId == candidate.JobId, ct);
        if (job.AttemptCount > 1)
        {
            // Exponential backoff between attempts.
            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, job.AttemptCount - 1)), ct);
        }

        logger.LogInformation("Running job {JobId} ({Type}) for period {Period}, attempt {Attempt}, checkpoint {Checkpoint}",
            job.JobId, job.Type, job.Period, job.AttemptCount, job.Checkpoint ?? "<none>");

        try
        {
            switch (job.Type)
            {
                case JobType.BordereauGeneration:
                    await RunBordereauGenerationAsync(db, runner, job, ct);
                    break;
                case JobType.Reconciliation:
                    await RunReconciliationAsync(db, runner, job, ct);
                    break;
            }

            job.Status = JobStatus.Completed;
            job.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            db.ChangeTracker.Clear();
            var failed = await db.BatchJobs.SingleAsync(j => j.JobId == job.JobId, ct);
            failed.LastError = ex.Message;
            failed.Status = failed.AttemptCount >= MaxAttempts ? JobStatus.Failed : JobStatus.Retrying;
            await db.SaveChangesAsync(ct);
            logger.LogError(ex, "Job {JobId} attempt {Attempt} failed → {Status}", failed.JobId, failed.AttemptCount, failed.Status);
        }
    }

    // Checkpointed stages: [sproc] → [tie-out + audit]. A crash between them resumes at the
    // tie-out — the sproc is not re-run (and is idempotent per period even if it were).
    private static async Task RunBordereauGenerationAsync(QuoterDbContext db, SprocRunner runner, BatchJobRow job, CancellationToken ct)
    {
        if (job.Checkpoint is null)
        {
            var outcome = await runner.RunAsync("usp_GenerateBordereau", job.JobId, job.Period, job.JobId,
                ("inserted", "rejected"), ct);
            job.Checkpoint = $"SprocCompleted:inserted={outcome.Outputs["inserted"]};rejected={outcome.Rejected}";
            await db.SaveChangesAsync(ct);
        }

        var entries = await db.BordereauEntries.AsNoTracking().Where(b => b.Period == job.Period).ToListAsync(ct);
        var gross = entries.Sum(e => e.GrossPremium);
        var commission = entries.Sum(e => e.Commission);
        var net = entries.Sum(e => e.NetToCarrier);
        // Double-entry tie-out: the remittance must equal gross minus commission to the cent.
        if (gross - commission != net)
            throw new InvalidOperationException($"Bordereau tie-out failure for {job.Period:yyyy-MM}: gross {gross} − commission {commission} ≠ net {net}");

        db.AuditEvents.Add(new AuditEventRow
        {
            OccurredAt = DateTimeOffset.UtcNow,
            EntityType = "Bordereau",
            EntityId = job.JobId,
            EventType = "BordereauGenerated",
            DetailJson = JsonSerializer.Serialize(new { Period = job.Period, Entries = entries.Count, gross, commission, net, job.Checkpoint }),
        });
        await db.SaveChangesAsync(ct);
    }

    private static async Task RunReconciliationAsync(QuoterDbContext db, SprocRunner runner, BatchJobRow job, CancellationToken ct)
    {
        int matched = 0, discrepancies = 0;
        if (job.Checkpoint is null)
        {
            var outcome = await runner.RunAsync("usp_ReconcileBordereau", job.JobId, job.Period, job.JobId,
                ("matched", "discrepancies"), ct);
            matched = outcome.Outputs["matched"];
            discrepancies = outcome.Outputs["discrepancies"];
            job.Checkpoint = $"SprocCompleted:matched={matched};discrepancies={discrepancies}";
            await db.SaveChangesAsync(ct);
        }

        db.AuditEvents.Add(new AuditEventRow
        {
            OccurredAt = DateTimeOffset.UtcNow,
            EntityType = "Reconciliation",
            EntityId = job.JobId,
            EventType = "ReconciliationCompleted",
            DetailJson = JsonSerializer.Serialize(new { Period = job.Period, matched, discrepancies }),
        });
        await db.SaveChangesAsync(ct);
    }
}
