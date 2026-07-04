using System.Text.Json;
using EOQuoter.Domain;
using EOQuoter.Underwriting;

namespace EOQuoter.Tests;

public class RatingEngineTests
{
    private static readonly DateOnly QuoteDate = new(2026, 7, 1);
    private readonly RatingEngine engine = new(new FakeRatingSource());

    [Fact]
    public async Task Rating_is_deterministic_same_input_same_ledger()
    {
        var profile = Profiles.WithClaims(
            new PriorClaim(12_000m, new DateOnly(2025, 9, 1)),
            new PriorClaim(60_000m, new DateOnly(2023, 2, 15)));

        var first = await engine.RateAsync(profile, QuoteDate);
        var second = await engine.RateAsync(profile, QuoteDate);

        // Byte-identical ledgers — no clock reads, no randomness, nothing environmental.
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
    }

    [Theory]
    [InlineData(250_000, 0, 0)]      // min limit, first-dollar retention, clean
    [InlineData(1_000_000, 5_000, 1)]
    [InlineData(2_000_000, 25_000, 2)]
    public async Task Ledger_ties_out_deltas_sum_to_final_minus_base(decimal limit, decimal retention, int claimCount)
    {
        var claims = Enumerable.Range(0, claimCount)
            .Select(i => new PriorClaim(30_000m * (i + 1), QuoteDate.AddYears(-(i + 1)).AddDays(-40)))
            .ToArray();
        var profile = Profiles.Clean() with { RequestedLimit = limit, RequestedRetention = retention, PriorClaims = claims };

        var result = await engine.RateAsync(profile, QuoteDate);

        Assert.Equal(result.FinalPremium - result.BasePremium, result.Ledger.Sum(s => s.DollarDelta));
        // And each step's arithmetic is internally consistent.
        foreach (var step in result.Ledger)
            Assert.Equal(step.PremiumAfter - step.PremiumBefore, step.DollarDelta);
    }

    [Fact]
    public async Task Ledger_has_fixed_order_base_limit_retention_claims()
    {
        var result = await engine.RateAsync(Profiles.Clean(), QuoteDate);
        Assert.Equal(new[] { "Base", "Limit", "Retention", "Claims" }, result.Ledger.Select(s => s.Name).ToArray());
        Assert.Equal(new[] { 1, 2, 3, 4 }, result.Ledger.Select(s => s.Order).ToArray());
    }

    [Fact]
    public async Task Claims_load_is_additive_not_multiplicative()
    {
        // Two $25-100k claims, both <1yr old: each load = 0.20 × 1.0. Additive → factor 1.40.
        // Multiplicative would give 1.2 × 1.2 = 1.44 — the premium explosion the design rejects.
        var profile = Profiles.WithClaims(
            new PriorClaim(50_000m, QuoteDate.AddMonths(-3)),
            new PriorClaim(50_000m, QuoteDate.AddMonths(-6)));

        var result = await engine.RateAsync(profile, QuoteDate);
        var claimsStep = result.Ledger.Single(s => s.Name == "Claims");

        Assert.Equal(1.40m, claimsStep.Value);
        Assert.Equal(2, claimsStep.Breakdown!.Count);
        Assert.All(claimsStep.Breakdown!, c => Assert.Equal(0.20m, c.Load));
    }

    [Fact]
    public async Task Recent_plus_old_claim_loads_higher_than_two_old_claims_of_same_severity()
    {
        var oldDate = QuoteDate.AddYears(-4);          // 3-5yr band, weight 0.3
        var recentDate = QuoteDate.AddMonths(-6);      // <1yr band, weight 1.0
        const decimal amount = 50_000m;                // $25-100k band on both

        var recentPlusOld = await engine.RateAsync(
            Profiles.WithClaims(new PriorClaim(amount, recentDate), new PriorClaim(amount, oldDate)), QuoteDate);
        var twoOld = await engine.RateAsync(
            Profiles.WithClaims(new PriorClaim(amount, oldDate), new PriorClaim(amount, oldDate.AddMonths(1))), QuoteDate);

        Assert.True(recentPlusOld.FinalPremium > twoOld.FinalPremium,
            "a recent claim carries full recency weight and must load higher than an equally severe old one");
    }

    [Fact]
    public async Task Claims_outside_the_recency_window_carry_no_load()
    {
        var profile = Profiles.WithClaims(new PriorClaim(500_000m, QuoteDate.AddYears(-7)));
        var result = await engine.RateAsync(profile, QuoteDate);
        var claimsStep = result.Ledger.Single(s => s.Name == "Claims");

        Assert.Equal(1m, claimsStep.Value);
        Assert.Equal(0m, claimsStep.DollarDelta);
    }

    [Fact]
    public async Task Off_menu_limit_is_a_rating_exception_not_a_guessed_price()
    {
        var profile = Profiles.Clean() with { RequestedLimit = 750_000m };
        await Assert.ThrowsAsync<RatingException>(() => engine.RateAsync(profile, QuoteDate));
    }

    [Fact]
    public async Task Claims_step_breakdown_reconciles_with_the_step_factor()
    {
        var profile = Profiles.WithClaims(
            new PriorClaim(10_000m, QuoteDate.AddMonths(-4)),   // 0.05 × 1.0
            new PriorClaim(150_000m, QuoteDate.AddYears(-2)));  // 0.50 × 0.6
        var result = await engine.RateAsync(profile, QuoteDate);
        var claimsStep = result.Ledger.Single(s => s.Name == "Claims");

        Assert.Equal(1m + claimsStep.Breakdown!.Sum(c => c.Load), claimsStep.Value);
        Assert.Equal(1.35m, claimsStep.Value);
    }
}
