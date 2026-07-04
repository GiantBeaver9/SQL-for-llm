using EOQuoter.Data;
using EOQuoter.Data.Entities;
using EOQuoter.Data.Messaging;
using Microsoft.EntityFrameworkCore;

namespace EOQuoter.Tests;

public class OutboxTests : IDisposable
{
    private readonly SqliteFixture fixture = new();
    public void Dispose() => fixture.Dispose();

    private class CountingConsumer : IOutboxConsumer
    {
        public int Invocations;
        public bool Fail;
        public string Type => "TestEvent";

        public Task HandleAsync(QuoterDbContext db, string payload, CancellationToken ct)
        {
            Invocations++;
            return Fail ? throw new InvalidOperationException("handler blew up") : Task.CompletedTask;
        }
    }

    private static OutboxRow Message(string dedupeKey) => new()
    {
        Id = Guid.NewGuid(),
        Seq = OutboxRow.NewSeq(),
        Type = "TestEvent",
        Payload = "{}",
        DedupeKey = dedupeKey,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task Message_is_dispatched_and_marked()
    {
        using var db = fixture.CreateContext();
        db.Outbox.Add(Message("evt-1"));
        await db.SaveChangesAsync();

        var consumer = new CountingConsumer();
        var handled = await new OutboxProcessor(db, [consumer], SqliteFixture.Logger<OutboxProcessor>()).DispatchPendingAsync();

        Assert.Equal(1, handled);
        Assert.Equal(1, consumer.Invocations);
        Assert.NotNull((await db.Outbox.SingleAsync()).DispatchedAt);
    }

    [Fact]
    public async Task Redelivery_with_same_dedupe_key_applies_the_effect_once()
    {
        // At-least-once delivery: the same logical event lands twice (two outbox rows, one key).
        using var db = fixture.CreateContext();
        db.Outbox.AddRange(Message("evt-dup"), Message("evt-dup"));
        await db.SaveChangesAsync();

        var consumer = new CountingConsumer();
        await new OutboxProcessor(db, [consumer], SqliteFixture.Logger<OutboxProcessor>()).DispatchPendingAsync();

        Assert.Equal(1, consumer.Invocations); // effect exactly once
        Assert.All(await db.Outbox.ToListAsync(), m => Assert.NotNull(m.DispatchedAt)); // both off the queue
    }

    [Fact]
    public async Task Persistently_failing_message_dead_letters_instead_of_blocking()
    {
        using var db = fixture.CreateContext();
        db.Outbox.Add(Message("evt-poison"));
        await db.SaveChangesAsync();

        var consumer = new CountingConsumer { Fail = true };
        var processor = new OutboxProcessor(db, [consumer], SqliteFixture.Logger<OutboxProcessor>());
        for (var i = 0; i < OutboxProcessor.MaxAttempts; i++)
            await processor.DispatchPendingAsync();

        var deadLetter = Assert.Single(await db.DeadLetters.ToListAsync());
        Assert.Equal("evt-poison", deadLetter.DedupeKey);
        Assert.Equal(OutboxProcessor.MaxAttempts, deadLetter.AttemptCount);
        Assert.NotNull((await db.Outbox.SingleAsync()).DispatchedAt); // off the live queue
        Assert.Empty(await db.ProcessedMessages.ToListAsync());       // effect never applied
    }
}
