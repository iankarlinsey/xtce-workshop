using System.Text;

namespace Xtce.Workshop.Model.Tests;

/// <summary>
/// The lossless round-trip guarantee: everything the object model doesn't
/// represent must survive load → save verbatim, and the written output must still validate
/// against the actual XTCE 1.2 XSD.
/// </summary>
public class PreservationRoundTripTests
{
    private static SpaceSystem LoadPreservationSample()
    {
        using var stream = File.OpenRead(TestPaths.PreservationSample);
        return XtceDocumentReader.Load(stream);
    }

    [Test]
    public void PreservationSampleFixture_IsItselfSchemaValid()
    {
        // Keeps the fixture honest: if the fixture drifts out of schema-validity, every
        // "output is schema-valid" claim below becomes meaningless.
        var xml = File.ReadAllText(TestPaths.PreservationSample);

        Assert.Empty(XsdValidation.Validate(xml));
    }

    [Test]
    public void Load_CapturesUnmodeledSpaceSystemChildrenAsFragments()
    {
        var loaded = LoadPreservationSample();

        Assert.NotNull(loaded.Preserved);
        var names = loaded.Preserved!.Select(f => f.ElementName).ToList();
        // CommandMetaData is modeled (an empty record here), not preserved.
        Assert.Equal(["LongDescription", "AliasSet", "Header"], names);
        Assert.NotNull(loaded.CommandMetaData);
        Assert.Contains("must survive load/save untouched", loaded.Preserved.Single(f => f.ElementName == "Header").OuterXml);
    }

    [Test]
    public void Load_CapturesUnmodeledAttributes()
    {
        var loaded = LoadPreservationSample();

        Assert.NotNull(loaded.PreservedAttributes);
        var byName = loaded.PreservedAttributes!.ToDictionary(a => a.Name);
        Assert.Equal("Round-trip preservation fixture", byName["shortDescription"].Value);
        Assert.Equal("unittest", byName["operationalStatus"].Value);
        Assert.True(byName.ContainsKey("xmlns:xsi"));
        Assert.True(byName.ContainsKey("xsi:schemaLocation"));
    }

    [Test]
    public void Load_ModelsAllScalarKindsAndPreservesSetEntries()
    {
        var loaded = LoadPreservationSample();
        var telemetry = loaded.TelemetryMetaData!;

        // Binary and time kinds are modeled — only Array/Aggregate would
        // still land in PreservedParameterTypes, and this fixture has none.
        Assert.Null(telemetry.PreservedParameterTypes);
        Assert.Equal(ParameterTypeKind.Binary,
            telemetry.ParameterTypeSet.Single(t => t.Name == "Blob_Type").Kind);
        Assert.Equal(ParameterTypeKind.AbsoluteTime,
            telemetry.ParameterTypeSet.Single(t => t.Name == "MissionTime_Type").Kind);
        Assert.Equal(["ParameterRef"], telemetry.PreservedParameters!.Select(f => f.ElementName).ToList());
        // ContainerSet is modeled, so nothing at the TelemetryMetaData
        // level is left to preserve in this fixture.
        Assert.Null(telemetry.Preserved);
        Assert.Equal("MainFrame", Assert.Single(telemetry.ContainerSet!).Name);
    }

    [Test]
    public void Load_CapturesUnmodeledParameterTypeContentAndAttributes()
    {
        var loaded = LoadPreservationSample();
        var counterType = loaded.TelemetryMetaData!.ParameterTypeSet.Single(t => t.Name == "Counter_Type");

        Assert.Equal(
            ["UnitSet", "IntegerDataEncoding"],
            counterType.Preserved!.Select(f => f.ElementName).ToList());
        var attributeNames = counterType.PreservedAttributes!.Select(a => a.Name).ToList();
        Assert.Contains("baseType", attributeNames);
        Assert.Contains("shortDescription", attributeNames);
    }

    [Test]
    public void Load_CapturesParameterChildrenAndAttributes()
    {
        var loaded = LoadPreservationSample();
        var counter = loaded.TelemetryMetaData!.ParameterSet.Single(p => p.Name == "Counter");

        Assert.Equal(["ParameterProperties"], counter.Preserved!.Select(f => f.ElementName).ToList());
        Assert.Equal("main counter", counter.PreservedAttributes!.Single(a => a.Name == "shortDescription").Value);
    }

    [Test]
    public void Load_ModelsEnumerationMaxValueAndShortDescription()
    {
        var loaded = LoadPreservationSample();
        var modeType = loaded.TelemetryMetaData!.ParameterTypeSet.Single(t => t.Name == "Mode_Type");

        var idle = modeType.Enumerations!.Single(e => e.Label == "IDLE");
        Assert.Equal("doing nothing", idle.ShortDescription);
        Assert.Null(idle.MaxValue);

        var active = modeType.Enumerations!.Single(e => e.Label == "ACTIVE");
        Assert.Equal(3, active.MaxValue);
    }

