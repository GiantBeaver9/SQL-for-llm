# EOQuoter — a mock E&O quote-bind-bordereau system

A miniature MGA (managing general agent) back office for professional liability (E&O) insurance:
**submission → intake → eligibility → rating → quote → bind → issue → bordereau reconciliation**,
end to end, on C#/.NET 8 + SQL Server via EF Core.

**The claim this project makes:** MGA operations understood end to end, with auditable,
deterministic underwriting logic backed by a correctly-modeled rating database.

**The claim it does NOT make:** ML/risk-modeling capability. **No LLM appears anywhere in this
pipeline — that is a design decision, not a gap.** A carrier delegating binding authority needs
every number reproducible; see [docs/DECISIONS.md](docs/DECISIONS.md), which is half the point of
the repo.

## Layout

| Project | What it is |
|---|---|
| `EOQuoter.Domain` | Types + contracts. Zero dependencies. |
| `EOQuoter.Data` | EF Core/SQL Server: effective-dated rate tables, policy store, idempotency, outbox, jobs, bordereau + the T-SQL sprocs. Two DbContexts — the second is a **separate execution-tracking DB**. |
| `EOQuoter.Intake` | Deterministic ACORD-style XML parser + the `IDegradedInputHandler` extension seam (zero implementations, by design). |
| `EOQuoter.Underwriting` | Eligibility knockouts (fixed order, first wins) + the rating engine (ordered ledger; Σ deltas == final − base by construction). |
| `EOQuoter.Api` | Minimal API host orchestrating the pipeline. Claim-first idempotency; bind writes policy + outbox row in one transaction. |
| `EOQuoter.Jobs` | Durable batch worker: bordereau generation + reconciliation via sprocs, retry/backoff, checkpoint resume, execution tracking written *around* sproc calls. |
| `EOQuoter.Web` | Internal viewer (Blazor Server, basic-auth-gated): decision-audit view and ops view, deliberately separated. |
| `EOQuoter.Tests` | Rating determinism, effective-dating boundaries, ledger tie-out, idempotency races, outbox dedupe/DLQ, sproc integration tests. |

## The one query that carries the temporal model

```sql
SELECT TOP 1 base_rate
FROM rate_table
WHERE profession_class = @class
  AND revenue_band     = @band
  AND effective_date  <= @quoteDate      -- latest rate that already existed at quote time
ORDER BY effective_date DESC;
```

Rates change over time; a quote must price against the version **in force at quote time**.
"Most recent posted" alone silently reprices a February submission at a June rate. The same
as-of pattern versions the factor curves, appetite, delegated authority, claim severity/recency
curves, and even the underwriting rule parameters.

## Running it

Prereqs: .NET 8 SDK, Docker.

```bash
docker compose up -d                      # SQL Server 2022 on localhost:1433
dotnet run --project EOQuoter.Api        # http://localhost:5080 — migrates, installs sprocs, seeds
dotnet run --project EOQuoter.Jobs       # batch worker
dotnet run --project EOQuoter.Web        # http://localhost:5090 — viewer (ops / eoquoter-dev)
```

### Walkthrough

**1. Submit** (structured ACORD XML; the `Idempotency-Key` is required — intake has no natural key yet):

```bash
curl -s http://localhost:5080/submissions \
  -H "Idempotency-Key: demo-001" -H "Content-Type: application/xml" \
  --data-binary @docs/sample-submission.xml
```

Returns a quote with the full **rating ledger** — each step's factor, its actual dollar delta,
and the per-claim severity × recency breakdown under the Claims step. Replay the same command:
you get the stored response back, not a second quote. Change the body but keep the key: **409** —
a key names one intent.

**2. See effective-dating work** — pin the quote date behind the current rate version:

```bash
curl -s http://localhost:5080/submissions \
  -H "Idempotency-Key: demo-002" -H "Content-Type: application/xml" \
  -H "X-Quote-Date: 2026-02-14" --data-binary @docs/sample-submission.xml
```

The ledger's Base step names the rate version that priced it (`effective 2025-01-01`), not the
2026-04-01 version that exists in the table but wasn't in force on Valentine's Day.

**3. Bind** — the critical idempotent command (keys domain-natively on the quote id):

```bash
curl -s http://localhost:5080/quotes/<quoteId>/bind \
  -H "Content-Type: application/json" \
  -d '{"effectiveDate":"2026-07-01","subjectivitiesCleared":true}'
```

Run it twice: one policy, replayed response. The policy row and its `PolicyBound` outbox row
commit in one transaction; the dispatcher then issues the policy (consumer dedupe makes
redelivery harmless). A unique index on `policies.quote_id` backstops it all at the DB level.

**4. Bordereau + reconciliation:**

```bash
curl -s -X POST http://localhost:5080/dev/carrier-statement/2026-07     # simulated carrier file (with planted discrepancies)
curl -s http://localhost:5080/jobs/bordereau      -H "Content-Type: application/json" -d '{"period":"2026-07"}'
curl -s http://localhost:5080/jobs/reconciliation -H "Content-Type: application/json" -d '{"period":"2026-07"}'
curl -s http://localhost:5080/bordereau/2026-07        # entries + gross/commission/net totals (tie out to the cent)
curl -s http://localhost:5080/reconciliation/2026-07   # Matched / PremiumMismatch / MissingAtCarrier / MissingAtMga
```

The carrier file is seeded with deliberate discrepancies so the reconciliation has something real
to catch. Reconciliation joins on `external_reference_id` — the key shared with the carrier —
never on policy number.

**5. Look at the trail** — open the viewer at `http://localhost:5090` (basic auth `ops` /
`eoquoter-dev`), search the policy number in **Audit**: the rating ledger with per-factor dollars,
eligibility outcome, bind/issue events, reconciliation rows. **Ops** shows job runs, the sproc
execution log (from the separate tracking DB — it survives work-transaction rollbacks), rejects,
and dead letters.

## Tests

```bash
dotnet test                                       # pure + SQLite-backed tests
EOQUOTER_TEST_SQLSERVER="Server=localhost,1433;User Id=sa;Password=EOQuoter!Dev2026;TrustServerCertificate=True" \
  dotnet test                                     # + sproc integration tests (scratch DB per fixture)
```

## Reading order for reviewers

1. [docs/DECISIONS.md](docs/DECISIONS.md) — what was considered, what was cut, why.
2. `EOQuoter.Data/Sql/*.sql` — set-based batch with per-item rejects, no cursors, no in-sproc logging (and why that's deliberate).
3. `EOQuoter.Underwriting/RatingEngine.cs` — the ledger accumulator.
4. `EOQuoter.Data/Idempotency/IdempotencyService.cs` + `EOQuoter.Api/Endpoints/PolicyEndpoints.cs` — the two-layer idempotency story.
5. [docs/scaffolding.md](docs/scaffolding.md) — the original design brief this was built from.
