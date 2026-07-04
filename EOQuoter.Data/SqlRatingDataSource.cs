using EOQuoter.Domain;
using Microsoft.EntityFrameworkCore;

namespace EOQuoter.Data;

/// <summary>
/// Every lookup here is the same temporal query: the latest row whose effective_date &lt;= asOf.
/// The &lt;= bound is the load-bearing guard — "most recent posted" alone would reprice a February
/// submission at a rate posted in June, a silent correctness bug.
/// </summary>
public class SqlRatingDataSource(QuoterDbContext db) : IRatingDataSource
{
    public async Task<(decimal BaseRate, DateOnly EffectiveDate)?> GetBaseRateAsync(
        ProfessionClass cls, RevenueBand band, DateOnly asOf, CancellationToken ct = default)
    {
        var row = await db.Rates.AsNoTracking()
            .Where(r => r.ProfessionClass == (int)cls && r.RevenueBand == (int)band && r.EffectiveDate <= asOf)
            .OrderByDescending(r => r.EffectiveDate)
            .FirstOrDefaultAsync(ct);
        return row is null ? null : (row.BaseRate, row.EffectiveDate);
    }

    public async Task<decimal?> GetLimitFactorAsync(decimal limit, DateOnly asOf, CancellationToken ct = default)
    {
        var row = await db.LimitFactors.AsNoTracking()
            .Where(r => r.LimitAmount == limit && r.EffectiveDate <= asOf)
            .OrderByDescending(r => r.EffectiveDate)
            .FirstOrDefaultAsync(ct);
        return row?.Factor;
    }

    public async Task<decimal?> GetRetentionFactorAsync(decimal retention, DateOnly asOf, CancellationToken ct = default)
    {
        var row = await db.RetentionFactors.AsNoTracking()
            .Where(r => r.RetentionAmount == retention && r.EffectiveDate <= asOf)
            .OrderByDescending(r => r.EffectiveDate)
            .FirstOrDefaultAsync(ct);
        return row?.Factor;
    }

    public async Task<IReadOnlyList<SeverityBand>> GetSeverityBandsAsync(DateOnly asOf, CancellationToken ct = default)
    {
        // A band SET shares one effective_date; take the set in force at asOf, not a per-row mix.
        var inForce = await db.SeverityBands.AsNoTracking()
            .Where(r => r.EffectiveDate <= asOf)
            .MaxAsync(r => (DateOnly?)r.EffectiveDate, ct);
        if (inForce is null) return [];
        return await db.SeverityBands.AsNoTracking()
            .Where(r => r.EffectiveDate == inForce)
            .OrderBy(r => r.FloorInclusive)
            .Select(r => new SeverityBand(r.Label, r.FloorInclusive, r.CeilingExclusive, r.Load))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<RecencyBand>> GetRecencyBandsAsync(DateOnly asOf, CancellationToken ct = default)
    {
        var inForce = await db.RecencyBands.AsNoTracking()
            .Where(r => r.EffectiveDate <= asOf)
            .MaxAsync(r => (DateOnly?)r.EffectiveDate, ct);
        if (inForce is null) return [];
        return await db.RecencyBands.AsNoTracking()
            .Where(r => r.EffectiveDate == inForce)
            .OrderBy(r => r.AgeFloorInclusiveYears)
            .Select(r => new RecencyBand(r.Label, r.AgeFloorInclusiveYears, r.AgeCeilingExclusiveYears, r.Weight))
            .ToListAsync(ct);
    }
}

public class SqlEligibilityDataSource(QuoterDbContext db) : IEligibilityDataSource
{
    public async Task<bool> IsClassInAppetiteAsync(ProfessionClass cls, DateOnly asOf, CancellationToken ct = default)
    {
        var row = await db.Appetite.AsNoTracking()
            .Where(r => r.ProfessionClass == (int)cls && r.EffectiveDate <= asOf)
            .OrderByDescending(r => r.EffectiveDate)
            .FirstOrDefaultAsync(ct);
        // No appetite row at all = never written = out of appetite.
        return row?.IsWritten ?? false;
    }

    public async Task<decimal?> GetDelegatedMaxLimitAsync(string state, DateOnly asOf, CancellationToken ct = default)
    {
        var row = await db.DelegatedAuthority.AsNoTracking()
            .Where(r => r.State == state && r.EffectiveDate <= asOf)
            .OrderByDescending(r => r.EffectiveDate)
            .FirstOrDefaultAsync(ct);
        return row?.MaxLimit;
    }

    public async Task<UnderwritingParameters?> GetParametersAsync(DateOnly asOf, CancellationToken ct = default)
    {
        var row = await db.UnderwritingParameters.AsNoTracking()
            .Where(r => r.EffectiveDate <= asOf)
            .OrderByDescending(r => r.EffectiveDate)
            .FirstOrDefaultAsync(ct);
        return row is null ? null : new UnderwritingParameters(row.MinYearsInBusiness, row.ClaimsKnockoutCount, row.ClaimsWindowYears);
    }
}
