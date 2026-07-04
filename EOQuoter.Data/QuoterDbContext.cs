using EOQuoter.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EOQuoter.Data;

public class QuoterDbContext(DbContextOptions<QuoterDbContext> options) : DbContext(options)
{
    // Rating reference data — all effective-dated
    public DbSet<RateRow> Rates => Set<RateRow>();
    public DbSet<LimitFactorRow> LimitFactors => Set<LimitFactorRow>();
    public DbSet<RetentionFactorRow> RetentionFactors => Set<RetentionFactorRow>();
    public DbSet<AppetiteRow> Appetite => Set<AppetiteRow>();
    public DbSet<AuthorityRow> DelegatedAuthority => Set<AuthorityRow>();
    public DbSet<UnderwritingParameterRow> UnderwritingParameters => Set<UnderwritingParameterRow>();
    public DbSet<SeverityBandRow> SeverityBands => Set<SeverityBandRow>();
    public DbSet<RecencyBandRow> RecencyBands => Set<RecencyBandRow>();

    // Pipeline
    public DbSet<SubmissionRow> Submissions => Set<SubmissionRow>();
    public DbSet<QuoteRow> Quotes => Set<QuoteRow>();
    public DbSet<PolicyRow> Policies => Set<PolicyRow>();
    public DbSet<AuditEventRow> AuditEvents => Set<AuditEventRow>();

    // Idempotency + outbox
    public DbSet<IdempotencyRow> IdempotencyRecords => Set<IdempotencyRow>();
    public DbSet<OutboxRow> Outbox => Set<OutboxRow>();
    public DbSet<ProcessedMessageRow> ProcessedMessages => Set<ProcessedMessageRow>();
    public DbSet<DeadLetterRow> DeadLetters => Set<DeadLetterRow>();

    // Batch / bordereau
    public DbSet<BatchJobRow> BatchJobs => Set<BatchJobRow>();
    public DbSet<BordereauEntryRow> BordereauEntries => Set<BordereauEntryRow>();
    public DbSet<CarrierStatementRow> CarrierStatements => Set<CarrierStatementRow>();
    public DbSet<ReconciliationResultRow> ReconciliationResults => Set<ReconciliationResultRow>();
    public DbSet<RejectedItemRow> RejectedItems => Set<RejectedItemRow>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // The rate table exactly as in the design doc: composite PK carries the temporal model.
        b.Entity<RateRow>(e =>
        {
            e.ToTable("rate_table");
            e.HasKey(x => new { x.ProfessionClass, x.RevenueBand, x.EffectiveDate });
            e.Property(x => x.BaseRate).HasPrecision(18, 4);
        });
        b.Entity<LimitFactorRow>(e =>
        {
            e.HasKey(x => new { x.LimitAmount, x.EffectiveDate });
            e.Property(x => x.LimitAmount).HasPrecision(18, 2);
            e.Property(x => x.Factor).HasPrecision(18, 4);
        });
        b.Entity<RetentionFactorRow>(e =>
        {
            e.HasKey(x => new { x.RetentionAmount, x.EffectiveDate });
            e.Property(x => x.RetentionAmount).HasPrecision(18, 2);
            e.Property(x => x.Factor).HasPrecision(18, 4);
        });
        b.Entity<AppetiteRow>(e => e.HasKey(x => new { x.ProfessionClass, x.EffectiveDate }));
        b.Entity<AuthorityRow>(e =>
        {
            e.HasKey(x => new { x.State, x.EffectiveDate });
            e.Property(x => x.State).HasMaxLength(2);
            e.Property(x => x.MaxLimit).HasPrecision(18, 2);
        });
        b.Entity<UnderwritingParameterRow>(e => e.HasKey(x => x.EffectiveDate));
        b.Entity<SeverityBandRow>(e =>
        {
            e.HasKey(x => new { x.EffectiveDate, x.Label });
            e.Property(x => x.Label).HasMaxLength(50);
            e.Property(x => x.FloorInclusive).HasPrecision(18, 2);
            e.Property(x => x.CeilingExclusive).HasPrecision(18, 2);
            e.Property(x => x.Load).HasPrecision(18, 4);
        });
        b.Entity<RecencyBandRow>(e =>
        {
            e.HasKey(x => new { x.EffectiveDate, x.Label });
            e.Property(x => x.Label).HasMaxLength(50);
            e.Property(x => x.Weight).HasPrecision(18, 4);
        });

