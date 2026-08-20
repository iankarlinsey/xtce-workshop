using System.Text;

namespace Xtce.Workshop.Model.Tests;

public class XtceDocumentWriterTests
{
    [Test]
    public void Write_ChildlessSpaceSystem_RoundTripsThroughReader()
    {
        var original = new SpaceSystem("Minimal", []);

        var xml = XtceDocumentWriter.Write(original);
        var reloaded = XtceDocumentReader.Load(ToStream(xml));

        Assert.Equal(original, reloaded);
    }

    [Test]
    public void Write_NestedSpaceSystem_RoundTripsThroughReader()
    {
        var original = new SpaceSystem("Mission", [
            new SpaceSystem("Bus", [
                new SpaceSystem("Power", []),
                new SpaceSystem("Thermal", []),
            ]),
            new SpaceSystem("Payload", []),
        ]);

        var xml = XtceDocumentWriter.Write(original);
        var reloaded = XtceDocumentReader.Load(ToStream(xml));

        Assert.Equal(original, reloaded);
    }

    [Test]
    public void Write_MinimalSampleLoadedThenWritten_RoundTripsThroughReader()
    {
        using var stream = File.OpenRead(TestPaths.MinimalSample);
        var loaded = XtceDocumentReader.Load(stream);

        var xml = XtceDocumentWriter.Write(loaded);
        var reloaded = XtceDocumentReader.Load(ToStream(xml));

        Assert.Equal(loaded, reloaded);
    }

    [Test]
    public void Write_NestedSampleLoadedThenWritten_RoundTripsThroughReader()
    {
        using var stream = File.OpenRead(TestPaths.NestedSample);
        var loaded = XtceDocumentReader.Load(stream);

        var xml = XtceDocumentWriter.Write(loaded);
        var reloaded = XtceDocumentReader.Load(ToStream(xml));

        Assert.Equal(loaded, reloaded);
    }

    [Test]
    public void Write_TelemetrySampleLoadedThenWritten_RoundTripsThroughReader()
    {
        using var stream = File.OpenRead(TestPaths.TelemetrySample);
        var loaded = XtceDocumentReader.Load(stream);

        var xml = XtceDocumentWriter.Write(loaded);
        var reloaded = XtceDocumentReader.Load(ToStream(xml));

        Assert.Equal(loaded, reloaded);
    }

    [Test]
    public void Write_SpaceSystemWithAllFiveParameterTypeKinds_RoundTripsThroughReader()
    {
        var telemetryMetaData = new TelemetryMetaData(
            [
                new ParameterTypeDefinition("Int_Type", ParameterTypeKind.Integer, Signed: true, SizeInBits: 16, InitialValue: "-1"),
                new ParameterTypeDefinition("Float_Type", ParameterTypeKind.Float, SizeInBits: 64, InitialValue: "1.5e2"),
                new ParameterTypeDefinition("String_Type", ParameterTypeKind.String, InitialValue: "hello"),
                new ParameterTypeDefinition("Bool_Type", ParameterTypeKind.Boolean, OneStringValue: "Yes", ZeroStringValue: "No"),
                new ParameterTypeDefinition("Enum_Type", ParameterTypeKind.Enumerated, InitialValue: "A",
                    Enumerations: [new EnumerationEntry(0, "A"), new EnumerationEntry(1, "B")]),
            ],
            [
                new Parameter("IntParam", "Int_Type"),
                new Parameter("FloatParam", "Float_Type", InitialValue: "3.0"),
            ]);
        var original = new SpaceSystem("WithTelemetry", [], telemetryMetaData);

        var xml = XtceDocumentWriter.Write(original);
        var reloaded = XtceDocumentReader.Load(ToStream(xml));

        Assert.Equal(original, reloaded);
    }

    [Test]
    public void Write_TelemetryMetaData_ProducesParameterTypeSetBeforeParameterSet()
    {
        var telemetryMetaData = new TelemetryMetaData(
            [new ParameterTypeDefinition("Int_Type", ParameterTypeKind.Integer)],
            [new Parameter("IntParam", "Int_Type")]);
        var spaceSystem = new SpaceSystem("Ordered", [], telemetryMetaData);

        var xml = XtceDocumentWriter.Write(spaceSystem);

        Assert.True(xml.IndexOf("<ParameterTypeSet", StringComparison.Ordinal) <
                    xml.IndexOf("<ParameterSet", StringComparison.Ordinal),
            "Expected ParameterTypeSet to precede ParameterSet, per TelemetryMetaDataType's XSD sequence.");
    }

    [Test]
    public void Write_IncludesXtceNamespaceOnRootElement()
    {
        var spaceSystem = new SpaceSystem("Minimal", []);

        var xml = XtceDocumentWriter.Write(spaceSystem);

        Assert.Contains("http://www.omg.org/spec/XTCE/20180204", xml);
    }

    private static MemoryStream ToStream(string xml) => new(Encoding.UTF8.GetBytes(xml));
}
