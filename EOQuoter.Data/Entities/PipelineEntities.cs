namespace EOQuoter.Data.Entities;

public class SubmissionRow
{
    public Guid Id { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public string RawContent { get; set; } = null!;
    public string ContentType { get; set; } = null!;
    /// <summary>Referred | Declined | Quoted</summary>
    public string Status { get; set; } = null!;
    public string? ReferralReason { get; set; }
    public string? RiskProfileJson { get; set; }
    public Guid? QuoteId { get; set; }
}

public class QuoteRow
{
    public Guid QuoteId { get; set; }
    public Guid SubmissionId { get; set; }
    /// <summary>Quoted | Bound | Expired</summary>
    public string Status { get; set; } = null!;
    public decimal Premium { get; set; }
    public decimal Limit { get; set; }
    public decimal Retention { get; set; }
    public string SubjectivitiesJson { get; set; } = null!;
    public DateOnly QuoteDate { get; set; }
    public DateOnly ExpiresOn { get; set; }
    public decimal BasePremium { get; set; }
    /// <summary>Which rate version priced this quote — the effective-dating receipt.</summary>
    public DateOnly RateEffectiveDate { get; set; }
    /// <summary>The full rating ledger, serialized. The audit record of every dollar.</summary>
    public string LedgerJson { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
}

public class PolicyRow
{
    public Guid PolicyId { get; set; }
    public string PolicyNumber { get; set; } = null!;
    /// <summary>Unique — the DB itself enforces one policy per quote, backstopping idempotency.</summary>
    public Guid QuoteId { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public DateOnly ExpiryDate { get; set; }
    public decimal Premium { get; set; }
    public decimal CommissionRate { get; set; }
    /// <summary>The shared key with the carrier's systems — reconciliation joins on THIS, not policy number.</summary>
    public string ExternalReferenceId { get; set; } = null!;
    /// <summary>Bound | Issued</summary>
    public string Status { get; set; } = null!;
    public DateTimeOffset BoundAt { get; set; }
    public DateTimeOffset? IssuedAt { get; set; }
    public string? FormsJson { get; set; }
}

/// <summary>Domain decision trail (bind/issue/eligibility/reconciliation events), keyed so a reviewer
/// can pull one risk and see everything that touched it. Separate from ops logging by design.</summary>
public class AuditEventRow
{
    public long Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string EntityType { get; set; } = null!;
    public Guid EntityId { get; set; }
    public string? ExternalReferenceId { get; set; }
    public string EventType { get; set; } = null!;
    public string DetailJson { get; set; } = null!;
}
