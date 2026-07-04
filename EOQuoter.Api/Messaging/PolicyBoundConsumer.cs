using System.Text.Json;
using EOQuoter.Data;
using EOQuoter.Data.Messaging;
using Microsoft.EntityFrameworkCore;

namespace EOQuoter.Api.Messaging;

/// <summary>
/// Consumes the PolicyBound outbox event and performs issuance: generate the policy record's
/// forms and mark it Issued. Runs on the processor's DbContext and does NOT save — the effect,
/// the dedupe mark, and the dispatched mark commit together, so a redelivery can't double-issue.
/// </summary>
public class PolicyBoundConsumer : IOutboxConsumer
{
    public const string MessageType = "PolicyBound";
    public string Type => MessageType;

    private static readonly string[] Forms = ["EO-DEC-2026 (declarations)", "EO-FORM-100 (policy wording)", "EO-END-STATE (state amendatory)"];

    public async Task HandleAsync(QuoterDbContext db, string payload, CancellationToken ct)
    {
        var evt = JsonSerializer.Deserialize<PolicyBoundEvent>(payload)
            ?? throw new InvalidOperationException("Empty PolicyBound payload");

        var policy = await db.Policies.SingleOrDefaultAsync(p => p.PolicyId == evt.PolicyId, ct)
            ?? throw new InvalidOperationException($"PolicyBound event for unknown policy {evt.PolicyId}");

        if (policy.Status == "Issued") return; // consumer-level idempotence on top of dedupe

        policy.FormsJson = JsonSerializer.Serialize(Forms);
        policy.IssuedAt = DateTimeOffset.UtcNow;
        policy.Status = "Issued";
        Audit.Record(db, "Policy", policy.PolicyId, "PolicyIssued",
            new { policy.PolicyNumber, Forms }, policy.ExternalReferenceId);
    }
}
