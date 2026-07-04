namespace EOQuoter.Domain;

public enum ReferralReason
{
    RequiresManualIntake,
    OutOfAppetite,
    PriorClaimsThreshold,
    OutsideDelegatedAuthority,
    LimitExceedsAuthority,
    YearsInBusinessBelowFloor,
}

public abstract record EligibilityOutcome;
public sealed record Eligible : EligibilityOutcome;
public sealed record Referred(ReferralReason Reason) : EligibilityOutcome;
public sealed record Declined(ReferralReason Reason) : EligibilityOutcome;
