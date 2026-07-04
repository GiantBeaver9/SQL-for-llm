namespace EOQuoter.Domain;

public record BoundPolicy(
    Guid PolicyId,
    string PolicyNumber,
    Guid QuoteId,
    DateOnly EffectiveDate,
    DateOnly ExpiryDate,
    decimal Premium,
    decimal CommissionRate,
    string ExternalReferenceId);

/// <summary>One bordereau line. Double-entry discipline: Gross = Commission + NetToCarrier, always.</summary>
public record BordereauEntry(
    string PolicyNumber,
    string ExternalReferenceId,
    decimal GrossPremium,
    decimal Commission,
    decimal NetToCarrier);
