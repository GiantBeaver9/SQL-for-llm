using EOQuoter.Domain;

namespace EOQuoter.Underwriting;

/// <summary>
/// Eligibility rules in fixed order, first knockout wins. Runs BEFORE rating — you don't price a
/// risk you're declining. Every rule reads its parameters as-of the quote date, so a decision made
/// in March is reproducible under March's rules even after the rules change.
///
/// Declines are risks the MGA simply doesn't write (appetite, claims history); referrals are risks
/// a human or the carrier might still take (authority gaps, thin track record).
/// </summary>
public class EligibilityEngine(IEligibilityDataSource source)
{
    public async Task<EligibilityOutcome> EvaluateAsync(RiskProfile profile, DateOnly quoteDate, CancellationToken ct = default)
    {
        // 1. Appetite: is this profession class written at all?
        if (!await source.IsClassInAppetiteAsync(profile.Class, quoteDate, ct))
            return new Declined(ReferralReason.OutOfAppetite);

        // 2. Delegated authority: can this MGA bind in this state, at this limit?
        var maxLimit = await source.GetDelegatedMaxLimitAsync(profile.State, quoteDate, ct);
        if (maxLimit is null)
            return new Referred(ReferralReason.OutsideDelegatedAuthority);
        if (profile.RequestedLimit > maxLimit)
            return new Referred(ReferralReason.LimitExceedsAuthority);

        var parameters = await source.GetParametersAsync(quoteDate, ct)
            ?? throw new InvalidOperationException($"No underwriting parameters in force at {quoteDate:yyyy-MM-dd}");

        // 3. Years-in-business floor: too new to judge — a human can still look.
        if (profile.YearsInBusiness < parameters.MinYearsInBusiness)
            return new Referred(ReferralReason.YearsInBusinessBelowFloor);

        // 4. Claims knockout: N+ claims in the window is a decline, full stop. Because this caps
        // the count, the rating engine only ever sees 0..N-1 claims — the claims load is bounded
        // by eligibility, not by the rating math.
        var windowStart = quoteDate.AddYears(-parameters.ClaimsWindowYears);
        var claimsInWindow = profile.PriorClaims.Count(c => c.OccurredOn >= windowStart && c.OccurredOn <= quoteDate);
        if (claimsInWindow >= parameters.ClaimsKnockoutCount)
            return new Declined(ReferralReason.PriorClaimsThreshold);

        return new Eligible();
    }
}
