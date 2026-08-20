using System.Text;
using Xunit;

namespace Xtce.Workshop.Model.Tests;

public class XtceDocumentReaderTests
{
    [Fact]
    public void Load_MinimalSampleFile_ReturnsSpaceSystemWithName()
    {
        using var stream = File.OpenRead(TestPaths.MinimalSample);

        var result = XtceDocumentReader.Load(stream);

        Assert.Equal("Minimal", result.Name);
    }

    [Fact]
    public void Load_MinimalSampleFile_ReturnsEmptyChildrenNotNull()
    {
        using var stream = File.OpenRead(TestPaths.MinimalSample);

        var result = XtceDocumentReader.Load(stream);

        Assert.NotNull(result.Children);
        Assert.Empty(result.Children);
    }

    [Fact]
    public void Load_NestedSampleFile_ReturnsCorrectStructureAtEveryLevel()
    {
        using var stream = File.OpenRead(TestPaths.NestedSample);

        var result = XtceDocumentReader.Load(stream);

        Assert.Equal("Mission", result.Name);
        Assert.Equal(2, result.Children.Count);

        var bus = result.Children[0];
        Assert.Equal("Bus", bus.Name);
        Assert.Equal(2, bus.Children.Count);
        Assert.Equal("Power", bus.Children[0].Name);
        Assert.Empty(bus.Children[0].Children);
        Assert.Equal("Thermal", bus.Children[1].Name);
        Assert.Empty(bus.Children[1].Children);

        var payload = result.Children[1];
        Assert.Equal("Payload", payload.Name);
        Assert.Empty(payload.Children);
    }

    [Fact]
    public void Load_SampleWithoutTelemetryMetaData_LeavesTelemetryMetaDataNull()
    {
        using var stream = File.OpenRead(TestPaths.NestedSample);

        var result = XtceDocumentReader.Load(stream);

        Assert.Null(result.TelemetryMetaData);
    }

    [Fact]
    public void Load_TelemetrySampleFile_ParsesAllFiveParameterTypeKinds()
    {
        using var stream = File.OpenRead(TestPaths.TelemetrySample);

        var result = XtceDocumentReader.Load(stream);

        Assert.NotNull(result.TelemetryMetaData);
        var types = result.TelemetryMetaData!.ParameterTypeSet;
        Assert.Equal(5, types.Count);

        var integer = Assert.Single(types, t => t.Name == "BatteryCount_Type");
        Assert.Equal(ParameterTypeKind.Integer, integer.Kind);
        Assert.Equal(false, integer.Signed);
        Assert.Equal(8, integer.SizeInBits);
        Assert.Equal("4", integer.InitialValue);

        var floatType = Assert.Single(types, t => t.Name == "BusVoltage_Type");
        Assert.Equal(ParameterTypeKind.Float, floatType.Kind);
        Assert.Equal(32, floatType.SizeInBits);
        Assert.Equal("28.5", floatType.InitialValue);

        var stringType = Assert.Single(types, t => t.Name == "DeviceLabel_Type");
        Assert.Equal(ParameterTypeKind.String, stringType.Kind);
        Assert.Equal("unset", stringType.InitialValue);

        var boolType = Assert.Single(types, t => t.Name == "HeaterOn_Type");
        Assert.Equal(ParameterTypeKind.Boolean, boolType.Kind);
        Assert.Equal("On", boolType.OneStringValue);
        Assert.Equal("Off", boolType.ZeroStringValue);
        Assert.Equal("False", boolType.InitialValue);

        var enumType = Assert.Single(types, t => t.Name == "BusState_Type");
        Assert.Equal(ParameterTypeKind.Enumerated, enumType.Kind);
        Assert.Equal("SAFE", enumType.InitialValue);
        Assert.NotNull(enumType.Enumerations);
        Assert.Equal(3, enumType.Enumerations!.Count);
        Assert.Contains(enumType.Enumerations, e => e.Value == 0 && e.Label == "SAFE");
        Assert.Contains(enumType.Enumerations, e => e.Value == 1 && e.Label == "NOMINAL");
        Assert.Contains(enumType.Enumerations, e => e.Value == 2 && e.Label == "FAULT");
    }

