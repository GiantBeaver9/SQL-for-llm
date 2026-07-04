namespace EOQuoter.Domain;

/// <summary>Profession classes the MGA writes (or knows it doesn't). Codes are stable — they key the rate table.</summary>
public enum ProfessionClass
{
    Unknown = 0,
    Accountant = 10,
    Architect = 20,
    ItConsultant = 30,
    ManagementConsultant = 40,
    InsuranceAgent = 50,
    RealEstateAgent = 60,   // deliberately out of appetite in seed data
    Lawyer = 70,            // deliberately out of appetite in seed data
}

/// <summary>Annual-revenue bands. Band, not raw revenue, keys the rate table.</summary>
public enum RevenueBand
{
    Unknown = 0,
    UpTo250K = 1,
    From250KTo1M = 2,
    From1MTo5M = 3,
    From5MTo25M = 4,
}

/// <summary>A prior claim as disclosed on the submission. Amount + date is all rating needs:
/// severity band and recency band are derived at rating time, as-of the quote date.</summary>
public record PriorClaim(decimal Amount, DateOnly OccurredOn);

/// <summary>The normalized risk. The scaffold sketched PriorClaimsCount; per-claim recency scoring
/// requires the underlying claims, so the list is the source of truth and the count is derived.</summary>
public record RiskProfile(
    ProfessionClass Class,
    RevenueBand Revenue,
    decimal RequestedLimit,
    decimal RequestedRetention,
    IReadOnlyList<PriorClaim> PriorClaims,
    int YearsInBusiness,
    string State)
{
    public int PriorClaimsCount => PriorClaims.Count;
}
