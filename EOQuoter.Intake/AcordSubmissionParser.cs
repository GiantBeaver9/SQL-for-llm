using System.Globalization;
using System.Xml.Linq;
using EOQuoter.Domain;

namespace EOQuoter.Intake;

/// <summary>
/// Deterministic parser for structured ACORD-style XML — the only built intake path. It is strict
/// on purpose: a missing or malformed field is an Unparseable with a specific reason, never a
/// guessed value. Confidently emitting fields from a broken submission is the failure mode the
/// whole intake design exists to avoid; the human referral downstream is the catch-all.
/// </summary>
public class AcordSubmissionParser : ISubmissionParser
{
    private static readonly Dictionary<string, ProfessionClass> ClassCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ACCT"] = ProfessionClass.Accountant,
        ["ARCH"] = ProfessionClass.Architect,
        ["ITC"] = ProfessionClass.ItConsultant,
        ["MGMT"] = ProfessionClass.ManagementConsultant,
        ["INSA"] = ProfessionClass.InsuranceAgent,
        ["REA"] = ProfessionClass.RealEstateAgent,
        ["LAW"] = ProfessionClass.Lawyer,
    };

    public IntakeResult Parse(RawSubmission raw)
    {
        if (!raw.ContentType.Contains("xml", StringComparison.OrdinalIgnoreCase))
            return new Unparseable($"Unsupported content type '{raw.ContentType}' — structured ACORD XML is the only automated path");

        XElement root;
        try
        {
            root = XElement.Parse(raw.Content);
        }
        catch (Exception ex)
        {
            return new Unparseable($"Not well-formed XML: {ex.Message}");
        }

        if (root.Name.LocalName != "ACORD")
            return new Unparseable($"Root element is '{root.Name.LocalName}', expected 'ACORD'");

        var rq = root.Descendants("ProfLiabilityPolicyQuoteInqRq").FirstOrDefault();
        if (rq is null)
            return new Unparseable("Missing ProfLiabilityPolicyQuoteInqRq element");

        var classCd = Value(rq, "BusinessInfo", "ProfessionClassCd");
        if (classCd is null)
            return new Unparseable("Missing BusinessInfo/ProfessionClassCd");
        if (!ClassCodes.TryGetValue(classCd, out var professionClass))
            return new Unparseable($"Unknown profession class code '{classCd}'");

        if (!TryDecimal(Value(rq, "BusinessInfo", "AnnualRevenueAmt"), out var revenue) || revenue < 0)
            return new Unparseable("Missing or invalid BusinessInfo/AnnualRevenueAmt");
        var band = ToRevenueBand(revenue);
        if (band == RevenueBand.Unknown)
            return new Unparseable($"Annual revenue {revenue:0} exceeds the largest supported band ($25M)");

        if (!int.TryParse(Value(rq, "BusinessInfo", "NumYearsInBusiness"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var years) || years < 0)
            return new Unparseable("Missing or invalid BusinessInfo/NumYearsInBusiness");

        var state = Value(rq, "Addr", "StateProvCd");
        if (string.IsNullOrWhiteSpace(state) || state.Length != 2)
            return new Unparseable("Missing or invalid Addr/StateProvCd (expected 2-letter state code)");

        var coverage = rq.Descendants("CommlCoverage").FirstOrDefault(c => (string?)c.Element("CoverageCd") == "EO");
        if (coverage is null)
            return new Unparseable("Missing CommlCoverage with CoverageCd 'EO'");
        if (!TryDecimal(coverage.Element("Limit")?.Descendants("Amt").FirstOrDefault()?.Value, out var limit) || limit <= 0)
            return new Unparseable("Missing or invalid CommlCoverage/Limit amount");
        if (!TryDecimal(coverage.Element("Deductible")?.Descendants("Amt").FirstOrDefault()?.Value, out var retention) || retention < 0)
            return new Unparseable("Missing or invalid CommlCoverage/Deductible amount");

        var claims = new List<PriorClaim>();
        foreach (var loss in rq.Descendants("Loss"))
        {
            if (!TryDecimal(loss.Element("TotalPaidAmt")?.Value, out var amount) || amount < 0)
                return new Unparseable("Loss element with missing or invalid TotalPaidAmt");
            if (!DateOnly.TryParse(loss.Element("LossDt")?.Value, CultureInfo.InvariantCulture, out var lossDate))
                return new Unparseable("Loss element with missing or invalid LossDt");
            claims.Add(new PriorClaim(amount, lossDate));
        }

        return new Parsed(new RiskProfile(
            professionClass, band, limit, retention, claims, years, state.ToUpperInvariant()));
    }

    private static string? Value(XElement scope, string parent, string element) =>
        scope.Descendants(parent).Elements(element).FirstOrDefault()?.Value.Trim();

    private static bool TryDecimal(string? s, out decimal value) =>
        decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out value);

    private static RevenueBand ToRevenueBand(decimal revenue) => revenue switch
    {
        <= 250_000m => RevenueBand.UpTo250K,
        <= 1_000_000m => RevenueBand.From250KTo1M,
        <= 5_000_000m => RevenueBand.From1MTo5M,
        <= 25_000_000m => RevenueBand.From5MTo25M,
        _ => RevenueBand.Unknown,
    };
}
