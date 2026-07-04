namespace EOQuoter.Data.Entities;

// Every table in this file is effective-dated: rows are never updated in place — a rate change is
// a NEW row with a new effective_date. Lookups are always "latest row whose effective_date <= asOf".

/// <summary>The rate table from the scaffold doc: (profession_class, revenue_band, effective_date) → base rate.</summary>
public class RateRow
{
    public int ProfessionClass { get; set; }
    public int RevenueBand { get; set; }
    public decimal BaseRate { get; set; }
    public DateOnly EffectiveDate { get; set; }
}

public class LimitFactorRow
{
    public decimal LimitAmount { get; set; }
    public decimal Factor { get; set; }
    public DateOnly EffectiveDate { get; set; }
}

public class RetentionFactorRow
{
    public decimal RetentionAmount { get; set; }
    public decimal Factor { get; set; }
    public DateOnly EffectiveDate { get; set; }
}

/// <summary>Appetite: is this profession class written at all, as of a date. A class leaves appetite
/// via a new row with is_written = 0, not a delete — history stays queryable.</summary>
public class AppetiteRow
{
    public int ProfessionClass { get; set; }
    public bool IsWritten { get; set; }
    public DateOnly EffectiveDate { get; set; }
}

/// <summary>Delegated binding authority: max limit the carrier lets the MGA bind per state.</summary>
public class AuthorityRow
{
    public string State { get; set; } = null!;
    public decimal MaxLimit { get; set; }
    public DateOnly EffectiveDate { get; set; }
}

/// <summary>Underwriting rule parameters — versioned like rates, so "why was this declined in March"
/// is answerable with March's rules.</summary>
public class UnderwritingParameterRow
{
    public DateOnly EffectiveDate { get; set; }
    public int MinYearsInBusiness { get; set; }
    public int ClaimsKnockoutCount { get; set; }
    public int ClaimsWindowYears { get; set; }
}

/// <summary>Claims severity bands. A band set is the group of rows sharing one effective_date;
/// the set in force at asOf is the group with the latest effective_date &lt;= asOf.</summary>
public class SeverityBandRow
{
    public DateOnly EffectiveDate { get; set; }
    public string Label { get; set; } = null!;
    public decimal FloorInclusive { get; set; }
    public decimal? CeilingExclusive { get; set; }
    public decimal Load { get; set; }
}

public class RecencyBandRow
{
    public DateOnly EffectiveDate { get; set; }
    public string Label { get; set; } = null!;
    public double AgeFloorInclusiveYears { get; set; }
    public double AgeCeilingExclusiveYears { get; set; }
    public decimal Weight { get; set; }
}
