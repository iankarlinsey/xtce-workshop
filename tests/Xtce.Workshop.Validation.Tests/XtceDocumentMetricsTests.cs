using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation.Tests;

/// <summary>XtceDocumentMetrics: per-system and deep counts.</summary>
public class XtceDocumentMetricsTests
{
    private const string Ns = "http://www.omg.org/spec/XTCE/20180204";

    private static SpaceSystem SampleTree() =>
        new("Root",
            [
                new SpaceSystem("Bus", [], new TelemetryMetaData(
                    [
                        new ParameterTypeDefinition("U8", ParameterTypeKind.Integer),
                        new ParameterTypeDefinition("Mode", ParameterTypeKind.Enumerated,
                            Enumerations: [new EnumerationEntry(0, "SAFE")]),
                    ],
                    [new Parameter("P1", "U8"), new Parameter("P2", "Mode")],
                    ContainerSet: [new SequenceContainer("Frame", [])])),
                new SpaceSystem("Payload", [], CommandMetaData: new CommandMetaData(
                    [new MetaCommand("Fire")],
                    Preserved: [new RawXmlFragment("ArgumentTypeSet", $"""<ArgumentTypeSet xmlns="{Ns}"/>""")])),
            ],
            new TelemetryMetaData([], [],
                Preserved: [new RawXmlFragment("StreamSet", $"""<StreamSet xmlns="{Ns}"/>""")]));

    [Test]
    public void Compute_ReportsLocalCountsPerSystem()
    {
        var metrics = XtceDocumentMetrics.Compute(SampleTree());

        Assert.Equal(["Root", "Root/Bus", "Root/Payload"], metrics.Systems.Select(s => s.SystemPath));

        var bus = metrics.Systems.Single(s => s.SystemPath == "Root/Bus");
        Assert.Equal(2, bus.Local.Parameters);
        Assert.Equal(2, bus.Local.ParameterTypes);
        Assert.Equal(1, bus.Local.Containers);
        Assert.Equal(1, bus.Local.ParameterTypesByKind["Integer"]);
        Assert.Equal(1, bus.Local.ParameterTypesByKind["Enumerated"]);

        var payload = metrics.Systems.Single(s => s.SystemPath == "Root/Payload");
        Assert.Equal(1, payload.Local.MetaCommands);
        Assert.Equal(1, payload.Local.PreservedFragments);
    }

    [Test]
    public void Compute_DeepCountsRollUpTheSubtree_AndTotalsEqualRootDeep()
    {
        var metrics = XtceDocumentMetrics.Compute(SampleTree());

        var root = metrics.Systems.Single(s => s.SystemPath == "Root");
        Assert.Equal(0, root.Local.Parameters);
        Assert.Equal(2, root.Deep.Parameters);
        Assert.Equal(2, root.Deep.ParameterTypes);
        Assert.Equal(1, root.Deep.MetaCommands);
        Assert.Equal(2, root.Deep.ChildSystems);
        Assert.Equal(2, root.Deep.PreservedFragments); // StreamSet + ArgumentTypeSet

        Assert.Equal(root.Deep, metrics.Totals);
    }

    [Test]
    public void Compute_ExcludesCommentFragmentsFromThePreservedCount()
    {
        var document = new SpaceSystem("S", [], Preserved:
        [
            new RawXmlFragment(CommentAnchor.ElementName, "a note", "Header"),
            new RawXmlFragment("Header", $"""<Header xmlns="{Ns}" date="2026"/>"""),
        ]);

        var metrics = XtceDocumentMetrics.Compute(document);

        Assert.Equal(1, metrics.Totals.PreservedFragments);
    }
}
