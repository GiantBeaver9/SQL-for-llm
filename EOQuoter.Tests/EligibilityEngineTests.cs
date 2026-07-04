using EOQuoter.Domain;
using EOQuoter.Underwriting;

namespace EOQuoter.Tests;

public class EligibilityEngineTests
{
    private static readonly DateOnly QuoteDate = new(2026, 7, 1);
    private readonly EligibilityEngine engine = new(new FakeEligibilitySource());

    [Fact]
    public async Task Clean_risk_is_eligible()
    {
        Assert.IsType<Eligible>(await engine.EvaluateAsync(Profiles.Clean(), QuoteDate));
    }

    [Fact]
    public async Task Out_of_appetite_class_is_declined()
    {
        var profile = Profiles.Clean() with { Class = ProfessionClass.Lawyer };
        var outcome = Assert.IsType<Declined>(await engine.EvaluateAsync(profile, QuoteDate));
        Assert.Equal(ReferralReason.OutOfAppetite, outcome.Reason);
    }

    [Fact]
    public async Task First_knockout_wins_appetite_beats_authority()
    {
        // Out-of-appetite AND over-limit AND too new: the FIRST rule in the fixed order decides.
        var profile = Profiles.Clean("NY") with
        {
            Class = ProfessionClass.Lawyer,
            RequestedLimit = 5_000_000m,
            YearsInBusiness = 0,
        };
        var outcome = Assert.IsType<Declined>(await engine.EvaluateAsync(profile, QuoteDate));
        Assert.Equal(ReferralReason.OutOfAppetite, outcome.Reason);
    }

    [Fact]
    public async Task State_without_delegated_authority_is_referred()
    {
        var outcome = Assert.IsType<Referred>(await engine.EvaluateAsync(Profiles.Clean("WY"), QuoteDate));
        Assert.Equal(ReferralReason.OutsideDelegatedAuthority, outcome.Reason);
    }

    [Fact]
    public async Task Limit_above_state_authority_is_referred_not_declined()
    {
        var profile = Profiles.Clean("NY") with { RequestedLimit = 2_000_000m }; // NY caps at 1M
        var outcome = Assert.IsType<Referred>(await engine.EvaluateAsync(profile, QuoteDate));
        Assert.Equal(ReferralReason.LimitExceedsAuthority, outcome.Reason);
    }

    [Fact]
    public async Task Below_years_in_business_floor_is_referred()
    {
        var profile = Profiles.Clean() with { YearsInBusiness = 1 };
        var outcome = Assert.IsType<Referred>(await engine.EvaluateAsync(profile, QuoteDate));
        Assert.Equal(ReferralReason.YearsInBusinessBelowFloor, outcome.Reason);
    }

    [Fact]
    public async Task Three_claims_in_window_is_a_decline()
    {
        var profile = Profiles.WithClaims(
            new PriorClaim(5_000m, QuoteDate.AddYears(-1)),
            new PriorClaim(5_000m, QuoteDate.AddYears(-2)),
            new PriorClaim(5_000m, QuoteDate.AddYears(-3)));
        var outcome = Assert.IsType<Declined>(await engine.EvaluateAsync(profile, QuoteDate));
        Assert.Equal(ReferralReason.PriorClaimsThreshold, outcome.Reason);
    }

    [Fact]
    public async Task Two_claims_in_window_still_eligible_rating_engine_prices_them()
    {
        var profile = Profiles.WithClaims(
            new PriorClaim(5_000m, QuoteDate.AddYears(-1)),
            new PriorClaim(5_000m, QuoteDate.AddYears(-2)));
        Assert.IsType<Eligible>(await engine.EvaluateAsync(profile, QuoteDate));
    }

    [Fact]
    public async Task Claims_older_than_the_window_do_not_count_toward_the_knockout()
    {
        // Three claims but only two inside the 5-year window → eligible.
        var profile = Profiles.WithClaims(
            new PriorClaim(5_000m, QuoteDate.AddYears(-1)),
            new PriorClaim(5_000m, QuoteDate.AddYears(-2)),
            new PriorClaim(5_000m, QuoteDate.AddYears(-6)));
        Assert.IsType<Eligible>(await engine.EvaluateAsync(profile, QuoteDate));
    }
}
