using System.Text;

namespace Xtce.Workshop.Model.Tests;

public class TimeBinaryTypeTests
{
    private static SpaceSystem LoadTimeBinarySample()
    {
        using var stream = File.OpenRead(TestPaths.TimeBinarySample);
        return XtceDocumentReader.Load(stream);
    }

    [Test]
    public void TimeBinarySampleFixture_IsItselfSchemaValid()
    {
        Assert.Empty(XsdValidation.Validate(File.ReadAllText(TestPaths.TimeBinarySample)));
    }

    [Test]
    public void Load_ParsesBinaryAndTimeKinds()
    {
        var types = LoadTimeBinarySample().TelemetryMetaData!.ParameterTypeSet;

        Assert.Equal(ParameterTypeKind.Binary, types.Single(t => t.Name == "Blob_Type").Kind);
        Assert.Equal("CAFEBABE", types.Single(t => t.Name == "Blob_Type").InitialValue);
        Assert.Equal(ParameterTypeKind.RelativeTime, types.Single(t => t.Name == "Uptime_Type").Kind);
        Assert.Equal("PT0S", types.Single(t => t.Name == "Uptime_Type").InitialValue);
        Assert.Equal(ParameterTypeKind.AbsoluteTime, types.Single(t => t.Name == "MissionTime_Type").Kind);
    }

    [Test]
    public void Load_PreservesEncodingAndBaseTypeOnTimeTypes()
    {
        var types = LoadTimeBinarySample().TelemetryMetaData!.ParameterTypeSet;

        // The Encoding wrapper is modeled since #100 — units on the record, the inner
        // data encoding as a nested DataEncoding, no fragment left behind.
        var uptime = types.Single(t => t.Name == "Uptime_Type");
        Assert.Null(uptime.Preserved);
        Assert.Equal("seconds", uptime.TimeEncoding!.Units);
        Assert.Equal((DataEncodingKind.Integer, 32L, "unsigned"),
            (uptime.TimeEncoding.DataEncoding!.Kind, uptime.TimeEncoding.DataEncoding.SizeInBits!.Value,
             uptime.TimeEncoding.DataEncoding.Encoding));

        var derived = types.Single(t => t.Name == "DerivedTime_Type");
        Assert.Equal("MissionTime_Type", derived.PreservedAttributes!.Single(a => a.Name == "baseType").Value);

        var naked = types.Single(t => t.Name == "NakedTime_Type");
        Assert.Null(naked.Preserved);
    }

    [Test]
    public void RoundTrip_TimeBinarySample_IsLosslessAndSchemaValid()
    {
        var loaded = LoadTimeBinarySample();

        var xml = XtceDocumentWriter.Write(loaded);
        var reloaded = XtceDocumentReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

        Assert.Equal(loaded, reloaded);
        var errors = XsdValidation.Validate(xml);
        Assert.True(errors.Count == 0, "Writer output failed XSD validation:\n" + string.Join("\n", errors));
    }
}
