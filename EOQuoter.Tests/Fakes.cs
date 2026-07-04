using EOQuoter.Domain;

namespace EOQuoter.Tests;

/// <summary>In-memory rating source with the doc's placeholder curves, effective V1. Pure — no DB —
/// so engine tests exercise rating logic alone.</summary>
public class FakeRatingSource : IRatingDataSource
{
    public static readonly DateOnly RateVersion = new(2025, 1, 1);

    public decimal BaseRate { get; init; } = 2100m;

    public Task<(decimal BaseRate, DateOnly EffectiveDate)?> GetBaseRateAsync(ProfessionClass cls, RevenueBand band, DateOnly asOf, CancellationToken ct = default) =>
        Task.FromResult<(decimal, DateOnly)?>((BaseRate, RateVersion));

    public Task<decimal?> GetLimitFactorAsync(decimal limit, DateOnly asOf, CancellationToken ct = default) =>
        Task.FromResult<decimal?>(limit switch
        {
            250_000m => 0.85m, 500_000m => 1.00m, 1_000_000m => 1.25m, 2_000_000m => 1.60m, _ => null,
        });

    public Task<decimal?> GetRetentionFactorAsync(decimal retention, DateOnly asOf, CancellationToken ct = default) =>
        Task.FromResult<decimal?>(retention switch
        {
            0m => 1.10m, 2_500m => 1.05m, 5_000m => 1.00m, 10_000m => 0.92m, 25_000m => 0.85m, _ => null,
        });

    public Task<IReadOnlyList<SeverityBand>> GetSeverityBandsAsync(DateOnly asOf, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SeverityBand>>(
        [
            new("$0-25k", 0m, 25_000m, 0.05m),
            new("$25-100k", 25_000m, 100_000m, 0.20m),
            new("$100k+", 100_000m, null, 0.50m),
        ]);

    public Task<IReadOnlyList<RecencyBand>> GetRecencyBandsAsync(DateOnly asOf, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<RecencyBand>>(
        [
            new("<1yr", 0, 1, 1.0m),
            new("1-3yr", 1, 3, 0.6m),
            new("3-5yr", 3, 5, 0.3m),
        ]);
}

public class FakeEligibilitySource : IEligibilityDataSource
{
    public HashSet<ProfessionClass> WrittenClasses { get; init; } =
        [ProfessionClass.Accountant, ProfessionClass.ItConsultant];

    public Dictionary<string, decimal> StateAuthority { get; init; } = new()
    {
        ["CA"] = 2_000_000m,
        ["NY"] = 1_000_000m,
    };

    public UnderwritingParameters Parameters { get; init; } = new(2, 3, 5);

    public Task<bool> IsClassInAppetiteAsync(ProfessionClass cls, DateOnly asOf, CancellationToken ct = default) =>
        Task.FromResult(WrittenClasses.Contains(cls));

    public Task<decimal?> GetDelegatedMaxLimitAsync(string state, DateOnly asOf, CancellationToken ct = default) =>
        Task.FromResult(StateAuthority.TryGetValue(state, out var max) ? (decimal?)max : null);

    public Task<UnderwritingParameters?> GetParametersAsync(DateOnly asOf, CancellationToken ct = default) =>
        Task.FromResult<UnderwritingParameters?>(Parameters);
}

public static class Profiles
{
    public static RiskProfile Clean(string state = "CA") => new(
        ProfessionClass.Accountant, RevenueBand.From250KTo1M,
        1_000_000m, 5_000m, [], 8, state);

    public static RiskProfile WithClaims(params PriorClaim[] claims) =>
        Clean() with { PriorClaims = claims };
}
