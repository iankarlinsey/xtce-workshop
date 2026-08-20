using Xtce.Workshop.Model;
using Xunit;

namespace Xtce.Workshop.Validation.Tests;

/// <summary>XtceCsvExporter (issue #54): parameters and containers as RFC 4180 CSV.</summary>
public class XtceCsvExporterTests
{
    private const string Ns = "http://www.omg.org/spec/XTCE/20180204";

    private static SpaceSystem Sample() =>
        new("Sat", [], new TelemetryMetaData(
            [
                new ParameterTypeDefinition("U8", ParameterTypeKind.Integer, Preserved:
                [
                    new RawXmlFragment("IntegerDataEncoding", $"""<IntegerDataEncoding xmlns="{Ns}" sizeInBits="8"/>"""),
                ]),
            ],
            [
                new Parameter("Batt, \"main\"", "U8", "7", Preserved:
                [
                    new RawXmlFragment("AliasSet", $"""<AliasSet xmlns="{Ns}"><Alias nameSpace="ops" alias="EPS_V"/></AliasSet>"""),
                    new RawXmlFragment("ParameterProperties", $"""<ParameterProperties xmlns="{Ns}" dataSource="telemetered"/>"""),
                ]),
            ],
            ContainerSet:
            [
                new SequenceContainer("Frame", [new SequenceEntry(SequenceEntryKind.ParameterRef, "Batt, \"main\"")]),
            ]));

    [Fact]
    public void ExportParameters_EmitsResolvedTypeInfo_Aliases_AndDataSource()
    {
        var csv = XtceCsvExporter.ExportParameters(Sample());
        var lines = csv.TrimEnd().Split("\r\n");

        Assert.Equal("SystemPath,Name,ParameterTypeRef,Kind,EncodedSizeInBits,InitialValue,DataSource,Aliases", lines[0]);
        // Comma and quotes in the name force RFC 4180 quoting with doubled quotes.
        Assert.Equal("Sat,\"Batt, \"\"main\"\"\",U8,Integer,8,7,telemetered,ops:EPS_V", lines[1]);
        Assert.Equal(2, lines.Length);
    }

    [Fact]
    public void ExportContainers_EmitsLayoutRowsWithOffsets()
    {
        var csv = XtceCsvExporter.ExportContainers(Sample());
        var lines = csv.TrimEnd().Split("\r\n");

        Assert.Equal("SystemPath,Container,EntryName,EntryKind,SourceContainer,OffsetInBits,SizeInBits,Note", lines[0]);
        Assert.Contains("Sat,Frame,", lines[1]);
        Assert.Contains(",parameter,Frame,0,8,", lines[1]);
    }

    [Fact]
    public void ExportContainers_CoversNestedSystems()
    {
        var tree = new SpaceSystem("Root",
        [
            new SpaceSystem("Bus", [], new TelemetryMetaData([], [],
                ContainerSet: [new SequenceContainer("Hk", [])])),
        ]);

        var csv = XtceCsvExporter.ExportContainers(tree);

        // An empty container still resolves through PacketLayoutBuilder (no rows), so
        // only the header appears — but a container with entries in a nested system must
        // carry the nested path. Assert via a populated variant:
        var populated = new SpaceSystem("Root",
        [
            new SpaceSystem("Bus", [], new TelemetryMetaData(
                [new ParameterTypeDefinition("T", ParameterTypeKind.Integer)],
                [new Parameter("P", "T")],
                ContainerSet: [new SequenceContainer("Hk", [new SequenceEntry(SequenceEntryKind.ParameterRef, "P")])])),
        ]);
        var populatedCsv = XtceCsvExporter.ExportContainers(populated);
        Assert.Contains("Root/Bus,Hk,P,parameter", populatedCsv);
        Assert.NotNull(csv);
    }
}
