using EOQuoter.Domain;

namespace EOQuoter.Underwriting;

/// <summary>Thrown when a required rate or factor has no version in force at the quote date.
/// A config gap, not a rating answer — the API routes it to manual handling, never a guessed price.</summary>
public class RatingException(string message) : Exception(message);

/// <summary>
/// Deterministic rating: a pure function of (RiskProfile, quoteDate) — no clock reads, no
/// randomness, no LLM. Computed as an ordered accumulator, Base → ×Limit → ×Retention → ×Claims,
/// where each step records the ACTUAL dollar effect given everything before it. Sequential and
/// marginal means the deltas sum exactly to FinalPremium − BasePremium, so the policy document can
/// show what each factor cost and the total reconciles. The ledger IS the rating audit record.
/// </summary>
public class RatingEngine(IRatingDataSource source)
{
    public async Task<RatingResult> RateAsync(RiskProfile profile, DateOnly quoteDate, CancellationToken ct = default)
    {
        var baseRate = await source.GetBaseRateAsync(profile.Class, profile.Revenue, quoteDate, ct)
            ?? throw new RatingException($"No base rate in force for {profile.Class}/{profile.Revenue} at {quoteDate:yyyy-MM-dd}");
        var limitFactor = await source.GetLimitFactorAsync(profile.RequestedLimit, quoteDate, ct)
            ?? throw new RatingException($"No limit factor in force for {profile.RequestedLimit:0} at {quoteDate:yyyy-MM-dd} — not an offered limit");
        var retentionFactor = await source.GetRetentionFactorAsync(profile.RequestedRetention, quoteDate, ct)
            ?? throw new RatingException($"No retention factor in force for {profile.RequestedRetention:0} at {quoteDate:yyyy-MM-dd} — not an offered retention");

        var basePremium = Math.Round(baseRate.BaseRate, 2);
        var ledger = new List<RatingStep>();
        var running = basePremium;

        ledger.Add(new RatingStep(1, "Base", FactorKind.Multiplier, 1m,
            running, 0m, running,
            $"rate_table {profile.Class}/{profile.Revenue}, version effective {baseRate.EffectiveDate:yyyy-MM-dd}"));

        running = ApplyMultiplier(ledger, 2, "Limit", limitFactor, running,
            $"limit factor for {profile.RequestedLimit:N0}");
        running = ApplyMultiplier(ledger, 3, "Retention", retentionFactor, running,
            $"retention factor for {profile.RequestedRetention:N0}");

        var (claimsFactor, breakdown) = await ScoreClaimsAsync(profile, quoteDate, ct);
        var afterClaims = Math.Round(running * claimsFactor, 2);
        ledger.Add(new RatingStep(4, "Claims", FactorKind.Multiplier, claimsFactor,
            running, afterClaims - running, afterClaims,
            breakdown.Count == 0
                ? "no scorable prior claims"
                : $"1 + sum of {breakdown.Count} per-claim additive load(s) — loads sum, they do not multiply",
            breakdown));
        running = afterClaims;

        return new RatingResult(basePremium, baseRate.EffectiveDate, ledger, running);
    }

    private static decimal ApplyMultiplier(List<RatingStep> ledger, int order, string name, decimal factor, decimal before, string note)
    {
        var after = Math.Round(before * factor, 2);
        ledger.Add(new RatingStep(order, name, FactorKind.Multiplier, factor, before, after - before, after, note));
        return after;
    }

    /// <summary>
    /// Per-claim scoring: load = SeverityLoad(band) × RecencyWeight(band), then loads SUM into
    /// 1 + Σ. Additive, not multiplicative — two moderate claims should not square the premium.
    /// This also yields the required ordering: a recent claim carries full weight, so
    /// recent+old loads strictly higher than old+old at equal severity.
    /// </summary>
    private async Task<(decimal Factor, List<ClaimLoad> Breakdown)> ScoreClaimsAsync(RiskProfile profile, DateOnly quoteDate, CancellationToken ct)
    {
        if (profile.PriorClaims.Count == 0) return (1m, []);

        var severityBands = await source.GetSeverityBandsAsync(quoteDate, ct);
        var recencyBands = await source.GetRecencyBandsAsync(quoteDate, ct);
        if (severityBands.Count == 0 || recencyBands.Count == 0)
            throw new RatingException($"No severity/recency curve in force at {quoteDate:yyyy-MM-dd}");

        var breakdown = new List<ClaimLoad>();
        foreach (var claim in profile.PriorClaims.OrderBy(c => c.OccurredOn))
        {
            var ageYears = (quoteDate.DayNumber - claim.OccurredOn.DayNumber) / 365.25;
            var recency = recencyBands.FirstOrDefault(r => ageYears >= r.AgeFloorInclusiveYears && ageYears < r.AgeCeilingExclusiveYears);
            if (recency is null) continue; // outside the recency window (e.g. >5yr) — carries no load

            var severity = severityBands.FirstOrDefault(s => claim.Amount >= s.FloorInclusive
                && (s.CeilingExclusive is null || claim.Amount < s.CeilingExclusive))
                ?? throw new RatingException($"Claim amount {claim.Amount:0} matches no severity band at {quoteDate:yyyy-MM-dd}");

            var load = severity.Load * recency.Weight;
            breakdown.Add(new ClaimLoad(claim.Amount, claim.OccurredOn, severity.Label, severity.Load, recency.Label, recency.Weight, load));
        }

        return (1m + breakdown.Sum(b => b.Load), breakdown);
    }
}
