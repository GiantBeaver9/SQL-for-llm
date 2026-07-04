using System.Text.Json;
using EOQuoter.Data;
using EOQuoter.Data.Entities;
using EOQuoter.Data.Idempotency;
using EOQuoter.Domain;
using EOQuoter.Intake;
using EOQuoter.Underwriting;
using Microsoft.EntityFrameworkCore;

namespace EOQuoter.Api.Endpoints;

public static class SubmissionEndpoints
{
    private static readonly string[] StandardSubjectivities =
    [
        "Signed and dated application received within 30 days of binding",
        "No known claims or circumstances between quote date and binding",
    ];

    public static void Map(WebApplication app)
    {
        // POST /submissions — sync quote path: intake → eligibility → rate → quote, premium inline.
        // Idempotency-Key is required: create has no natural domain key yet, so the client names the intent.
        app.MapPost("/submissions", async (
            HttpRequest request,
            IdempotentHandler idempotent,
            QuoterDbContext db,
            IntakePipeline intake,
            EligibilityEngine eligibility,
            RatingEngine rating,
            CancellationToken ct) =>
        {
            if (!request.Headers.TryGetValue("Idempotency-Key", out var key) || string.IsNullOrWhiteSpace(key))
                return Results.BadRequest(new { error = "Idempotency-Key header is required on POST /submissions" });

            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync(ct);
            if (string.IsNullOrWhiteSpace(body))
                return Results.BadRequest(new { error = "Request body (the raw submission) is required" });

            // Demo hook for effective-dating: rating is a pure function of (profile, quoteDate),
            // so letting the caller pin the quote date shows as-of pricing without touching a clock.
            var quoteDate = request.Headers.TryGetValue("X-Quote-Date", out var qd) && DateOnly.TryParse(qd, out var parsedDate)
                ? parsedDate
                : DateOnly.FromDateTime(DateTime.UtcNow);

            var contentType = request.ContentType ?? "application/xml";

            return await idempotent.RunAsync(
                $"submission:{key}",
                IdempotencyService.Fingerprint(body),
                async () =>
                {
                    var submission = new SubmissionRow
                    {
                        Id = Guid.NewGuid(),
                        ReceivedAt = DateTimeOffset.UtcNow,
                        RawContent = body,
                        ContentType = contentType,
                        Status = "Received",
                    };
                    db.Submissions.Add(submission);
                    Audit.Record(db, "Submission", submission.Id, "SubmissionReceived", new { contentType, quoteDate });

                    var intakeResult = intake.Process(new RawSubmission(body, contentType));
                    if (intakeResult is Unparseable unparseable)
                    {
                        // Human is the catch-all: no automated degraded-input path exists (by design).
                        submission.Status = "Referred";
                        submission.ReferralReason = ReferralReason.RequiresManualIntake.ToString();
                        Audit.Record(db, "Submission", submission.Id, "ReferredToManualIntake", new { unparseable.Reason });
                        await db.SaveChangesAsync(ct);
                        return (StatusCodes.Status202Accepted,
                            (object)new SubmissionOutcomeResponse(submission.Id, "Referred",
                                ReferralReason.RequiresManualIntake.ToString(), unparseable.Reason, null),
                            submission.Id);
                    }

                    var profile = ((Parsed)intakeResult).Profile;
                    submission.RiskProfileJson = JsonSerializer.Serialize(profile);

                    var outcome = await eligibility.EvaluateAsync(profile, quoteDate, ct);
                    Audit.Record(db, "Submission", submission.Id, "EligibilityEvaluated",
                        new { Outcome = outcome.GetType().Name, Reason = (outcome as Referred)?.Reason ?? (outcome as Declined)?.Reason, quoteDate });

                    switch (outcome)
                    {
                        case Declined declined:
                            submission.Status = "Declined";
                            submission.ReferralReason = declined.Reason.ToString();
                            await db.SaveChangesAsync(ct);
                            return (StatusCodes.Status200OK,
                                (object)new SubmissionOutcomeResponse(submission.Id, "Declined", declined.Reason.ToString(), null, null),
                                submission.Id);
                        case Referred referred:
                            submission.Status = "Referred";
                            submission.ReferralReason = referred.Reason.ToString();
                            await db.SaveChangesAsync(ct);
                            return (StatusCodes.Status200OK,
                                (object)new SubmissionOutcomeResponse(submission.Id, "Referred", referred.Reason.ToString(), null, null),
                                submission.Id);
                    }

                    RatingResult ratingResult;
                    try
                    {
                        ratingResult = await rating.RateAsync(profile, quoteDate, ct);
                    }
                    catch (RatingException ex)
                    {
                        // Eligible but not mechanically priceable (e.g. off-menu limit): a human
                        // decision, not a guessed price.
                        submission.Status = "Referred";
                        submission.ReferralReason = ReferralReason.RequiresManualIntake.ToString();
                        Audit.Record(db, "Submission", submission.Id, "ReferredFromRating", new { ex.Message });
                        await db.SaveChangesAsync(ct);
                        return (StatusCodes.Status200OK,
                            (object)new SubmissionOutcomeResponse(submission.Id, "Referred",
                                ReferralReason.RequiresManualIntake.ToString(), ex.Message, null),
                            submission.Id);
                    }

                    var quote = new QuoteRow
                    {
                        QuoteId = Guid.NewGuid(),
                        SubmissionId = submission.Id,
                        Status = "Quoted",
                        Premium = ratingResult.FinalPremium,
                        Limit = profile.RequestedLimit,
                        Retention = profile.RequestedRetention,
                        SubjectivitiesJson = JsonSerializer.Serialize(StandardSubjectivities),
                        QuoteDate = quoteDate,
                        ExpiresOn = quoteDate.AddDays(30),
                        BasePremium = ratingResult.BasePremium,
                        RateEffectiveDate = ratingResult.RateEffectiveDate,
                        LedgerJson = JsonSerializer.Serialize(ratingResult.Ledger),
                        CreatedAt = DateTimeOffset.UtcNow,
                    };
                    db.Quotes.Add(quote);
                    submission.Status = "Quoted";
                    submission.QuoteId = quote.QuoteId;
                    Audit.Record(db, "Quote", quote.QuoteId, "QuoteIssued",
                        new { quote.Premium, quote.QuoteDate, quote.RateEffectiveDate, submission.Id });
                    await db.SaveChangesAsync(ct);

                    var response = new SubmissionOutcomeResponse(submission.Id, "Quoted", null, null,
                        new QuoteResponse(quote.QuoteId, quote.Premium, quote.Limit, quote.Retention,
                            StandardSubjectivities, quote.QuoteDate, quote.ExpiresOn,
                            ratingResult.BasePremium, ratingResult.RateEffectiveDate, ratingResult.Ledger));
                    return (StatusCodes.Status201Created, (object)response, quote.QuoteId);
                }, ct);
        });

        app.MapGet("/submissions/{id:guid}", async (Guid id, QuoterDbContext db, CancellationToken ct) =>
        {
            var s = await db.Submissions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            return s is null
                ? Results.NotFound()
                : Results.Ok(new { s.Id, s.Status, s.ReferralReason, s.QuoteId, s.ReceivedAt });
        });

        app.MapGet("/quotes/{id:guid}", async (Guid id, QuoterDbContext db, CancellationToken ct) =>
        {
            var q = await db.Quotes.AsNoTracking().SingleOrDefaultAsync(x => x.QuoteId == id, ct);
            if (q is null) return Results.NotFound();
            return Results.Ok(new QuoteResponse(q.QuoteId, q.Premium, q.Limit, q.Retention,
                JsonSerializer.Deserialize<string[]>(q.SubjectivitiesJson)!, q.QuoteDate, q.ExpiresOn,
                q.BasePremium, q.RateEffectiveDate,
                JsonSerializer.Deserialize<List<RatingStep>>(q.LedgerJson)!));
        });
    }
}
