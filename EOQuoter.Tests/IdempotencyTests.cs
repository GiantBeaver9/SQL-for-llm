using EOQuoter.Data.Idempotency;

namespace EOQuoter.Tests;

public class IdempotencyTests : IDisposable
{
    private readonly SqliteFixture fixture = new();
    public void Dispose() => fixture.Dispose();

    private IdempotencyService CreateService() =>
        new(fixture.CreateContext(), SqliteFixture.Logger<IdempotencyService>());

    [Fact]
    public async Task First_claim_wins_and_completes_with_a_snapshot()
    {
        var service = CreateService();
        var claim = await service.TryClaimAsync("submission:key-1", "fp-A");
        Assert.IsType<ClaimResult.Claimed>(claim);

        await service.CompleteAsync("submission:key-1", 201, """{"quoteId":"abc"}""", Guid.NewGuid());

        var replay = await CreateService().TryClaimAsync("submission:key-1", "fp-A");
        var replayed = Assert.IsType<ClaimResult.Replay>(replay);
        Assert.Equal(201, replayed.StatusCode);
        Assert.Contains("abc", replayed.ResponseSnapshot);
    }

    [Fact]
    public async Task Same_key_different_fingerprint_is_a_mismatch_not_a_new_request()
    {
        var service = CreateService();
        await service.TryClaimAsync("submission:key-2", "fp-A");

        var second = await CreateService().TryClaimAsync("submission:key-2", "fp-B");

        Assert.IsType<ClaimResult.FingerprintMismatch>(second);
    }

    [Fact]
    public async Task Duplicate_while_processing_is_rejected_with_retry_after()
    {
        // Claim-first is the point: the row exists BEFORE any work, so a concurrent duplicate
        // hits the constraint even though the first request hasn't completed.
        await CreateService().TryClaimAsync("bind:quote-9", "fp-A");

        var concurrent = await CreateService().TryClaimAsync("bind:quote-9", "fp-A");

        var inFlight = Assert.IsType<ClaimResult.InFlight>(concurrent);
        Assert.True(inFlight.RetryAfter > TimeSpan.Zero, "the backoff hint is load-bearing");
    }

    [Fact]
    public async Task Failed_attempt_can_be_retried_under_the_same_key()
    {
        var service = CreateService();
        await service.TryClaimAsync("submission:key-3", "fp-A");
        await service.FailAsync("submission:key-3");

        var retry = await CreateService().TryClaimAsync("submission:key-3", "fp-A");

        Assert.IsType<ClaimResult.RetryAfterFailure>(retry);
    }

    [Fact]
    public void Fingerprint_is_stable_and_body_sensitive()
    {
        Assert.Equal(IdempotencyService.Fingerprint("body"), IdempotencyService.Fingerprint("body"));
        Assert.NotEqual(IdempotencyService.Fingerprint("body"), IdempotencyService.Fingerprint("body2"));
    }
}
