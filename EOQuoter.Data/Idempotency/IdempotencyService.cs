using System.Security.Cryptography;
using System.Text;
using EOQuoter.Data.Entities;
using EOQuoter.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EOQuoter.Data.Idempotency;

public abstract record ClaimResult
{
    /// <summary>We hold the claim — do the work, then Complete or Fail.</summary>
    public sealed record Claimed : ClaimResult;

    /// <summary>Same key, same fingerprint, already finished — replay the stored response.</summary>
    public sealed record Replay(int StatusCode, string ResponseSnapshot) : ClaimResult;

    /// <summary>Same key, still Processing. Default policy: reject with Retry-After — the backoff
    /// is load-bearing; a bare reject invites a hot retry loop that costs more than waiting.</summary>
    public sealed record InFlight(TimeSpan RetryAfter) : ClaimResult;

    /// <summary>Same key, DIFFERENT request body. A key names one intent — this is a client bug, 409.</summary>
    public sealed record FingerprintMismatch : ClaimResult;

    /// <summary>Prior attempt with this key failed terminally; caller may retry the work under the same claim.</summary>
    public sealed record RetryAfterFailure : ClaimResult;
}

/// <summary>
/// Claim-first idempotency: the key row is INSERTed before any work, so a concurrent duplicate
/// can't slip through the gap between "check" and "record". The primary key on the key column is
/// the atomic claim; the loser of the race sees the existing row and acts on its status.
/// </summary>
public class IdempotencyService(QuoterDbContext db, ILogger<IdempotencyService> logger)
{
    public static string Fingerprint(string requestBody)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(requestBody));
        return Convert.ToHexString(hash);
    }

    public async Task<ClaimResult> TryClaimAsync(string key, string fingerprint, CancellationToken ct = default)
    {
        db.IdempotencyRecords.Add(new IdempotencyRow
        {
            Key = key,
            RequestFingerprint = fingerprint,
            Status = IdempotencyStatus.Processing,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        try
        {
            await db.SaveChangesAsync(ct);
            return new ClaimResult.Claimed();
        }
        catch (DbUpdateException)
        {
            // Lost the race (or a straight duplicate): the row exists. Read it and decide.
            db.ChangeTracker.Clear();
        }

        var existing = await db.IdempotencyRecords.AsNoTracking().SingleAsync(r => r.Key == key, ct);

        if (existing.RequestFingerprint != fingerprint)
        {
            logger.LogWarning("Idempotency key {Key} reused with a different request fingerprint", key);
            return new ClaimResult.FingerprintMismatch();
        }

        switch (existing.Status)
        {
            case IdempotencyStatus.Completed:
                // Duplicate attempts are a stats/anomaly signal, not something to swallow silently.
                logger.LogInformation("Idempotency key {Key} replayed (duplicate of completed request)", key);
                return new ClaimResult.Replay(existing.StatusCode ?? 200, existing.ResponseSnapshot ?? "");
            case IdempotencyStatus.Failed:
                return new ClaimResult.RetryAfterFailure();
            default:
                logger.LogInformation("Idempotency key {Key} received while first attempt still in flight", key);
                return new ClaimResult.InFlight(TimeSpan.FromSeconds(5));
        }
    }

    public async Task CompleteAsync(string key, int statusCode, string responseSnapshot, Guid? resultId, CancellationToken ct = default)
    {
        var row = await db.IdempotencyRecords.SingleAsync(r => r.Key == key, ct);
        row.Status = IdempotencyStatus.Completed;
        row.StatusCode = statusCode;
        row.ResponseSnapshot = responseSnapshot;
        row.ResultId = resultId;
        row.CompletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task FailAsync(string key, CancellationToken ct = default)
    {
        var row = await db.IdempotencyRecords.SingleAsync(r => r.Key == key, ct);
        row.Status = IdempotencyStatus.Failed;
        row.CompletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