    [Test]
    public void Load_DoesNotBakeXsdDefaultsIntoAbsentAttributes()
    {
        var loaded = LoadPreservationSample();
        var modeType = loaded.TelemetryMetaData!.ParameterTypeSet.Single(t => t.Name == "Mode_Type");

        // Mode_Type carries no signed/sizeInBits/oneStringValue — and none apply to its
        // kind — but the Counter_Type case is the real check: absent stays null.
        Assert.Null(modeType.SizeInBits);

        using var minimalStream = new MemoryStream(Encoding.UTF8.GetBytes(
            """<SpaceSystem xmlns="http://www.omg.org/spec/XTCE/20180204" name="S"><TelemetryMetaData><ParameterTypeSet><IntegerParameterType name="I"/><BooleanParameterType name="B"/></ParameterTypeSet></TelemetryMetaData></SpaceSystem>"""));
        var minimal = XtceDocumentReader.Load(minimalStream);
        var integer = minimal.TelemetryMetaData!.ParameterTypeSet.Single(t => t.Name == "I");
        var boolean = minimal.TelemetryMetaData!.ParameterTypeSet.Single(t => t.Name == "B");

        Assert.Null(integer.Signed);
        Assert.Null(integer.SizeInBits);
        Assert.Null(boolean.OneStringValue);
        Assert.Null(boolean.ZeroStringValue);

        var written = XtceDocumentWriter.Write(minimal);
        Assert.DoesNotContain("signed", written);
        Assert.DoesNotContain("oneStringValue", written);
    }

    [Test]
    public void Load_UnparseableModeledAttribute_ThrowsInsteadOfSilentlyDroppingIt()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(
            """<SpaceSystem xmlns="http://www.omg.org/spec/XTCE/20180204" name="S"><TelemetryMetaData><ParameterTypeSet><IntegerParameterType name="I" signed="maybe"/></ParameterTypeSet></TelemetryMetaData></SpaceSystem>"""));

        var ex = Assert.Throws<XtceParseException>(() => XtceDocumentReader.Load(stream));
        Assert.Contains("signed", ex.Message);
    }

    [Test]
    public void RoundTrip_PreservationSample_IsLossless()
    {
        var loaded = LoadPreservationSample();

        var xml = XtceDocumentWriter.Write(loaded);
        var reloaded = XtceDocumentReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

        Assert.Equal(loaded, reloaded);
    }

    [Test]
    public void RoundTrip_PreservationSample_OutputContainsEveryPreservedConstruct()
    {
        var loaded = LoadPreservationSample();

        var xml = XtceDocumentWriter.Write(loaded);

        // Fragments carry a redundant (same-namespace) xmlns declaration on their root —
        // ReadOuterXml includes the in-scope default declaration — so match on "<Name",
        // never "<Name>".
        Assert.Contains("<Header", xml);
        Assert.Contains("<LongDescription", xml);
        Assert.Contains("<AliasSet", xml);
        Assert.Contains("<CommandMetaData", xml);
        Assert.Contains("<BinaryParameterType", xml);
        Assert.Contains("<AbsoluteTimeParameterType", xml);
        Assert.Contains("<ParameterRef", xml);
        Assert.Contains("<ContainerSet", xml);
        Assert.Contains("<ParameterProperties", xml);
        Assert.Contains("<IntegerDataEncoding", xml);
        Assert.Contains("<UnitSet", xml);
        Assert.Contains("operationalStatus=\"unittest\"", xml);
        Assert.Contains("baseType=\"SomeBase_Type\"", xml);
        Assert.Contains("xsi:schemaLocation", xml);
        Assert.Contains("maxValue=\"3\"", xml);
        Assert.Contains("shortDescription=\"doing nothing\"", xml);
    }

    [Test]
    public void RoundTrip_PreservationSample_OutputValidatesAgainstXtceXsd()
    {
        var loaded = LoadPreservationSample();

        var xml = XtceDocumentWriter.Write(loaded);
        var errors = XsdValidation.Validate(xml);

        Assert.True(errors.Count == 0, "Writer output failed XSD validation:\n" + string.Join("\n", errors));
    }

    [Test]
    public void RoundTrip_AfterEditingAName_PreservedContentSurvives()
    {
        // The actual editor scenario: load, rename something, save — nothing else changes.
        var loaded = LoadPreservationSample();
        var renamed = loaded with { Name = "RenamedDemo" };

        var xml = XtceDocumentWriter.Write(renamed);

        Assert.Contains("name=\"RenamedDemo\"", xml);
        Assert.Contains("<Header", xml);
        Assert.Contains("<ContainerSet", xml);
        Assert.Empty(XsdValidation.Validate(xml));
    }

    [Test]
    public void Writer_EmitsPreservedFragmentsInXsdSequenceOrder()
    {
        var loaded = LoadPreservationSample();

        var xml = XtceDocumentWriter.Write(loaded);

        var longDescription = xml.IndexOf("<LongDescription", StringComparison.Ordinal);
        var aliasSet = xml.IndexOf("<AliasSet", StringComparison.Ordinal);
        var header = xml.IndexOf("<Header", StringComparison.Ordinal);
        var telemetry = xml.IndexOf("<TelemetryMetaData", StringComparison.Ordinal);
        var command = xml.IndexOf("<CommandMetaData", StringComparison.Ordinal);
        var childSpaceSystem = xml.IndexOf("<SpaceSystem name=\"Bus\"", StringComparison.Ordinal);

        Assert.True(longDescription < aliasSet, "LongDescription must precede AliasSet");
        Assert.True(aliasSet < header, "AliasSet must precede Header");
        Assert.True(header < telemetry, "Header must precede TelemetryMetaData");
        Assert.True(telemetry < command, "TelemetryMetaData must precede CommandMetaData");
        Assert.True(command < childSpaceSystem, "CommandMetaData must precede child SpaceSystems");
    }
}
