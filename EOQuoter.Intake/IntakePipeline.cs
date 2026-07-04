using EOQuoter.Domain;

namespace EOQuoter.Intake;

/// <summary>
/// The intake front door. Structured parse first; if that fails, any registered degraded-input
/// handlers get a try BEFORE the submission falls through to human referral. No handler ships —
/// the seam exists (priced during design, deliberately not built), so automation slots in later
/// without touching this pipeline. Zero handlers registered means every Unparseable goes to a human.
/// </summary>
public class IntakePipeline(ISubmissionParser parser, IEnumerable<IDegradedInputHandler> degradedHandlers)
{
    public IntakeResult Process(RawSubmission raw)
    {
        var result = parser.Parse(raw);
        if (result is Parsed) return result;

        foreach (var handler in degradedHandlers)
        {
            if (handler.TryExtract(raw) is Parsed rescued)
                return rescued;
        }

        return result; // original Unparseable, with the parser's reason — the human sees why
    }
}
