using Xtce.Workshop.Model;
using Xunit;

namespace Xtce.Workshop.Validation.Tests;

/// <summary>R08 (location-in-container flags, warning) and R04 (segment overlap, partial).</summary>
public class ContainerEntryRulesTests
{
    private const string R08 = "XTCE-1.2-R08-location-in-container-flags";
    private const string R04 = "XTCE-1.2-R04-container-segments-no-overlap";
    private const string Ns = "http://www.omg.org/spec/XTCE/20180204";

    private static SpaceSystem WithEntries(params SequenceEntry[] entries) =>
        new("S", [], new TelemetryMetaData([], [], ContainerSet: [new SequenceContainer("Frame", entries)]));

    private static SequenceEntry ModeledEntryWithLocation(string referenceLocation, long fixedValue) =>
        new(SequenceEntryKind.ParameterRef, "P", Preserved:
        [
            new RawXmlFragment("LocationInContainerInBits",
                $"""<LocationInContainerInBits referenceLocation="{referenceLocation}" xmlns="{Ns}"><FixedValue>{fixedValue}</FixedValue></LocationInContainerInBits>"""),
        ]);

    private static SequenceEntry Segment(string element, string refAttribute, string target, string? order) =>
        new(SequenceEntryKind.Raw, RawXml: new RawXmlFragment(element,
            order is null
                ? $"""<{element} {refAttribute}="{target}" sizeInBits="8" xmlns="{Ns}"/>"""
                : $"""<{element} {refAttribute}="{target}" order="{order}" sizeInBits="8" xmlns="{Ns}"/>"""));

    [Fact]
    public void NextEntryReferenceLocation_IsFlaggedAsDeprecated()
    {
        var issues = XtceValidator.Validate(WithEntries(ModeledEntryWithLocation("nextEntry", 0)));

        var issue = Assert.Single(issues, i => i.RuleId == R08);
        Assert.Equal(ValidationSeverity.Warning, issue.Severity);
        Assert.Contains("nextEntry", issue.Message);
    }

    [Theory]
    [InlineData("containerStart")]
    [InlineData("containerEnd")]
    public void NegativeAbsoluteOffsets_AreFlagged(string referenceLocation)
    {
        var issues = XtceValidator.Validate(WithEntries(ModeledEntryWithLocation(referenceLocation, -4)));

        var issue = Assert.Single(issues, i => i.RuleId == R08);
        Assert.Contains(referenceLocation, issue.Message);
        Assert.Contains("-4", issue.Message);
    }

    [Fact]
    public void PositiveOffsetsAndPreviousEntry_AreClean()
    {
        var issues = XtceValidator.Validate(WithEntries(
            ModeledEntryWithLocation("containerStart", 16),
            ModeledEntryWithLocation("previousEntry", -1))); // relative overlap is legal

        Assert.DoesNotContain(issues, i => i.RuleId == R08);
    }

    [Fact]
    public void LocationInsideRawSegmentEntry_IsAlsoInspected()
    {
        var raw = new SequenceEntry(SequenceEntryKind.Raw, RawXml: new RawXmlFragment(
            "ParameterSegmentRefEntry",
            $"""<ParameterSegmentRefEntry parameterRef="P" sizeInBits="8" xmlns="{Ns}"><LocationInContainerInBits referenceLocation="containerEnd"><FixedValue>-1</FixedValue></LocationInContainerInBits></ParameterSegmentRefEntry>"""));

        var issues = XtceValidator.Validate(WithEntries(raw));

        Assert.Single(issues, i => i.RuleId == R08);
    }

    [Fact]
    public void DuplicateSegmentOrderForSameTarget_IsFlagged()
    {
        var issues = XtceValidator.Validate(WithEntries(
            Segment("ParameterSegmentRefEntry", "parameterRef", "P", "0"),
            Segment("ParameterSegmentRefEntry", "parameterRef", "P", "0")));

        var issue = Assert.Single(issues, i => i.RuleId == R04);
        Assert.Equal(ValidationSeverity.Error, issue.Severity);
        Assert.Contains("order=\"0\"", issue.Message);
    }

    [Fact]
    public void DistinctOrdersDifferentTargetsAndOrderlessSegments_AreClean()
    {
        var issues = XtceValidator.Validate(WithEntries(
            Segment("ParameterSegmentRefEntry", "parameterRef", "P", "0"),
            Segment("ParameterSegmentRefEntry", "parameterRef", "P", "1"),
            Segment("ParameterSegmentRefEntry", "parameterRef", "Q", "0"),   // different target
            Segment("ContainerSegmentRefEntry", "containerRef", "P", "0"),   // different element kind
            Segment("ParameterSegmentRefEntry", "parameterRef", "R", null),  // orderless = sequential
            Segment("ParameterSegmentRefEntry", "parameterRef", "R", null)));

        Assert.DoesNotContain(issues, i => i.RuleId == R04);
    }
}
