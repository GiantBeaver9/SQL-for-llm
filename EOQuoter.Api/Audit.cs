using System.Text.Json;
using EOQuoter.Data;
using EOQuoter.Data.Entities;

namespace EOQuoter.Api;

/// <summary>Decision-audit writes — the domain trail (who decided what, about which risk),
/// deliberately separate from ops logging. Rows are added to the caller's context so they commit
/// in the same transaction as the decision they record.</summary>
public static class Audit
{
    public static void Record(QuoterDbContext db, string entityType, Guid entityId, string eventType, object detail, string? externalRef = null)
    {
        db.AuditEvents.Add(new AuditEventRow
        {
            OccurredAt = DateTimeOffset.UtcNow,
            EntityType = entityType,
            EntityId = entityId,
            ExternalReferenceId = externalRef,
            EventType = eventType,
            DetailJson = JsonSerializer.Serialize(detail),
        });
    }
}
