using Xtce.Workshop.Model;
using Xunit;

namespace Xtce.Workshop.Validation.Tests;

/// <summary>XtceDocumentQuery: name/alias search and parameter where-used.</summary>
public class XtceDocumentQueryTests
{
    private const string Ns = "http://www.omg.org/spec/XTCE/20180204";

    private static SpaceSystem SampleTree() =>
        new("Root",
            [
                new SpaceSystem("Bus", [], new TelemetryMetaData(
                    [new ParameterTypeDefinition("Volt_Type", ParameterTypeKind.Integer)],
                    [
                        new Parameter("BattVoltage", "Volt_Type", Preserved:
                        [
                            new RawXmlFragment("AliasSet",
                                $"""<AliasSet xmlns="{Ns}"><Alias nameSpace="ops" alias="EPS_V_BATT"/></AliasSet>"""),
                        ]),
                        new Parameter("BusCurrent", "Volt_Type"),
                    ],
                    ContainerSet:
                    [
                        new SequenceContainer("EpsHk",
                        [
                            new SequenceEntry(SequenceEntryKind.ParameterRef, "BattVoltage"),
                            new SequenceEntry(SequenceEntryKind.Raw, RawXml: new RawXmlFragment(
                                "ParameterSegmentRefEntry",
                                $"""<ParameterSegmentRefEntry xmlns="{Ns}" parameterRef="BattVoltage" order="1" sizeInBits="4"/>""")),
                        ]),
                        new SequenceContainer("Sub", [],
                            new BaseContainer("EpsHk", new RestrictionCriteria(
                                new Comparison("BattVoltage", "1")))),
                    ])),
            ]);

    [Fact]
    public void Search_MatchesBySubstring_CaseInsensitive_AcrossKinds()
    {
        var matches = XtceDocumentQuery.Search(SampleTree(), "volt");

        Assert.Equal(2, matches.Count);
        Assert.Contains(matches, m => m is { Kind: "Parameter", Name: "BattVoltage", SystemPath: "Root/Bus" });
        Assert.Contains(matches, m => m is { Kind: "ParameterType", Name: "Volt_Type" });
    }

    [Fact]
    public void Search_GlobRequiresFullMatch()
    {
        var matches = XtceDocumentQuery.Search(SampleTree(), "B*age");

        var match = Assert.Single(matches);
        Assert.Equal("BattVoltage", match.Name);
    }

    [Fact]
    public void Search_MatchesAliases_AndReportsWhichAliasMatched()
    {
        var matches = XtceDocumentQuery.Search(SampleTree(), "EPS_V*");

        var match = Assert.Single(matches);
        Assert.Equal("BattVoltage", match.Name);
        Assert.Equal("EPS_V_BATT", match.MatchedAlias);
    }

    [Fact]
    public void FindParameterUsages_CoversEntries_RawSegments_AndComparisons_WithoutDoubleCounting()
    {
        var usages = XtceDocumentQuery.FindParameterUsages(SampleTree(), "Root/Bus", "BattVoltage");

        Assert.Equal(3, usages.Count);
        Assert.Contains(usages, u => u.Kind == "ParameterRefEntry" && u.Location.EndsWith("EpsHk"));
        Assert.Contains(usages, u => u.Kind == "ParameterSegmentRefEntry");
        Assert.Contains(usages, u => u.Kind == "RestrictionComparison" && u.Location.EndsWith("Sub"));
    }

    [Fact]
    public void FindParameterUsages_DoesNotMatchASameNamedParameterElsewhere()
    {
        var tree = new SpaceSystem("Root",
        [
            new SpaceSystem("A", [], new TelemetryMetaData(
                [new ParameterTypeDefinition("T", ParameterTypeKind.Integer)],
                [new Parameter("P", "T")],
                ContainerSet: [new SequenceContainer("C", [new SequenceEntry(SequenceEntryKind.ParameterRef, "P")])])),
            new SpaceSystem("B", [], new TelemetryMetaData(
                [new ParameterTypeDefinition("T", ParameterTypeKind.Integer)],
                [new Parameter("P", "T")])),
        ]);

        // A/C's entry binds to A's P — asking for B's P must return nothing.
        Assert.Empty(XtceDocumentQuery.FindParameterUsages(tree, "Root/B", "P"));
        Assert.Single(XtceDocumentQuery.FindParameterUsages(tree, "Root/A", "P"));
    }

    [Fact]
    public void FindParameterUsages_SeesReferencesInsidePreservedCommandFragments()
    {
        var tree = new SpaceSystem("Sat", [],
            new TelemetryMetaData(
                [new ParameterTypeDefinition("T", ParameterTypeKind.Integer)],
                [new Parameter("Ack", "T")]),
            CommandMetaData: new CommandMetaData(
            [
                new MetaCommand("Cmd", CompleteVerifiers:
                [
                    new RawXmlFragment("CompleteVerifier",
                        $"""<CompleteVerifier xmlns="{Ns}"><Comparison parameterRef="Ack" value="1"/><CheckWindow timeToStopChecking="PT5S"/></CompleteVerifier>"""),
                ]),
            ]));

        var usages = XtceDocumentQuery.FindParameterUsages(tree, "Sat", "Ack");

        var usage = Assert.Single(usages);
        Assert.Equal("Comparison", usage.Kind);
        Assert.Contains("MetaCommandSet/Cmd", usage.Location);
    }
}
