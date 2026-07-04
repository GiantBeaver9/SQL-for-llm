using System.Globalization;
using EOQuoter.Data;
using EOQuoter.Data.Entities;
using EOQuoter.Data.Seed;
using EOQuoter.Domain;
using Microsoft.EntityFrameworkCore;

namespace EOQuoter.Api.Endpoints;

public static class JobEndpoints
{
    public static void Map(WebApplication app)
    {
        // Enqueue only — the durable row is the contract, EOQuoter.Jobs' worker does the work.
        app.MapPost("/jobs/bordereau", (EnqueueJobRequest req, QuoterDbContext db, CancellationToken ct) =>
            EnqueueAsync(db, JobType.BordereauGeneration, req.Period, ct));

        app.MapPost("/jobs/reconciliation", (EnqueueJobRequest req, QuoterDbContext db, CancellationToken ct) =>
            EnqueueAsync(db, JobType.Reconciliation, req.Period, ct));

        app.MapGet("/jobs/{id:guid}", async (Guid id, QuoterDbContext db, CancellationToken ct) =>
        {
            var j = await db.BatchJobs.AsNoTracking().SingleOrDefaultAsync(x => x.JobId == id, ct);
            return j is null
                ? Results.NotFound()
                : Results.Ok(new JobResponse(j.JobId, j.Type.ToString(), j.Status.ToString(), j.Period, j.AttemptCount, j.Checkpoint, j.LastError));
        });

        app.MapGet("/bordereau/{period}", async (string period, QuoterDbContext db, CancellationToken ct) =>
        {
            if (!TryParsePeriod(period, out var p)) return Results.BadRequest(new { error = "Period must be yyyy-MM" });
            var entries = await db.BordereauEntries.AsNoTracking().Where(b => b.Period == p).ToListAsync(ct);
            return Results.Ok(new
            {
                period = p,
                entries = entries.Select(e => new BordereauEntry(e.PolicyNumber, e.ExternalReferenceId, e.GrossPremium, e.Commission, e.NetToCarrier)),
                totals = new
                {
                    gross = entries.Sum(e => e.GrossPremium),
                    commission = entries.Sum(e => e.Commission),
                    // The period's remittance ties out to the sum of entries — double-entry discipline.
                    netToCarrier = entries.Sum(e => e.NetToCarrier),
                },
            });
        });

        app.MapGet("/reconciliation/{period}", async (string period, QuoterDbContext db, CancellationToken ct) =>
        {
            if (!TryParsePeriod(period, out var p)) return Results.BadRequest(new { error = "Period must be yyyy-MM" });
            var results = await db.ReconciliationResults.AsNoTracking()
                .Where(r => r.Period == p)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync(ct);
            var latestBatch = results.FirstOrDefault()?.BatchId;
            return Results.Ok(results.Where(r => r.BatchId == latestBatch).Select(r => new
            {
                r.ExternalReferenceId, r.Category, r.MgaGross, r.CarrierGross, r.Detail,
            }));
        });

        // Dev-only: builds the simulated carrier statement (with deliberate discrepancies) for a
        // period, standing in for the file a real carrier would send back.
        app.MapPost("/dev/carrier-statement/{period}", async (string period, QuoterDbContext db, CancellationToken ct) =>
        {
            if (!TryParsePeriod(period, out var p)) return Results.BadRequest(new { error = "Period must be yyyy-MM" });
            var rows = await DbSeeder.SeedCarrierStatementAsync(db, p, ct);
            return Results.Ok(new { period = p, statementRows = rows });
        });
    }

    private static async Task<IResult> EnqueueAsync(QuoterDbContext db, JobType type, string period, CancellationToken ct)
    {
        if (!TryParsePeriod(period, out var p))
            return Results.BadRequest(new { error = "Period must be yyyy-MM" });

        var job = new BatchJobRow
        {
            JobId = Guid.NewGuid(),
            Type = type,
            Status = JobStatus.Queued,
            Period = p,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.BatchJobs.Add(job);
        await db.SaveChangesAsync(ct);
        return Results.Accepted($"/jobs/{job.JobId}",
            new JobResponse(job.JobId, job.Type.ToString(), job.Status.ToString(), job.Period, 0, null, null));
    }

    private static bool TryParsePeriod(string period, out DateOnly firstOfMonth)
    {
        firstOfMonth = default;
        if (!DateTime.TryParseExact(period, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return false;
        firstOfMonth = DateOnly.FromDateTime(dt);
        return true;
    }
}
