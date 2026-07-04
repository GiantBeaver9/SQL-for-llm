using System.Text.Json;
using EOQuoter.Data.Idempotency;

namespace EOQuoter.Api;

/// <summary>
/// Wraps a committing command in the claim-first idempotency lifecycle: claim → work →
/// Complete(snapshot) | Fail. Replays, in-flight rejects, and fingerprint mismatches short-circuit
/// before any work runs. Only the committing commands go through this — reads don't carry keys.
/// </summary>
public class IdempotentHandler(IdempotencyService idempotency)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IResult> RunAsync(
        string key,
        string fingerprint,
        Func<Task<(int StatusCode, object Body, Guid? ResultId)>> work,
        CancellationToken ct = default)
    {
        var claim = await idempotency.TryClaimAsync(key, fingerprint, ct);
        switch (claim)
        {
            case ClaimResult.Replay replay:
                return Results.Content(replay.ResponseSnapshot, "application/json", statusCode: replay.StatusCode);

            case ClaimResult.InFlight inFlight:
                // Reject-with-Retry-After is the default in-flight policy: server-cheap, and the
                // backoff hint prevents the hot retry loop a bare 409 invites.
                return new WithHeaderResult(
                    Results.Json(
                        new { error = "A request with this Idempotency-Key is still being processed." },
                        statusCode: StatusCodes.Status409Conflict),
                    "Retry-After", ((int)inFlight.RetryAfter.TotalSeconds).ToString());

            case ClaimResult.FingerprintMismatch:
                return Results.Json(
                    new { error = "This Idempotency-Key was already used for a different request. A key names one specific intent." },
                    statusCode: StatusCodes.Status409Conflict);
        }

        // Claimed (or retrying a failed attempt): do the work under the claim.
        try
        {
            var (statusCode, body, resultId) = await work();
            var snapshot = JsonSerializer.Serialize(body, Json);
            await idempotency.CompleteAsync(key, statusCode, snapshot, resultId, ct);
            return Results.Content(snapshot, "application/json", statusCode: statusCode);
        }
        catch
        {
            await idempotency.FailAsync(key, CancellationToken.None);
            throw;
        }
    }

    private class WithHeaderResult(IResult inner, string name, string value) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.Headers[name] = value;
            return inner.ExecuteAsync(httpContext);
        }
    }
}
