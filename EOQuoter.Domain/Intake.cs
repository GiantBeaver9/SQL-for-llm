namespace EOQuoter.Domain;

/// <summary>A submission as received, before any interpretation.</summary>
public record RawSubmission(string Content, string ContentType);

public abstract record IntakeResult;
public sealed record Parsed(RiskProfile Profile) : IntakeResult;
public sealed record Unparseable(string Reason) : IntakeResult;

public interface ISubmissionParser
{
    IntakeResult Parse(RawSubmission raw);
}

/// <summary>
/// Extension seam — no implementation ships. If degraded-input (scan/handwriting/OCR) volume
/// ever justifies automation, a handler registers here and is tried BEFORE human referral.
/// A real handler must be able to escalate ("can't read this") rather than confidently emit
/// fields from a broken submission — hence it returns IntakeResult, not RiskProfile.
/// </summary>
public interface IDegradedInputHandler
{
    IntakeResult TryExtract(RawSubmission raw);
}
