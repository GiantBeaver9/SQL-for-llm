namespace EOQuoter.Domain;

/// <summary>
/// Effective-dated lookups the rating engine depends on. Every method takes asOf (the quote date)
/// and must answer with the version in force AT that date — i.e. the latest row whose
/// effective_date &lt;= asOf. Implementations returning "most recent posted" are wrong: that
/// silently reprices old submissions at rates that didn't exist yet.
/// Null means "no row in force" and is an error the engine surfaces, not a default.
/// </summary>
public interface IRatingDataSource
{
    Task<(decimal BaseRate, DateOnly EffectiveDate)?> GetBaseRateAsync(ProfessionClass cls, RevenueBand band, DateOnly asOf, CancellationToken ct = default);
    Task<decimal?> GetLimitFactorAsync(decimal limit, DateOnly asOf, CancellationToken ct = default);
    Task<decimal?> GetRetentionFactorAsync(decimal retention, DateOnly asOf, CancellationToken ct = default);

    /// <summary>Severity bands in force at asOf: (band label, inclusive floor, exclusive ceiling or null, load).</summary>
    Task<IReadOnlyList<SeverityBand>> GetSeverityBandsAsync(DateOnly asOf, CancellationToken ct = default);

    /// <summary>Recency bands in force at asOf: (band label, inclusive age floor in years, exclusive ceiling, weight).</summary>
    Task<IReadOnlyList<RecencyBand>> GetRecencyBandsAsync(DateOnly asOf, CancellationToken ct = default);
}

public record SeverityBand(string Label, decimal FloorInclusive, decimal? CeilingExclusive, decimal Load);
public record RecencyBand(string Label, double AgeFloorInclusiveYears, double AgeCeilingExclusiveYears, decimal Weight);

/// <summary>Eligibility rule parameters — appetite, delegated authority, floors — all effective-dated
/// and queried as-of the quote date, same as rates.</summary>
public interface IEligibilityDataSource
{
    Task<bool> IsClassInAppetiteAsync(ProfessionClass cls, DateOnly asOf, CancellationToken ct = default);

    /// <summary>Max limit the carrier delegates for this state, in force at asOf. Null = no authority for the state at all.</summary>
    Task<decimal?> GetDelegatedMaxLimitAsync(string state, DateOnly asOf, CancellationToken ct = default);

    /// <summary>Underwriting parameters (years-in-business floor, claims knockout count + window) in force at asOf.</summary>
    Task<UnderwritingParameters?> GetParametersAsync(DateOnly asOf, CancellationToken ct = default);
}

public record UnderwritingParameters(int MinYearsInBusiness, int ClaimsKnockoutCount, int ClaimsWindowYears);
