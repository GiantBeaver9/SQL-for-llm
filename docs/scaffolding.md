# Mock E&O Quoter — Scaffolding Doc (original design brief)

**Stack:** C#/.NET (minimal API host), SQL Server via EF Core
**Audience:** insurtech/MGA reviewers — domain realism is the priority
**Claim this project makes:** "I understand MGA operations end to end and ship auditable, deterministic underwriting logic backed by a correctly-modeled rating DB."
**Claim this project does NOT make:** ML/risk-modeling capability. That lives in a separate project. No LLM appears here — that is the finding, not a gap.

---

## Scope boundary (read first)

**In scope — full end to end:** submission → intake → eligibility → rating → quote → bind → issue → bordereau.

**Still deferred, deliberately:** servicing (endorsements, renewals) and claims. Claims route to carrier/TPA in a real MGA anyway; servicing adds surface without new judgment. Note as "known, deferred" in the decision log.

**Also deferred, deliberately:** the degraded-input (scan/handwriting/OCR) handler. Priced during design, chosen not to build. A seam is left so it slots in later if volume ever justifies it.

---

## Solution layout

```
EOQuoter.sln
├── EOQuoter.Domain         // types + contracts, zero dependencies
├── EOQuoter.Data           // EF Core / SQL Server: rate tables, policy store, bordereau, audit + job stores
├── EOQuoter.Intake         // structured ACORD parser + extension seam
├── EOQuoter.Underwriting   // eligibility rules + deterministic rating engine (emits rating ledger)
├── EOQuoter.Jobs           // BackgroundService worker: durable bordereau/batch queue, retry + resume
├── EOQuoter.Api            // minimal API host, orchestrates the pipeline
├── EOQuoter.Web            // internal-only viewer (Blazor Server): audit trail + ops logs, separated
└── EOQuoter.Tests          // unit tests (rating determinism, effective-dating, ledger tie-out, reconciliation)
```

---

## Pipeline (EOQuoter.Api orchestration)

```
POST /submissions                          [Idempotency-Key header]
  → Intake.Parse(rawSubmission)
      → Parsed(RiskProfile)   → continue
      → Unparseable(reason)   → Referral(RequiresManualIntake)      // human is the catch-all
  → Underwriting.Evaluate(RiskProfile, quoteDate)
      → eligibility knockouts / appetite / delegated-authority
          → Ineligible        → Referral | Declined
          → Eligible          → Rate(effective-dated) → Quote        // SYNC: premium returns inline
                                                                       // key replay on retry (no re-quote)
POST /quotes/{id}/bind                      [Idempotency-Key header — the critical one]
  → dedupe on key → clear subjectivities → BoundPolicy
  → write policy + outbox row in ONE tx → dispatcher publishes → mark Issued
POST /policies/{id}/issue
  → generate policy record + forms → mark Issued
Bordereau (durable job)
  → worker consumes → dedupe → collect period → reconcile → remit net of commission
```

---

## The rating DB — where "I can structure a DB" actually gets proven (EOQuoter.Data)

A flat rate table is just JSON with a schema. The domain-realism signal is **effective-dated rate versioning**: rates change over time, and a quote must price against the rate version *in force at quote time*.

```sql
CREATE TABLE rate_table (
    profession_class  INT          NOT NULL,
    revenue_band      INT          NOT NULL,
    base_rate         DECIMAL(18,4) NOT NULL,
    effective_date    DATE         NOT NULL,
    CONSTRAINT pk_rate PRIMARY KEY (profession_class, revenue_band, effective_date)
);
```

Rate lookup — the guard that carries the whole temporal model:

```sql
SELECT TOP 1 base_rate
FROM rate_table
WHERE profession_class = @class
  AND revenue_band     = @band
  AND effective_date  <= @quoteDate      -- latest rate that already existed at quote time
ORDER BY effective_date DESC;
```