    [Fact]
    public void Load_TelemetrySampleFile_ParsesAllParameters()
    {
        using var stream = File.OpenRead(TestPaths.TelemetrySample);

        var result = XtceDocumentReader.Load(stream);

        var parameters = result.TelemetryMetaData!.ParameterSet;
        Assert.Equal(5, parameters.Count);

        var busVoltage = Assert.Single(parameters, p => p.Name == "BusVoltage");
        Assert.Equal("BusVoltage_Type", busVoltage.ParameterTypeRef);
        Assert.Equal("29.1", busVoltage.InitialValue);

        var batteryCount = Assert.Single(parameters, p => p.Name == "BatteryCount");
        Assert.Null(batteryCount.InitialValue);
    }

    [Fact]
    public void Load_NotWellFormedXml_ThrowsXtceParseException()
    {
        using var stream = ToStream("<SpaceSystem name=\"Broken\"");

        var ex = Assert.Throws<XtceParseException>(() => XtceDocumentReader.Load(stream));
        Assert.Contains("not well-formed", ex.Message);
    }

    [Fact]
    public void Load_WrongRootElement_ThrowsXtceParseException()
    {
        using var stream = ToStream("<NotASpaceSystem name=\"Wrong\"/>");

        var ex = Assert.Throws<XtceParseException>(() => XtceDocumentReader.Load(stream));
        Assert.Contains("SpaceSystem", ex.Message);
    }

    [Fact]
    public void Load_MissingNameAttribute_ThrowsXtceParseException()
    {
        using var stream = ToStream("<SpaceSystem xmlns=\"http://www.omg.org/spec/XTCE/20180204\"/>");

        var ex = Assert.Throws<XtceParseException>(() => XtceDocumentReader.Load(stream));
        Assert.Contains("name", ex.Message);
    }

    [Fact]
    public void Load_EmptyInput_ThrowsXtceParseException()
    {
        using var stream = ToStream("");

        Assert.Throws<XtceParseException>(() => XtceDocumentReader.Load(stream));
    }

    [Fact]
    public void Load_AdversariallyDeepNesting_ThrowsCleanlyInsteadOfOverflowing()
    {
        var builder = new StringBuilder("""<SpaceSystem xmlns="http://www.omg.org/spec/XTCE/20180204" name="Root">""");
        for (var i = 0; i < 500; i++)
        {
            builder.Append($"""<SpaceSystem name="L{i}">""");
        }
        for (var i = 0; i < 500; i++)
        {
            builder.Append("</SpaceSystem>");
        }
        builder.Append("</SpaceSystem>");

        using var stream = ToStream(builder.ToString());

        var ex = Assert.Throws<XtceParseException>(() => XtceDocumentReader.Load(stream));
        Assert.Contains("depth", ex.Message);
    }

    [Fact]
    public void Load_RealisticNesting_IsUnaffectedByTheDepthGuard()
    {
        var builder = new StringBuilder("""<SpaceSystem xmlns="http://www.omg.org/spec/XTCE/20180204" name="Root">""");
        for (var i = 0; i < 50; i++)
        {
            builder.Append($"""<SpaceSystem name="L{i}">""");
        }
        for (var i = 0; i < 50; i++)
        {
            builder.Append("</SpaceSystem>");
        }
        builder.Append("</SpaceSystem>");

        using var stream = ToStream(builder.ToString());

        var result = XtceDocumentReader.Load(stream);

        Assert.Equal("Root", result.Name);
    }

    private static MemoryStream ToStream(string xml) => new(Encoding.UTF8.GetBytes(xml));
}
