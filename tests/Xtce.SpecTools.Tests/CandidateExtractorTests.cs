using Xunit;

namespace Xtce.SpecTools.Tests;

public class CandidateExtractorTests
{
    [Fact]
    public void Extract_FindsAllDocumentationNodes_MatchingCrossValidatedCount()
    {
        var result = CandidateExtractor.Extract(TestPaths.Xtce12Xsd);

        // Independently cross-validated (Python ElementTree vs .NET XDocument) during
        // design discussion — see InventoryExtractorTests for the counterpart check.
        Assert.Equal(861, result.TotalDocumentationNodes);
    }

    [Fact]
    public void Extract_CandidateCount_MatchesCurrentBaseline()
    {
        var result = CandidateExtractor.Extract(TestPaths.Xtce12Xsd);

        // This is the actual Phase A output for Phase B triage: 109 XSD documentation
        // blocks flagged with normative language, out of 861 total. A future keyword-list
        // change should shift this deliberately, not accidentally — that's why it's an
        // exact assertion rather than a range.
        Assert.Equal(109, result.CandidateCount);
        Assert.InRange(result.CandidateCount, 1, result.TotalDocumentationNodes);
    }

    [Fact]
    public void Extract_FlagsKnownFalsePositive_ArchaicConditionalPhrasing()
    {
        // "Should negative exponents be required, use a Math Calibrator style..." matches
        // both "should" and "required" as keywords but is archaic conditional phrasing
        // ("if X is required"), not a stacked pair of normative statements. The extractor
        // is expected to surface this as a candidate (keyword matching can't resolve the
        // ambiguity) — triage is where a human/agent decides it's not a real rule. This
        // test documents that expectation so the noisy case doesn't get "fixed" away by
        // a future keyword-matching tweak without that decision being deliberate.
        var result = CandidateExtractor.Extract(TestPaths.Xtce12Xsd);

        var falsePositive = result.Candidates.SingleOrDefault(c =>
            c.Text.Contains("Math Calibrator", StringComparison.Ordinal));

        Assert.NotNull(falsePositive);
        Assert.Contains("should", falsePositive.MatchedKeywords);
        Assert.Contains("required", falsePositive.MatchedKeywords);
    }
}
