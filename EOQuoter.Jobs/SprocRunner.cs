using System.Data;
using System.Text.Json;
using EOQuoter.Data;
using EOQuoter.Data.Entities;
using EOQuoter.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EOQuoter.Jobs;

public record SprocOutcome(long RowsAffected, int Rejected, ExecutionStatus Status, IReadOnlyDictionary<string, int> Outputs);

/// <summary>
/// Executes a bordereau sproc with execution tracking written AROUND the call — start row before,
/// end state after — on the SEPARATE tracking database and connection. That placement is the whole
/// point: it sits outside the sproc's transaction, so when the batch rolls back, the Failed row
/// survives as evidence. (SQL Server has no autonomous transactions; a log insert inside the
/// failing tx would vanish with it.)
/// </summary>
public class SprocRunner(QuoterDbContext quoterDb, ExecutionTrackingDbContext trackingDb)
{
    public async Task<SprocOutcome> RunAsync(
        string sprocName,
        Guid batchId,
        DateOnly period,
        Guid rejectsRef,
        (string CountName, string SecondaryName) outputNames,
        CancellationToken ct)
    {
        var log = new SprocExecutionLogRow
        {
            RunId = Guid.NewGuid(),
            BatchId = batchId,
            SprocName = sprocName,
            Params = JsonSerializer.Serialize(new { batchId, period, rejectsRef }),
            StartedAt = DateTimeOffset.UtcNow,
            Status = ExecutionStatus.Failed, // pessimistic until the call returns
        };
        trackingDb.SprocExecutions.Add(log);
        await trackingDb.SaveChangesAsync(ct);

        try
        {
            var pBatch = new SqlParameter("@batch_id", batchId);
            var pPeriod = new SqlParameter("@period", SqlDbType.Date) { Value = period.ToDateTime(TimeOnly.MinValue) };
            var pRejects = new SqlParameter("@rejects_ref", rejectsRef);
            var pCount = new SqlParameter("@count", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var pSecondary = new SqlParameter("@secondary", SqlDbType.Int) { Direction = ParameterDirection.Output };

            await quoterDb.Database.ExecuteSqlRawAsync(
                $"EXEC dbo.{sprocName} @batch_id = @batch_id, @period = @period, @rejects_ref = @rejects_ref, " +
                $"@{outputNames.CountName} = @count OUTPUT, @{outputNames.SecondaryName} = @secondary OUTPUT",
                [pBatch, pPeriod, pRejects, pCount, pSecondary], ct);

            var count = (int)(pCount.Value ?? 0);
            var secondary = (int)(pSecondary.Value ?? 0);

            var rejected = await quoterDb.RejectedItems.AsNoTracking()
                .CountAsync(r => r.RejectsRef == rejectsRef, ct);

            log.Status = rejected > 0 ? ExecutionStatus.PartialWithRejects : ExecutionStatus.Succeeded;
            log.RowsAffected = count;
            log.RejectsRef = rejected > 0 ? rejectsRef : null;
            log.EndedAt = DateTimeOffset.UtcNow;
            await trackingDb.SaveChangesAsync(ct);

            return new SprocOutcome(count, rejected, log.Status,
                new Dictionary<string, int> { [outputNames.CountName] = count, [outputNames.SecondaryName] = secondary });
        }
        catch (Exception ex)
        {
            log.Status = ExecutionStatus.Failed;
            log.Error = ex.Message;
            log.EndedAt = DateTimeOffset.UtcNow;
            await trackingDb.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }
}