        b.Entity<SubmissionRow>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasMaxLength(30);
            e.Property(x => x.ReferralReason).HasMaxLength(50);
            e.Property(x => x.ContentType).HasMaxLength(100);
        });
        b.Entity<QuoteRow>(e =>
        {
            e.HasKey(x => x.QuoteId);
            e.Property(x => x.Status).HasMaxLength(30);
            e.Property(x => x.Premium).HasPrecision(18, 2);
            e.Property(x => x.Limit).HasPrecision(18, 2);
            e.Property(x => x.Retention).HasPrecision(18, 2);
            e.Property(x => x.BasePremium).HasPrecision(18, 2);
            e.HasIndex(x => x.SubmissionId);
        });
        b.Entity<PolicyRow>(e =>
        {
            e.HasKey(x => x.PolicyId);
            e.Property(x => x.PolicyNumber).HasMaxLength(30);
            e.Property(x => x.Status).HasMaxLength(30);
            e.Property(x => x.ExternalReferenceId).HasMaxLength(60);
            e.Property(x => x.Premium).HasPrecision(18, 2);
            e.Property(x => x.CommissionRate).HasPrecision(9, 4);
            e.HasIndex(x => x.PolicyNumber).IsUnique();
            e.HasIndex(x => x.ExternalReferenceId).IsUnique();
            // The DB-level backstop for bind idempotency: one policy per quote, no matter what.
            e.HasIndex(x => x.QuoteId).IsUnique();
        });
        b.Entity<AuditEventRow>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.EntityType).HasMaxLength(30);
            e.Property(x => x.EventType).HasMaxLength(60);
            e.Property(x => x.ExternalReferenceId).HasMaxLength(60);
            e.HasIndex(x => x.EntityId);
            e.HasIndex(x => x.ExternalReferenceId);
        });

        b.Entity<IdempotencyRow>(e =>
        {
            // PK on the key IS the claim: the first INSERT wins, the duplicate hits the constraint.
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(200);
            e.Property(x => x.RequestFingerprint).HasMaxLength(64);
        });
        b.Entity<OutboxRow>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasMaxLength(100);
            e.Property(x => x.DedupeKey).HasMaxLength(200);
            e.HasIndex(x => x.DispatchedAt);
            e.HasIndex(x => x.Seq);
        });
        b.Entity<ProcessedMessageRow>(e =>
        {
            e.HasKey(x => x.DedupeKey);
            e.Property(x => x.DedupeKey).HasMaxLength(200);
        });
        b.Entity<DeadLetterRow>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasMaxLength(100);
            e.Property(x => x.DedupeKey).HasMaxLength(200);
        });

        b.Entity<BatchJobRow>(e =>
        {
            e.HasKey(x => x.JobId);
            e.HasIndex(x => x.Status);
        });
        b.Entity<BordereauEntryRow>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.PolicyNumber).HasMaxLength(30);
            e.Property(x => x.ExternalReferenceId).HasMaxLength(60);
            e.Property(x => x.GrossPremium).HasPrecision(18, 2);
            e.Property(x => x.Commission).HasPrecision(18, 2);
            e.Property(x => x.NetToCarrier).HasPrecision(18, 2);
            // Reruns must not double-report a policy for a period.
            e.HasIndex(x => new { x.Period, x.ExternalReferenceId }).IsUnique();
        });
        b.Entity<CarrierStatementRow>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ExternalReferenceId).HasMaxLength(60);
            e.Property(x => x.PolicyNumber).HasMaxLength(30);
            e.Property(x => x.GrossPremium).HasPrecision(18, 2);
            e.HasIndex(x => new { x.Period, x.ExternalReferenceId }).IsUnique();
        });
        b.Entity<ReconciliationResultRow>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Category).HasMaxLength(30);
            e.Property(x => x.ExternalReferenceId).HasMaxLength(60);
            e.Property(x => x.MgaGross).HasPrecision(18, 2);
            e.Property(x => x.CarrierGross).HasPrecision(18, 2);
            e.HasIndex(x => x.BatchId);
        });
        b.Entity<RejectedItemRow>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ExternalReferenceId).HasMaxLength(60);
            e.Property(x => x.Reason).HasMaxLength(100);
            e.HasIndex(x => x.RejectsRef);
        });

        SnakeCase.Apply(b);
    }
}
