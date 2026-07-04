using EOQuoter.Domain;

namespace EOQuoter.Api;

// Wire contracts. The rating ledger goes out verbatim — the audit record IS the response detail.

public record SubmissionOutcomeResponse(
    Guid SubmissionId,
    string Status,           // Quoted | Referred | Declined
    string? Reason,
    string? Detail,
    QuoteResponse? Quote);

public record QuoteResponse(
    Guid QuoteId,
    decimal Premium,
    decimal Limit,
    decimal Retention,
    IReadOnlyList<string> Subjectivities,
    DateOnly QuoteDate,
    DateOnly ExpiresOn,
    decimal BasePremium,
    DateOnly RateEffectiveDate,
    IReadOnlyList<RatingStep> Ledger);

public record BindRequest(DateOnly? EffectiveDate, bool SubjectivitiesCleared);

public record BindResponse(
    Guid PolicyId,
    string PolicyNumber,
    Guid QuoteId,
    DateOnly EffectiveDate,
    DateOnly ExpiryDate,
    decimal Premium,
    decimal CommissionRate,
    string ExternalReferenceId,
    string Status);

public record IssueResponse(Guid PolicyId, string PolicyNumber, string Status, DateTimeOffset? IssuedAt, IReadOnlyList<string> Forms);

public record EnqueueJobRequest(string Period); // "yyyy-MM"

public record JobResponse(Guid JobId, string Type, string Status, DateOnly Period, int AttemptCount, string? Checkpoint, string? LastError);

public record PolicyBoundEvent(Guid PolicyId);
