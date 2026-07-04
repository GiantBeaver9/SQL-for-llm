using EOQuoter.Data.Entities;
using EOQuoter.Domain;
using Microsoft.EntityFrameworkCore;

namespace EOQuoter.Data.Seed;

/// <summary>
/// Seeds the effective-dated reference data. Two rate versions on purpose — V1 (2025-01-01) and
/// V2 (2026-04-01, +8%) — so as-of pricing is demonstrable: a quote dated March 2026 must pick up
/// V1 even though V2 is the "most recent posted" row by the time you look.
/// </summary>
public static class DbSeeder
{
    public static readonly DateOnly V1 = new(2025, 1, 1);
    public static readonly DateOnly V2 = new(2026, 4, 1);

    public static async Task SeedAsync(QuoterDbContext db, CancellationToken ct = default)
    {
        if (await db.Rates.AnyAsync(ct)) return;

        db.UnderwritingParameters.Add(new UnderwritingParameterRow
        {
            EffectiveDate = V1,
            MinYearsInBusiness = 2,
            ClaimsKnockoutCount = 3,
            ClaimsWindowYears = 5,
        });

        // Appetite. RealEstateAgent enters appetite at V1 and is PULLED at 2026-01-01 — exiting
        // appetite is a new row, not a delete, so "was this in appetite last November?" stays answerable.
        var written = new[]
        {
            ProfessionClass.Accountant, ProfessionClass.Architect, ProfessionClass.ItConsultant,
            ProfessionClass.ManagementConsultant, ProfessionClass.InsuranceAgent, ProfessionClass.RealEstateAgent,
        };
        foreach (var cls in written)
            db.Appetite.Add(new AppetiteRow { ProfessionClass = (int)cls, IsWritten = true, EffectiveDate = V1 });
        db.Appetite.Add(new AppetiteRow { ProfessionClass = (int)ProfessionClass.RealEstateAgent, IsWritten = false, EffectiveDate = new DateOnly(2026, 1, 1) });
        // Lawyer: no row at all — never written.

        // Delegated authority by state. NY gets a mid-2025 raise — another effective-dated story.
        (string State, decimal MaxLimit, DateOnly Eff)[] authority =
        [
            ("CA", 2_000_000m, V1),
            ("NY", 1_000_000m, V1),
            ("NY", 2_000_000m, new DateOnly(2025, 7, 1)),
            ("TX", 2_000_000m, V1),
            ("IL", 1_000_000m, V1),
            ("FL", 500_000m, V1),
        ];
        foreach (var (state, max, eff) in authority)
            db.DelegatedAuthority.Add(new AuthorityRow { State = state, MaxLimit = max, EffectiveDate = eff });

        // Rate table: class-relativity × band base, two versions. base_rate here is the cell's base
        // premium in dollars (the accumulator's starting point), per the scaffold's rating pipeline.
        var classRelativity = new Dictionary<ProfessionClass, decimal>
        {
            [ProfessionClass.Accountant] = 1.00m,
            [ProfessionClass.Architect] = 1.25m,
            [ProfessionClass.ItConsultant] = 1.10m,
            [ProfessionClass.ManagementConsultant] = 1.20m,
            [ProfessionClass.InsuranceAgent] = 1.15m,
            [ProfessionClass.RealEstateAgent] = 1.05m,
        };
        var bandBase = new Dictionary<RevenueBand, decimal>
        {
            [RevenueBand.UpTo250K] = 1200m,
            [RevenueBand.From250KTo1M] = 2100m,
            [RevenueBand.From1MTo5M] = 3800m,
            [RevenueBand.From5MTo25M] = 7500m,
        };
        foreach (var (cls, rel) in classRelativity)
        foreach (var (band, baseAmt) in bandBase)
        {
            var v1Rate = Math.Round(baseAmt * rel, 4);
            db.Rates.Add(new RateRow { ProfessionClass = (int)cls, RevenueBand = (int)band, BaseRate = v1Rate, EffectiveDate = V1 });
            db.Rates.Add(new RateRow { ProfessionClass = (int)cls, RevenueBand = (int)band, BaseRate = Math.Round(v1Rate * 1.08m, 4), EffectiveDate = V2 });
        }

        // Limit factor curve — V2 hardens the top limits, so limit pricing is version-sensitive too.
        (decimal Limit, decimal FactorV1, decimal FactorV2)[] limits =
        [
            (250_000m, 0.85m, 0.85m),
            (500_000m, 1.00m, 1.00m),
            (1_000_000m, 1.25m, 1.30m),
            (2_000_000m, 1.60m, 1.70m),
        ];
        foreach (var (limit, f1, f2) in limits)
        {
            db.LimitFactors.Add(new LimitFactorRow { LimitAmount = limit, Factor = f1, EffectiveDate = V1 });
            db.LimitFactors.Add(new LimitFactorRow { LimitAmount = limit, Factor = f2, EffectiveDate = V2 });
        }

        (decimal Retention, decimal Factor)[] retentions =
        [
            (0m, 1.10m), (2_500m, 1.05m), (5_000m, 1.00m), (10_000m, 0.92m), (25_000m, 0.85m),
        ];
        foreach (var (retention, factor) in retentions)
            db.RetentionFactors.Add(new RetentionFactorRow { RetentionAmount = retention, Factor = factor, EffectiveDate = V1 });

        // Claims severity/recency curves — placeholder values from the design doc; the structure
        // (per-claim severity × recency, summed) is the point.
        (string Label, decimal Floor, decimal? Ceiling, decimal Load)[] severity =
        [
            ("$0-25k", 0m, 25_000m, 0.05m),
            ("$25-100k", 25_000m, 100_000m, 0.20m),
            ("$100k+", 100_000m, null, 0.50m),
        ];
        foreach (var (label, floor, ceiling, load) in severity)
            db.SeverityBands.Add(new SeverityBandRow { EffectiveDate = V1, Label = label, FloorInclusive = floor, CeilingExclusive = ceiling, Load = load });

        (string Label, double Floor, double Ceiling, decimal Weight)[] recency =
        [
            ("<1yr", 0, 1, 1.0m),
            ("1-3yr", 1, 3, 0.6m),
            ("3-5yr", 3, 5, 0.3m),
        ];
        foreach (var (label, floor, ceiling, weight) in recency)
            db.RecencyBands.Add(new RecencyBandRow { EffectiveDate = V1, Label = label, AgeFloorInclusiveYears = floor, AgeCeilingExclusiveYears = ceiling, Weight = weight });

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Builds the simulated carrier statement for a period from the policies actually bound, then
    /// plants deliberate discrepancies: one premium overstated by $100, one MGA policy dropped
    /// (MissingAtCarrier), and one ghost row the MGA never wrote (MissingAtMga). With no real
    /// carrier, the discrepancy path is what makes the reconciliation demonstrably real.
    /// </summary>
    public static async Task<int> SeedCarrierStatementAsync(QuoterDbContext db, DateOnly period, CancellationToken ct = default)
    {
        var periodStart = new DateTimeOffset(period.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var periodEnd = new DateTimeOffset(period.AddMonths(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        await db.CarrierStatements.Where(c => c.Period == period).ExecuteDeleteAsync(ct);

        var policies = await db.Policies
            .Where(p => p.BoundAt >= periodStart && p.BoundAt < periodEnd)
            .OrderBy(p => p.BoundAt)
            .ToListAsync(ct);

        var rows = new List<CarrierStatementRow>();
        for (var i = 0; i < policies.Count; i++)
        {
            if (i == 1) continue; // drop the second policy → MissingAtCarrier
            var p = policies[i];
            rows.Add(new CarrierStatementRow
            {
                Period = period,
                ExternalReferenceId = p.ExternalReferenceId,
                PolicyNumber = p.PolicyNumber,
                GrossPremium = i == 0 ? p.Premium + 100m : p.Premium, // overstate the first → PremiumMismatch
            });
        }
        rows.Add(new CarrierStatementRow
        {
            Period = period,
            ExternalReferenceId = "EXT-GHOST-0001", // carrier claims a policy the MGA never bound → MissingAtMga
            PolicyNumber = "EO-GHOST",
            GrossPremium = 999.99m,
        });

        db.CarrierStatements.AddRange(rows);
        await db.SaveChangesAsync(ct);
        return rows.Count;
    }
}
