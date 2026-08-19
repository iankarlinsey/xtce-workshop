using System.Text;
using Xunit;

namespace Xtce.Workshop.Model.Tests;

public class ContainerSetTests
{
    private static SpaceSystem LoadContainersSample()
    {
        using var stream = File.OpenRead(TestPaths.ContainersSample);
        return XtceDocumentReader.Load(stream);
    }

    [Fact]
    public void ContainersSampleFixture_IsItselfSchemaValid()
    {
        Assert.Empty(XsdValidation.Validate(File.ReadAllText(TestPaths.ContainersSample)));
    }

    [Fact]
    public void Load_ParsesAllContainers()
    {
        var loaded = LoadContainersSample();
        var containers = loaded.TelemetryMetaData!.ContainerSet!;

        Assert.Equal(
            ["CcsdsHeader", "EpsPacket", "AdcsPacket", "ChainedPacket", "TrailerFragment"],
            containers.Select(c => c.Name).ToList());
    }

    [Fact]
    public void Load_ParsesAbstractFlagAndPreservesUnmodeledContainerContent()
    {
        var loaded = LoadContainersSample();
        var header = loaded.TelemetryMetaData!.ContainerSet!.Single(c => c.Name == "CcsdsHeader");

        Assert.Equal(true, header.Abstract);
        Assert.Equal(["LongDescription"], header.Preserved!.Select(f => f.ElementName).ToList());
        Assert.Equal("Primary header layout",
            header.PreservedAttributes!.Single(a => a.Name == "shortDescription").Value);

        var eps = loaded.TelemetryMetaData.ContainerSet!.Single(c => c.Name == "EpsPacket");
        Assert.Null(eps.Abstract); // absent attribute stays null, not baked to false
    }

    [Fact]
    public void Load_KeepsEntryOrderIncludingRawEntriesInPosition()
    {
        var loaded = LoadContainersSample();
        var eps = loaded.TelemetryMetaData!.ContainerSet!.Single(c => c.Name == "EpsPacket");

        Assert.Equal(3, eps.EntryList.Count);
        Assert.Equal(SequenceEntryKind.ParameterRef, eps.EntryList[0].Kind);
        Assert.Equal("BusVoltage", eps.EntryList[0].Ref);
        // The segment entry is unmodeled but must keep its middle position — entry order
        // IS the packet layout.
        Assert.Equal(SequenceEntryKind.Raw, eps.EntryList[1].Kind);
        Assert.Equal("ParameterSegmentRefEntry", eps.EntryList[1].RawXml!.ElementName);
        Assert.Equal(SequenceEntryKind.ContainerRef, eps.EntryList[2].Kind);
        Assert.Equal("TrailerFragment", eps.EntryList[2].Ref);
    }

    [Fact]
    public void Load_PreservesEntryChildrenLikeLocationInContainerInBits()
    {
        var loaded = LoadContainersSample();
        var header = loaded.TelemetryMetaData!.ContainerSet!.Single(c => c.Name == "CcsdsHeader");

        var seqCountEntry = header.EntryList[1];
        Assert.Equal("SeqCount", seqCountEntry.Ref);
        Assert.Equal(["LocationInContainerInBits"], seqCountEntry.Preserved!.Select(f => f.ElementName).ToList());
        Assert.Contains("FixedValue", seqCountEntry.Preserved[0].OuterXml);
    }

    [Fact]
    public void Load_ParsesAllRestrictionCriteriaForms()
    {
        var loaded = LoadContainersSample();
        var containers = loaded.TelemetryMetaData!.ContainerSet!;

        var eps = containers.Single(c => c.Name == "EpsPacket");
        Assert.Equal("CcsdsHeader", eps.BaseContainer!.ContainerRef);
        var single = eps.BaseContainer.RestrictionCriteria!.Comparison!;
        Assert.Equal("Apid", single.ParameterRef);
        Assert.Equal("101", single.Value);
        Assert.Null(single.ComparisonOperator); // absent, default == applied by consumers

        var adcs = containers.Single(c => c.Name == "AdcsPacket");
        var list = adcs.BaseContainer!.RestrictionCriteria!.ComparisonList!;
        Assert.Equal(2, list.Count);
        Assert.Equal(">=", list[1].ComparisonOperator);
        Assert.Equal(-1, list[1].Instance);
        Assert.Equal(false, list[1].UseCalibratedValue);

        // NextContainer is additive to the required match-criteria choice, never standalone
        // (RestrictionCriteriaType extends MatchCriteriaType — see the record's doc comment).
        var chained = containers.Single(c => c.Name == "ChainedPacket");
        Assert.Equal("EpsPacket", chained.BaseContainer!.RestrictionCriteria!.NextContainerRef);
        Assert.Equal("103", chained.BaseContainer.RestrictionCriteria.Comparison!.Value);
    }

    [Fact]
    public void RoundTrip_ContainersSample_IsLossless()
    {
        var loaded = LoadContainersSample();

        var xml = XtceDocumentWriter.Write(loaded);
        var reloaded = XtceDocumentReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

        Assert.Equal(loaded, reloaded);
    }

    [Fact]
    public void RoundTrip_ContainersSample_OutputValidatesAgainstXtceXsd()
    {
        var loaded = LoadContainersSample();

        var xml = XtceDocumentWriter.Write(loaded);
        var errors = XsdValidation.Validate(xml);

        Assert.True(errors.Count == 0, "Writer output failed XSD validation:\n" + string.Join("\n", errors));
    }

    [Fact]
    public void Write_EmptyEntryListIsStillWritten_BecauseXsdRequiresIt()
    {
        var container = new SequenceContainer("Empty", []);
        var telemetry = new TelemetryMetaData([], [], ContainerSet: [container]);
        var spaceSystem = new SpaceSystem("S", [], telemetry);

        var xml = XtceDocumentWriter.Write(spaceSystem);

        Assert.Contains("<EntryList", xml);
        Assert.Empty(XsdValidation.Validate(xml));
    }

    [Fact]
    public void RoundTrip_ProgrammaticallyBuiltContainers_IsLossless()
    {
        var telemetry = new TelemetryMetaData(
            [],
            [],
            ContainerSet:
            [
                new SequenceContainer(
                    "Frame",
                    [
                        new SequenceEntry(SequenceEntryKind.ParameterRef, "P1"),
                        new SequenceEntry(SequenceEntryKind.ContainerRef, "Sub"),
                    ],
                    new BaseContainer("Base", new RestrictionCriteria(
                        ComparisonList:
                        [
                            new Comparison("P1", "5", ">"),
                            new Comparison("P2", "OK"),
                        ])),
                    Abstract: false),
                new SequenceContainer("Sub", [new SequenceEntry(SequenceEntryKind.ParameterRef, "P2")]),
                new SequenceContainer("Base", [], Abstract: true),
            ]);
        var original = new SpaceSystem("S", [], telemetry);

        var xml = XtceDocumentWriter.Write(original);
        var reloaded = XtceDocumentReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

        Assert.Equal(original, reloaded);
        Assert.Empty(XsdValidation.Validate(xml));
    }
}
