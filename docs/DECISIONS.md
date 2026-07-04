# Decision log

Judgment lives in the negative space. Each entry: what was considered, what was cut, and why.

## Scope

**1. No LLM in rating or eligibility — anywhere.**
Deterministic + auditable is *required* under delegated binding authority: a carrier letting an MGA
bind on its paper needs every number reproducible, and a regulator pulling one risk needs to see
exactly which rule fired. An LLM here is less defensible, not more capable. The absence is the
design, not a gap. (ML/risk-modeling capability is a separate project's claim.)

**2. E&O over complex property.**
E&O rating is near-formulaic (class × revenue band × limit/retention factors × claims history) and
eligibility is rules-based. Property drags in CAT modeling, valuation, and too many free variables
for a first build to stay honest.

**3. Full end to end, including bordereau.**
Submission → intake → eligibility → rating → quote → bind → issue → bordereau. Bordereau
reconciliation (joined on `external_reference_id`, remitting net of commission) is where ledger
discipline shows — it's the stage that separates "quoting demo" from "MGA operations."

**4. Servicing and claims: known, deferred.**
Endorsements/renewals add surface without new judgment; claims route to carrier/TPA in a real MGA
anyway. Deliberately out, not forgotten.

**5. Degraded-input (scan/handwriting/OCR) handler: priced, not built.**
The seam exists — `IDegradedInputHandler`, tried before human referral, zero implementations
shipped. If volume ever justifies automation it slots in without touching the pipeline. Until
then, `Unparseable → Referred(RequiresManualIntake)`: the human is the catch-all. A real handler
must be able to escalate ("can't read this") rather than confidently emit fields from a broken
submission — which is why the interface returns `IntakeResult`, not `RiskProfile`.

## Data

**6. SQL Server, not Mongo.**
E&O rating is relational to the bone: rate table, factor curves, appetite, delegated authority all
join, and the bordereau is one big reconciliation join. Mongo would show we *can* use it while
showing we'd *misuse* it. Rejected on domain grounds, not familiarity.

**7. Effective-dated versioning on everything that prices or gates a risk.**
Rates, factor curves, appetite, delegated authority, severity/recency curves, even the
underwriting rule parameters. The lookup is always `effective_date <= @quoteDate … ORDER BY
effective_date DESC` — the `<=` bound is the point. "Most recent posted" alone silently reprices a
February submission at a rate posted in June. Rows are never updated in place: leaving appetite is
a new `is_written = 0` row, so "was this in appetite last November?" stays answerable forever.

**8. Sprocs for batch, EF for per-entity work.**
Bordereau generation and reconciliation are set-based aggregation over a whole period — EF's
per-row materialization is the wrong shape. Quote/bind/issue are per-entity with domain logic — a
sproc there would be logic hiding in the database. Scoped deliberately; neither tool leaks into
the other's territory.

**9. Per-item failure without cursors: the rejects table.**
Row-by-row transactions would discard the set-based performance the sprocs exist for. Instead:
validate the set, commit valid rows in one insert, route failures to `rejected_items`. "Roll back
the failed item" becomes "it never commits and is captured"; **resume = reprocess the rejects,
never the period.**

**10. Execution tracking in a separate database, written by the worker.**
SQL Server has no autonomous transactions — a log insert inside a failing transaction rolls back
with it, destroying the evidence exactly when you need it. So the worker writes
`sproc_executions` around each call, on its own connection to its own DB. Deliberately the
opposite of the outbox (same-DB/same-tx by design): the outbox must commit *with* the work; the
execution log must survive *without* it.

## Messaging & idempotency

**11. Two-layer idempotency; either layer alone leaves a hole.**
Layer 1 (HTTP edge) makes bind safe to retry from the client. Layer 2 (transactional outbox +
consumer dedupe + DLQ) makes it safe to redeliver from the dispatcher. At-least-once delivery
means "exactly once" is a lie you engineer around: outbox row commits in the SAME transaction as
the policy (no dual-write gap), the consumer dedupes on key, poison messages dead-letter instead
of blocking or retrying forever.

**12. Claim-first idempotency records.**
The key row is INSERTed (`Processing`) *before* any work — the unique constraint is the atomic
claim, so a concurrent duplicate cannot slip through a check-then-record gap. Completion updates
the row; it is never first-seen at the end. The key maps to the resulting entity id; it never
becomes it.

**13. Key sourcing: client token for intake, domain-native for bind.**
Create has no entity yet, so intake requires a client `Idempotency-Key`. Bind keys on
`bind:{quote_id}` — one quote, one bind intent — with the request fingerprint still guarding
against a different body under the same intent (409). The DB backstops it all with a unique index
on `policies.quote_id`.

**14. In-flight duplicates: reject with Retry-After (default), block-and-return (pluggable).**
The backoff hint is load-bearing — a bare reject invites a hot retry loop that costs more than
waiting. Duplicates are logged as a stats/anomaly signal, not silently swallowed.

**15. Scheduler/worker, not a message broker.**
Bordereau is periodic batch at MGA scale. Durable SQL job rows + a `BackgroundService` give
restart-survival, retry with backoff, and checkpoint resume — and the job row is itself an audit
record. RabbitMQ/Service Bus here would be infrastructure for its own sake.

## Rating

**16. The ledger is the rating engine's output, not a report about it.**
Fixed order Base → ×Limit → ×Retention → ×Claims, each step recording the actual dollar delta
given everything before it. Sequential and marginal means Σ deltas == final − base *by
construction* — the policy document can show what each factor cost, and the total reconciles.
Order-dependence is embraced, not hidden: it's disclosed on the ledger itself.

**17. Claims load additively, not multiplicatively.**
Per claim: `SeverityLoad(band) × RecencyWeight(band)`, then `ClaimsFactor = 1 + Σ loads`.
Multiplying per-claim factors explodes premium unrealistically (two moderate claims shouldn't 2×
the rate). Additive loading is the standard actuarial pattern, and it naturally yields the
required ordering: recent + old loads more than old + old at equal severity. The eligibility
knockout (3+ claims in 5 years → decline) bounds what rating ever sees.

**18. Off-menu inputs are referrals, not guesses.**
A limit or retention with no factor row in force is a `RatingException` routed to manual handling.
The engine never interpolates a price it can't source from a versioned table.

## Operations & viewing

**19. Two log streams, separated.**
Decision audit (rating ledgers, eligibility outcomes, bind/issue events, reconciliation results,
job runs — persisted, queryable by quote/policy/external ref) vs ops logs (Serilog: request
traces, worker execution, errors). Different audiences: the first is regulator/carrier-facing,
the second operator-facing. Reconciliation evidence shouldn't be buried in request-trace noise.

**20. Reconcile against a simulated carrier file with seeded discrepancies.**
With no real carrier, a clean tie-out would prove only the join syntax. The seeded file plants a
premium mismatch, a policy missing at the carrier, and a ghost row the MGA never wrote — so the
discrepancy paths (`PremiumMismatch`, `MissingAtCarrier`, `MissingAtMga`) demonstrably work. The
join is on `external_reference_id`, the shared key across systems — never policy number, which is
ours alone and breaks the join the day it's renumbered.

**21. Internal viewer: Blazor Server behind basic auth, deliberately thin.**
Two pages (Audit, Ops) that surface the two streams. An identity provider for an
internally-deployed read-only viewer would be scope creep; the point is the ledgers and logs
behind the gate, not the gate.

## Build substitutions (this environment)

**22. Tests split by engine.**
Rating/eligibility tests are pure (fakes, no DB). EF-level behavior (temporal lookups,
idempotency claims, outbox dedupe) runs on SQLite in-memory. Sproc behavior can't be faked —
those tests run against real SQL Server, gated on `EOQUOTER_TEST_SQLSERVER`, with a scratch
database per fixture.
