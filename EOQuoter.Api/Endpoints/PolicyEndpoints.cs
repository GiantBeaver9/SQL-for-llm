using System.Text.Json;
using EOQuoter.Api.Messaging;
using EOQuoter.Data;
using EOQuoter.Data.Entities;
using EOQuoter.Data.Idempotency;
using Microsoft.EntityFrameworkCore;

namespace EOQuoter.Api.Endpoints;

public static class PolicyEndpoints
{
    public static void Map(WebApplication app)
    {
        // POST /quotes/{id}/bind — THE command that must be idempotent: a duplicate bind is a
        // double policy and double carrier liability. Unlike intake (client-supplied key), bind
        // keys domain-natively on the quote id — one quote, one bind intent.
        app.MapPost("/quotes/{id:guid}/bind", async (
            Guid id,
            BindRequest bindRequest,
            IdempotentHandler idempotent,
            QuoterDbContext db,
            IConfiguration config,
            CancellationToken ct) =>
        {
            var fingerprint = IdempotencyService.Fingerprint(JsonSerializer.Serialize(bindRequest));

            return await idempotent.RunAsync($"bind:{id}", fingerprint, async () =>
            {
                var quote = await db.Quotes.SingleOrDefaultAsync(q => q.QuoteId == id, ct);
                if (quote is null)
                    return (StatusCodes.Status404NotFound, (object)new { error = $"Quote {id} not found" }, null);
                if (quote.Status == "Expired" || DateOnly.FromDateTime(DateTime.UtcNow) > quote.ExpiresOn)
                    return (StatusCodes.Status422UnprocessableEntity, (object)new { error = "Quote has expired; resubmit for a fresh quote" }, null);
                if (!bindRequest.SubjectivitiesCleared)
                    return (StatusCodes.Status422UnprocessableEntity, (object)new { error = "Subjectivities must be cleared before binding (subjectivitiesCleared: true)" }, null);

                var effectiveDate = bindRequest.EffectiveDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
                var year = effectiveDate.Year;
                var seq = await db.Policies.CountAsync(ct) + 1;
                var policy = new PolicyRow
                {
                    PolicyId = Guid.NewGuid(),
                    PolicyNumber = $"EO-{year}-{seq:D6}",
                    QuoteId = quote.QuoteId,
                    EffectiveDate = effectiveDate,
                    ExpiryDate = effectiveDate.AddYears(1),
                    Premium = quote.Premium,
                    CommissionRate = config.GetValue("EOQuoter:CommissionRate", 0.15m),
                    ExternalReferenceId = $"EXT-{Guid.NewGuid():N}"[..16].ToUpperInvariant(),
                    Status = "Bound",
                    BoundAt = DateTimeOffset.UtcNow,
                };
                db.Policies.Add(policy);
                quote.Status = "Bound";

                // The outbox row rides the SAME SaveChanges transaction as the policy: no window
                // where the policy exists but the event is lost, or the event fires for a rollback.
                db.Outbox.Add(new OutboxRow
                {
                    Id = Guid.NewGuid(),
                    Seq = OutboxRow.NewSeq(),
                    Type = PolicyBoundConsumer.MessageType,
                    Payload = JsonSerializer.Serialize(new PolicyBoundEvent(policy.PolicyId)),
                    DedupeKey = $"policy-bound:{policy.PolicyId}",
                    CreatedAt = DateTimeOffset.UtcNow,
                });
                Audit.Record(db, "Policy", policy.PolicyId, "PolicyBound",
                    new { policy.PolicyNumber, policy.Premium, policy.EffectiveDate, quote.QuoteId },
                    policy.ExternalReferenceId);

                await db.SaveChangesAsync(ct);

                var response = new BindResponse(policy.PolicyId, policy.PolicyNumber, policy.QuoteId,
                    policy.EffectiveDate, policy.ExpiryDate, policy.Premium, policy.CommissionRate,
                    policy.ExternalReferenceId, policy.Status);
                return (StatusCodes.Status201Created, (object)response, policy.PolicyId);
            }, ct);
        });

        // POST /policies/{id}/issue — explicit issuance. Normally the PolicyBound outbox consumer
        // issues automatically; this is the manual path, idempotent by state check.
        app.MapPost("/policies/{id:guid}/issue", async (Guid id, QuoterDbContext db, CancellationToken ct) =>
        {
            var policy = await db.Policies.SingleOrDefaultAsync(p => p.PolicyId == id, ct);
            if (policy is null) return Results.NotFound(new { error = $"Policy {id} not found" });

            if (policy.Status != "Issued")
            {
                policy.FormsJson = JsonSerializer.Serialize(new[]
                    { "EO-DEC-2026 (declarations)", "EO-FORM-100 (policy wording)", "EO-END-STATE (state amendatory)" });
                policy.IssuedAt = DateTimeOffset.UtcNow;
                policy.Status = "Issued";
                Audit.Record(db, "Policy", policy.PolicyId, "PolicyIssued",
                    new { policy.PolicyNumber, Path = "manual" }, policy.ExternalReferenceId);
                await db.SaveChangesAsync(ct);
            }

            return Results.Ok(new IssueResponse(policy.PolicyId, policy.PolicyNumber, policy.Status,
                policy.IssuedAt, JsonSerializer.Deserialize<string[]>(policy.FormsJson!)!));
        });

        app.MapGet("/policies/{id:guid}", async (Guid id, QuoterDbContext db, CancellationToken ct) =>
        {
            var p = await db.Policies.AsNoTracking().SingleOrDefaultAsync(x => x.PolicyId == id, ct);
            return p is null
                ? Results.NotFound()
                : Results.Ok(new BindResponse(p.PolicyId, p.PolicyNumber, p.QuoteId, p.EffectiveDate,
                    p.ExpiryDate, p.Premium, p.CommissionRate, p.ExternalReferenceId, p.Status));
        });
    }
}
