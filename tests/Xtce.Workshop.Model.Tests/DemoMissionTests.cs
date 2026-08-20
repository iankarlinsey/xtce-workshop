using System.Text;
using Xtce.Workshop.Validation;
using Xunit;

namespace Xtce.Workshop.Model.Tests;

/// <summary>
/// The comprehensive demo mission (samples/demo-mission-1.2.xml) is the whole system's
/// integration fixture: every modeled construct in one document, schema-valid, losslessly
/// round-tripping, validating CLEAN, and packet-layout-computable.
/// </summary>
public class DemoMissionTests
{
    private static SpaceSystem LoadDemoMission()
    {
        using var stream = File.OpenRead(TestPaths.DemoMissionSample);
        return XtceDocumentReader.Load(stream);
    }

    [Fact]
    public void DemoMission_IsSchemaValid()
    {
        Assert.Empty(XsdValidation.Validate(File.ReadAllText(TestPaths.DemoMissionSample)));
    }

    [Fact]
    public void DemoMission_RoundTripsLosslesslyAndOutputStaysSchemaValid()
    {
        var loaded = LoadDemoMission();

        var xml = XtceDocumentWriter.Write(loaded);
        var reloaded = XtceDocumentReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

        Assert.Equal(loaded, reloaded);
        var errors = XsdValidation.Validate(xml);
        Assert.True(errors.Count == 0, "Writer output failed XSD validation:\n" + string.Join("\n", errors));
    }

    [Fact]
    public void DemoMission_ValidatesClean_AllTwentyOneRules()
    {
        var issues = XtceValidator.Validate(LoadDemoMission());

        Assert.True(issues.Count == 0,
            "Demo mission must be clean by construction:\n" +
            string.Join("\n", issues.Select(i => $"{i.RuleId} @ {i.Location}: {i.Message}")));
    }

    [Fact]
    public void DemoMission_ModelsEveryConstructKind()
    {
        var loaded = LoadDemoMission();
        var telemetry = loaded.TelemetryMetaData!;

        Assert.Equal(11, telemetry.ParameterTypeSet.Count);
        Assert.Equal(Enum.GetValues<ParameterTypeKind>().Length,
            telemetry.ParameterTypeSet.Select(t => t.Kind).Distinct().Count()); // all 10 kinds present
        Assert.Equal(11, telemetry.ParameterSet.Count);
        Assert.Equal(3, telemetry.ContainerSet!.Count);
        Assert.Single(telemetry.MessageSet!.Messages);
        Assert.Equal(2, loaded.CommandMetaData!.MetaCommands.Count);
        Assert.NotNull(loaded.CommandMetaData.MetaCommands[1].CommandContainer!.BaseContainerRef);
        Assert.Single(loaded.Children); // Payload subsystem with cross-system refs
    }

    [Fact]
    public void DemoMission_EpsPacketLayoutIsFullyStatic()
    {
        var layout = PacketLayoutBuilder.Build(LoadDemoMission(), [], "EpsPacket")!;

        // Inherited header (11 + 14) + BusVoltage(32) + Heater(1) + Mode(3) = 61 bits.
        Assert.Equal(61, layout.TotalSizeInBits);
        Assert.Equal(["Apid", "SeqCount", "BusVoltage", "Heater", "Mode"],
            layout.Rows.Select(r => r.Name).ToList());
        Assert.All(layout.Rows, r => Assert.NotNull(r.SizeInBits));
    }
}
