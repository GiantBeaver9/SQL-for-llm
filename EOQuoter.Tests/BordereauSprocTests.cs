using System.Data;
using EOQuoter.Data.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EOQuoter.Tests;

/// <summary>
/// The set-based batch path against real SQL Server: generation validates the set and routes
/// per-item failures to rejects (no cursor, no batch abort), and reconciliation's full outer join
/// classifies every discrepancy the simulated carrier file plants.
/// </summary>
public class BordereauSprocTests : IClassFixture<SqlServerFixture>
{
    private static readonly DateOnly Period = new(2026, 7, 1);
    private readonly SqlServerFixture fixture;

    public BordereauSprocTests(SqlServerFixture fixture) => this.fixture = fixture;

    private static PolicyRow Policy(int n, decimal premium, decimal commissionRate = 0.15m, string? extRef = null) => new()
    {
        PolicyId = Guid.NewGuid(),
        PolicyNumber = $"EO-2026-{n:D6}",
        QuoteId = Guid.NewGuid(),
        EffectiveDate = Period,
        ExpiryDate = Period.AddYears(1),
        Premium = premium,
        CommissionRate = commissionRate,
        ExternalReferenceId = extRef ?? $"EXT-TEST-{n:D4}",
        Status = "Issued",
        BoundAt = new DateTimeOffset(Period.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero).AddDays(n),
    };

    private static async Task<(int A, int B)> RunSprocAsync(Data.QuoterDbContext db, string sproc, Guid batchId, (string, string) outputs)
    {
        var pA = new SqlParameter("@a", SqlDbType.Int) { Direction = ParameterDirection.Output };
        var pB = new SqlParameter("@b", SqlDbType.Int) { Direction = ParameterDirection.Output };
        await db.Database.ExecuteSqlRawAsync(
            $"EXEC dbo.{sproc} @batch_id = @batch, @period = @period, @rejects_ref = @rejects, @{outputs.Item1} = @a OUTPUT, @{outputs.Item2} = @b OUTPUT",
            new SqlParameter("@batch", batchId),
            new SqlParameter("@period", SqlDbType.Date) { Value = Period.ToDateTime(TimeOnly.MinValue) },
            new SqlParameter("@rejects", batchId),
            pA, pB);
        return ((int)pA.Value!, (int)pB.Value!);
    }

    [SqlServerFact]
    public async Task Generation_commits_valid_set_and_routes_failures_to_rejects()
    {
        using var db = fixture.CreateContext();
        db.Policies.AddRange(
            Policy(1, 3000m),
            Policy(2, 4500m),
            Policy(3, 2000m, commissionRate: 1.5m)); // invalid: commission out of range
        await db.SaveChangesAsync();

        var batchId = Guid.NewGuid();
        var (inserted, rejected) = await RunSprocAsync(db, "usp_GenerateBordereau", batchId, ("inserted", "rejected"));

        Assert.Equal(2, inserted);
        Assert.Equal(1, rejected);

        var entries = await db.BordereauEntries.Where(b => b.Period == Period).ToListAsync();
        Assert.Equal(2, entries.Count);
        // Double-entry to the cent on every row, and the remittance ties out to the entry sum.
        Assert.All(entries, e => Assert.Equal(e.GrossPremium, e.Commission + e.NetToCarrier));
        Assert.Equal(entries.Sum(e => e.GrossPremium) - entries.Sum(e => e.Commission), entries.Sum(e => e.NetToCarrier));

        var reject = Assert.Single(await db.RejectedItems.Where(r => r.BatchId == batchId).ToListAsync());
        Assert.Equal("CommissionRateOutOfRange", reject.Reason);

        // Idempotent per period: a rerun (resume) must not double-report.
        var (rerunInserted, _) = await RunSprocAsync(db, "usp_GenerateBordereau", Guid.NewGuid(), ("inserted", "rejected"));
        Assert.Equal(0, rerunInserted);
    }

    [SqlServerFact]
    public async Task Reconciliation_classifies_every_seeded_discrepancy()
    {
        using var db = fixture.CreateContext();

        // Fresh period slice for this test: distinct external refs.
        var pOk = Policy(10, 5000m, extRef: "EXT-RECON-OK");
        var pMismatch = Policy(11, 6000m, extRef: "EXT-RECON-MISMATCH");
        var pMissing = Policy(12, 7000m, extRef: "EXT-RECON-DROPPED");
        db.Policies.AddRange(pOk, pMismatch, pMissing);
        await db.SaveChangesAsync();
        await RunSprocAsync(db, "usp_GenerateBordereau", Guid.NewGuid(), ("inserted", "rejected"));

        db.CarrierStatements.AddRange(
            new CarrierStatementRow { Period = Period, ExternalReferenceId = "EXT-RECON-OK", GrossPremium = 5000m },
            new CarrierStatementRow { Period = Period, ExternalReferenceId = "EXT-RECON-MISMATCH", GrossPremium = 6100m }, // premium delta
            // EXT-RECON-DROPPED deliberately absent → MissingAtCarrier
            new CarrierStatementRow { Period = Period, ExternalReferenceId = "EXT-RECON-GHOST", GrossPremium = 999.99m }); // MGA never wrote it
        await db.SaveChangesAsync();

        var batchId = Guid.NewGuid();
        var (matched, discrepancies) = await RunSprocAsync(db, "usp_ReconcileBordereau", batchId, ("matched", "discrepancies"));
        var results = await db.ReconciliationResults.Where(r => r.BatchId == batchId).ToListAsync();

        Assert.Equal("Matched", results.Single(r => r.ExternalReferenceId == "EXT-RECON-OK").Category);
        Assert.Equal("PremiumMismatch", results.Single(r => r.ExternalReferenceId == "EXT-RECON-MISMATCH").Category);
        Assert.Equal("MissingAtCarrier", results.Single(r => r.ExternalReferenceId == "EXT-RECON-DROPPED").Category);
        Assert.Equal("MissingAtMga", results.Single(r => r.ExternalReferenceId == "EXT-RECON-GHOST").Category);
        Assert.Equal(results.Count(r => r.Category == "Matched"), matched);
        Assert.Equal(results.Count(r => r.Category != "Matched"), discrepancies);
    }
}
