namespace EOQuoter.Domain;

public enum FactorKind { Multiplier, AdditiveLoad }

/// <summary>Per-claim decomposition of the Claims step: each claim's severity band, recency band,
/// and the additive load it contributed. Loads SUM (they don't multiply) — see decision log.</summary>
public record ClaimLoad(
    decimal ClaimAmount,
    DateOnly OccurredOn,
    string SeverityBand,
    decimal SeverityLoad,
    string RecencyBand,
    decimal RecencyWeight,
    decimal Load);

/// <summary>
/// One step of the rating ledger. Sequential and marginal: DollarDelta is the ACTUAL dollar
/// effect of this step given everything before it, so Σ DollarDelta == FinalPremium − BasePremium
/// by construction. The ledger IS the audit trail and drives per-factor cost on the policy doc.
/// </summary>
public record RatingStep(
    int Order,
    string Name,
    FactorKind Kind,
    decimal Value,
    decimal PremiumBefore,
    decimal DollarDelta,
    decimal PremiumAfter,
    string Note,
    IReadOnlyList<ClaimLoad>? Breakdown = null);

/// <summary>Fixed order: Base → Limit → Retention → Claims. Marginal (order-dependent) by design.</summary>
public record RatingResult(
    decimal BasePremium,
    DateOnly RateEffectiveDate,
    IReadOnlyList<RatingStep> Ledger,
    decimal FinalPremium);

public record Quote(
    Guid QuoteId,
    decimal Premium,
    decimal Limit,
    decimal Retention,
    IReadOnlyList<string> Subjectivities,
    DateOnly QuoteDate,
    DateOnly ExpiresOn,
    RatingResult Rating);
