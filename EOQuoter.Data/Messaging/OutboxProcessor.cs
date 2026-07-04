using EOQuoter.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EOQuoter.Data.Messaging;

/// <summary>A consumer of one outbox message type. Handlers receive the SAME DbContext the
/// processor uses, so the handler's effect, the dedupe mark, and the dispatched mark commit
/// together — a redelivered message either fully applied once or not at all.</summary>
public interface IOutboxConsumer
{
    string Type { get; }
    Task HandleAsync(QuoterDbContext db, string payload, CancellationToken ct);
}

/// <summary>
/// Layer 2 of the idempotency story. The outbox row was written in the same transaction as the
/// domain change (no dual-write gap); this processor delivers at-least-once, and the
/// processed-message table makes the EFFECT exactly-once. Messages that keep failing go to the
/// dead-letter table for inspection instead of blocking the queue or retrying forever.
/// </summary>
public class OutboxProcessor(QuoterDbContext db, IEnumerable<IOutboxConsumer> consumers, ILogger<OutboxProcessor> logger)
{
    public const int MaxAttempts = 3;

    /// <summary>Returns the number of messages handled (dispatched, deduped, or dead-lettered).</summary>
    public async Task<int> DispatchPendingAsync(CancellationToken ct = default)
    {
        var pending = await db.Outbox
            .Where(m => m.DispatchedAt == null)
            .OrderBy(m => m.Seq)
            .Take(50)
            .ToListAsync(ct);

        var handled = 0;
        foreach (var message in pending)
        {
            var alreadyProcessed = await db.ProcessedMessages.AnyAsync(p => p.DedupeKey == message.DedupeKey, ct);
            if (alreadyProcessed)
            {
                // Redelivery of an applied message: skip the effect, just mark it sent.
                logger.LogInformation("Outbox message {Id} deduped on key {DedupeKey}", message.Id, message.DedupeKey);
                message.DispatchedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
                handled++;
                continue;
            }

            var consumer = consumers.FirstOrDefault(c => c.Type == message.Type);
            try
            {
                if (consumer is null)
                    throw new InvalidOperationException($"No consumer registered for message type '{message.Type}'");

                await consumer.HandleAsync(db, message.Payload, ct);
                db.ProcessedMessages.Add(new ProcessedMessageRow { DedupeKey = message.DedupeKey, ProcessedAt = DateTimeOffset.UtcNow });
                message.DispatchedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
                handled++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                db.ChangeTracker.Clear();
                var failed = await db.Outbox.SingleAsync(m => m.Id == message.Id, ct);
                failed.AttemptCount++;
                if (failed.AttemptCount >= MaxAttempts)
                {
                    db.DeadLetters.Add(new DeadLetterRow
                    {
                        Id = Guid.NewGuid(),
                        OutboxId = failed.Id,
                        Type = failed.Type,
                        Payload = failed.Payload,
                        DedupeKey = failed.DedupeKey,
                        Error = ex.Message,
                        AttemptCount = failed.AttemptCount,
                        DeadLetteredAt = DateTimeOffset.UtcNow,
                    });
                    failed.DispatchedAt = DateTimeOffset.UtcNow; // off the live queue; the DLQ row is the record
                    logger.LogError(ex, "Outbox message {Id} dead-lettered after {Attempts} attempts", failed.Id, failed.AttemptCount);
                    handled++;
                }
                else
                {
                    logger.LogWarning(ex, "Outbox message {Id} failed (attempt {Attempts}); will retry", failed.Id, failed.AttemptCount);
                }
                await db.SaveChangesAsync(ct);
            }
        }
        return handled;
    }
}