The `<= @quoteDate` bound is the point. "Most recent posted" alone reprices a 2-14 submission at a 6-24 rate that didn't exist yet — a silent correctness bug and exactly the kind of thing a domain reviewer probes. Same effective-dating pattern applies to factor curves, appetite rules, and delegated-authority limits: all versioned, all queried as-of.

**Why SQL Server, not Mongo:** E&O rating is relational to the bone — rate tables, factor curves, appetite, delegated-authority-by-state all join. Mongo would show you *can* use it while showing you'd *misuse* it. Relational is the correct model; that judgment goes in the log.

---

## Data contracts (EOQuoter.Domain)

Sketch — types, not final signatures.

```csharp
public record RiskProfile(
    ProfessionClass Class, RevenueBand Revenue,
    decimal RequestedLimit, decimal RequestedRetention,
    int PriorClaimsCount, int YearsInBusiness, string State);

public abstract record IntakeResult;
public record Parsed(RiskProfile Profile) : IntakeResult;
public record Unparseable(string Reason)  : IntakeResult;

public abstract record EligibilityOutcome;
public record Eligible                        : EligibilityOutcome;
public record Referred(ReferralReason Reason) : EligibilityOutcome;
public record Declined(ReferralReason Reason) : EligibilityOutcome;

public enum ReferralReason {
    RequiresManualIntake, OutOfAppetite, PriorClaimsThreshold,
    OutsideDelegatedAuthority, LimitExceedsAuthority }

// Rating is computed as an ordered ledger: each step records the ACTUAL dollar
// effect and the running total, so Σ DollarDelta == FinalPremium − BasePremium.
// The ledger IS the audit trail and drives the per-factor cost on the policy doc.
public record RatingResult(
    decimal BasePremium, DateOnly RateEffectiveDate,
    IReadOnlyList<RatingStep> Ledger, decimal FinalPremium);
public enum FactorKind { Multiplier, AdditiveLoad }
public record RatingStep(
    int Order, string Name, FactorKind Kind, decimal Value,
    decimal PremiumBefore, decimal DollarDelta, decimal PremiumAfter, string Note);
// Fixed order: Base → Limit → Retention → Claims. Marginal (order-dependent) by design.
// Claims step carries a sub-breakdown of the per-claim AdditiveLoads that formed it.

public record Quote(
    Guid QuoteId, decimal Premium, decimal Limit, decimal Retention,
    IReadOnlyList<string> Subjectivities, DateOnly QuoteDate,
    DateOnly ExpiresOn, RatingResult Rating);

public record BoundPolicy(
    Guid PolicyId, string PolicyNumber, Guid QuoteId,
    DateOnly EffectiveDate, DateOnly ExpiryDate,
    decimal Premium, decimal CommissionRate, string ExternalReferenceId);

public record BordereauEntry(
    string PolicyNumber, string ExternalReferenceId,
    decimal GrossPremium, decimal Commission, decimal NetToCarrier);

// HTTP-edge idempotency: claim-first row, updated through its lifecycle (see Intake idempotency).
public enum IdempotencyStatus { Processing, Completed, Failed }
public record IdempotencyRecord(
    string Key, string RequestFingerprint, IdempotencyStatus Status,
    string? ResponseSnapshot, int? StatusCode, Guid? ResultId,
    DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);   // same key + different fingerprint => 409

// Outbox: domain change + this row commit in one tx; dispatcher publishes, then marks sent.
public record OutboxMessage(
    Guid Id, string Type, string Payload, string DedupeKey,
    DateTimeOffset CreatedAt, DateTimeOffset? DispatchedAt);

// Separate execution-tracking DB — worker writes AROUND each sproc call, outside the
// work tx, so it survives a per-item rollback. NOT the outbox (that's same-tx).
public enum ExecutionStatus { Succeeded, Failed, PartialWithRejects }
public record SprocExecutionLog(
    Guid RunId, Guid BatchId, string SprocName, string Params,
    DateTimeOffset StartedAt, DateTimeOffset? EndedAt,
    ExecutionStatus Status, long RowsAffected, string? Error, Guid? RejectsRef);

// Captured failures; resume reprocesses these, not the whole period.
public record RejectedItem(
    Guid RejectsRef, Guid BatchId, string ExternalReferenceId,
    string Reason, string RawPayload, DateTimeOffset RejectedAt);
```

---

## Underwriting (EOQuoter.Underwriting)

Deterministic and auditable — **no LLM anywhere.** A carrier delegating binding authority needs every number reproducible.

**Eligibility** — rules in order, first knockout wins: appetite (is `Class` written?), delegated-authority by `State` + `Limit`, years-in-business floor, and the **claims knockout: 3+ claims in the 5-year window → decline** (`PriorClaimsThreshold`). Runs before rating — you don't price a risk you're declining. Because it caps at 3, the rating engine below only ever sees 0, 1, or 2 claims, which naturally bounds the claims load.

**Rating** — pure function of `(RiskProfile, quoteDate)`, computed as an ordered accumulator:
```
Base → ×LimitFactor → ×RetentionFactor → ×ClaimsFactor
```
Each step records `PremiumBefore`, the factor, the **actual DollarDelta it added**, and `PremiumAfter`. Because it's sequential and marginal, the deltas sum exactly back to `FinalPremium − BasePremium` — so the policy document can show what each factor actually cost, and the total reconciles. The Claims step carries its per-claim additive-load sub-breakdown so that piece decomposes too. The ledger doubles as the rating audit record.

### Claims modifier (per-claim scoring, additive load)

Recency-per-claim means each claim is scored individually, then loads are **summed** — not multiplied. Multiplying per-claim factors explodes premium unrealistically (two moderate claims shouldn't 2× the rate); additive loading is the correct actuarial pattern.

```
per-claim load = SeverityLoad(band) × RecencyWeight(band)
ClaimsFactor   = 1.0 + Σ per-claim loads
```

Both tables effective-dated like everything else (queried as-of quote date):

| Severity band (per claim) | SeverityLoad |
|---|---|
| $0–25k    | 0.05 |
| $25–100k  | 0.20 |
| $100k+    | 0.50 |

| Recency band | RecencyWeight |
|---|---|
| < 1 yr   | 1.0 |
| 1–3 yr   | 0.6 |
| 3–5 yr   | 0.3 |

Values are placeholders — tune them, but the structure is the point. This produces the ordering you specified: a recent claim + an older claim loads higher than two older claims of the same severity, because the recent one carries full weight. Each claim's `(band, weight, load)` lands in `RatingResult.Factors`, so the claims load is auditable claim by claim.

---

## Bordereau + reconciliation — where ledger discipline shows (EOQuoter.Data)

This is the stage that earned "full end to end." Not a report dump — a reconciliation.

- Collect bound policies for the period → build `BordereauReport`.
- **Reconcile** the MGA's bound book against the carrier's expected report, **joined on `ExternalReferenceId`** (not policy number — the external ref is the shared key across systems).
- Surface discrepancies: present here but not there, premium mismatch, missing external ref.
- **Remit net of commission:** `NetToCarrier = GrossPremium − (GrossPremium × CommissionRate)`. Double-entry discipline — every gross has a matching commission + net, and the period's remittance ties out to the sum of entries.

Open question for the build: with no real carrier, you reconcile against a **simulated carrier file** (seed one with a deliberate discrepancy or two) so the reconciliation has something to catch. Decide whether you want that, or a clean tie-out only. The discrepancy path is what makes the reconciliation look real.
*(Resolved during the build: seeded discrepancies — see DECISIONS.md #20.)*

### Execution: sprocs, per-item rejects, and a separate tracking DB

Bordereau batch runs as **T-SQL stored procedures**, not EF — set-based aggregation and reconciliation joins over the whole period, where EF's per-row materialization is the wrong shape. Scoped deliberately: sprocs for the batch, EF for per-entity quote/bind.

**Per-item without a cursor.** Per-item rollback + resume conflicts with set-based work if written row-by-row with a transaction each — that discards the performance the sprocs exist for. Instead: validate the set, commit valid rows in one operation, route failures to a **rejects/exception table** rather than failing the batch. "Roll back the failed item" becomes "it never commits and is captured"; **resume = reprocess the rejects**, not the period.

**Execution tracking DB (separate).** A distinct DB records each sproc run: batch id, sproc, params, start/end, status (Succeeded / Failed / PartialWithRejects), rows affected, error, rejects pointer. The **worker writes this around the sproc call** — separate connection, separate DB — so it sits outside the work transaction and survives a rollback. Do *not* log from inside the sproc: SQL Server has no true autonomous transaction, so a log insert in the failing tx rolls back with it. Distinct from the outbox: the outbox is same-DB/same-tx by design; this is deliberately the opposite.

---

## Batch queue (EOQuoter.Jobs)

Bordereau generation and reconciliation run as **durable background jobs**, not inline request handling — they're periodic and can be large. Scope is deliberately a *scheduler/worker*, not a message broker; no RabbitMQ/Service Bus needed for batch at this scale, and pulling one in would be infrastructure for its own sake.

```csharp
public enum JobType   { BordereauGeneration, Reconciliation }
public enum JobStatus { Queued, Running, Completed, Failed, Retrying }

public record BatchJob(
    Guid JobId, JobType Type, JobStatus Status, DateOnly Period,
    int AttemptCount, string? Checkpoint, string? LastError,
    DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt);
```

- **Durable** — jobs are SQL Server rows, so they survive restart and are themselves auditable (who ran what, when, outcome).
- A `BackgroundService` worker claims `Queued`/`Retrying` jobs and processes them.
- **Resume/retry lives here** (not in rating): a failed job retries with backoff; a partially-complete run resumes from `Checkpoint` (e.g. last processed policy) rather than redoing the batch. This is the honest home for "resume."

---

## Idempotency & messaging — two layers (EOQuoter.Api + EOQuoter.Jobs)

Idempotency guards the **committing commands**, not everything. Reads and non-mutating paths don't carry keys — that'd be noise. The failure being prevented is a duplicate bind (double policy, double carrier liability), so **bind is the command that must be idempotent**; quote and bordereau remit are next.

**Layer 1 — HTTP edge.** Client sends `Idempotency-Key` on POST bind (and quote). Server stores `key → response snapshot`; a retry with the same key **replays the stored response** instead of re-executing. Same key + different request fingerprint → reject (409/422): the key names one specific intent. Key is scoped to a single bind intent — reusing it for a different bind is an error, not a new bind.

**Layer 2 — outbox + consumer.** At-least-once delivery means "exactly once" is a lie you engineer around:
- **Outbox (transactional):** the domain write (e.g. `BoundPolicy`) and the `OutboxMessage` commit in the *same* transaction. No dual-write gap — you can't persist the policy but lose the event, or publish an event for a rolled-back policy. A dispatcher reads unsent outbox rows, publishes, marks them dispatched.
- **Consumer dedupe:** the worker tracks processed `DedupeKey`s and skips repeats, so a redelivery applies the effect once.
- **DLQ:** a message that keeps failing goes to a dead-letter queue for inspection rather than blocking the worker or retrying forever.

Net: bind is safe to retry from the client (Layer 1) *and* safe to redeliver from the bus (Layer 2). That two-sided guarantee is the senior signal — either layer alone leaves a hole.

### Intake idempotency (Slice 1 — resolved; bind/bordereau specifics land with their slices)

The guard is a **claim-first write**, not a completion-only record: insert the key row *before* any work, so a concurrent duplicate can't slip through the gap. A unique constraint on the key makes the first insert the atomic claim; the loser sees an existing row.

- **Key source:** create has no entity yet, so intake requires a **client-supplied token** (`Idempotency-Key`). (Bind, by contrast, keys domain-natively on `quote_id` — that lives in the bind slice.)
- **Lifecycle:** `Processing` on claim → `Completed` (with stored response) on success → `Failed` on error. The row is written at the start and *updated*, never first-seen at the end.
- **Key → id mapping, not identity:** the key is the client's token; the resulting submission/`RiskProfile` id is filled into the stored response on completion. The key never *becomes* the entity id.
- **In-flight policy (pluggable):** same key arrives while the first is still `Processing`. Default **reject with `Retry-After`** (server-cheap; the backoff is load-bearing — a bare reject invites a hot retry loop that costs more than waiting). **Block-and-return** is the configurable alternative where client simplicity outweighs held-connection cost.
- **Completed duplicate:** replay the stored response.
- **Duplicate attempts are logged** (stats/anomaly signal), not silently swallowed.

The `IdempotencyRecord` contract (above) carries this lifecycle: `Status` transitions `Processing → Completed/Failed`, `ResultId` and `ResponseSnapshot` fill in on completion.

---

## Logging & audit — two separated streams (EOQuoter.Web)

Per the split: **decision audit** and **system/ops logs** are different concerns and stay apart.

- **Decision audit (domain, persisted, queryable):** rating ledgers, eligibility outcomes, bind/issue events, bordereau reconciliation results, job runs. Keyed by quote/policy/external-ref so a reviewer can pull one risk and see every decision and dollar that touched it. This is the regulator/carrier-facing trail.
- **Ops logs (technical):** structured application logging (e.g. Serilog) — request traces, worker execution, errors. Operator-facing.

The **internal viewer** (`EOQuoter.Web`, Blazor Server, auth-gated, internally deployable — not public) surfaces both as separate views: an Audit view (search by policy/quote, drill into a rating ledger and see each factor's actual dollar cost) and an Ops view (job status, system logs). Keeping them separate matters — you don't want reconciliation evidence buried in request-trace noise, and you don't want ops chasing errors through underwriting decisions.

---

## Intake + the extension seam (EOQuoter.Intake)

Structured ACORD XML/XFA → deterministic parse, the only built path.

```csharp
public interface ISubmissionParser { IntakeResult Parse(RawSubmission raw); }

// Extension point — no implementation ships. If degraded-input volume ever
// justifies automation, a handler registers here and is tried BEFORE human
// referral. A real handler must be able to escalate ("can't read this")
// rather than confidently emit fields from a broken submission.
public interface IDegradedInputHandler { IntakeResult TryExtract(RawSubmission raw); }
```

No handler registered → `Unparseable` → `Referred(RequiresManualIntake)`. Human is the catch-all. Interface with zero implementations, designed so a second could be added — the correct amount of speculative capacity.

---

## Decision log (ship it alongside the code — this is the differentiator)

Judgment lives in the negative space. Each entry: what was considered, what was cut, why.

1. **No LLM in rating/eligibility.** Deterministic + auditable is required under delegated binding authority. An LLM here is less defensible, not more capable. The absence is the design.
2. **E&O over complex property.** Near-formulaic rating, rules-based eligibility. Property has too many free variables for a first build.
3. **SQL Server, not Mongo.** Rating is relational (tables/factors/appetite/authority all join). Mongo would demonstrate misuse. Rejected on domain grounds, not familiarity.
4. **Effective-dated rate versioning.** Quotes price as-of quote date (`effective_date <= quoteDate`), not today. Without it, a rate table is just a lookup — this is what proves DB structuring.
5. **Full end to end including bordereau.** Bordereau reconciliation (join on `ExternalReferenceId`, remit net of commission) is the stage that separates a quoting demo from MGA operations.

*The full, expanded decision log — including decisions resolved during the build — lives in [DECISIONS.md](DECISIONS.md).*
