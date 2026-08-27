using System.Text;
using Xtce.Workshop.Model;

namespace Xtce.Workshop.Model.Tests;

/// <summary>
/// Issue #88: a user's algorithm-heavy XTCE never finishes loading. These fixtures walk
/// everything AlgorithmSet can hold through the reader under a hard timeout — a hang
/// fails the assertion instead of wedging the run.
/// </summary>
public class AlgorithmParsingTests
{
    private static void MustCompleteWithin(TimeSpan limit, Action action, string what)
    {
        var completed = Task.Run(action).Wait(limit);
        Assert.True(completed, $"{what} did not complete within {limit.TotalSeconds}s — reader hang");
    }

    private static XtceLoadResult LoadText(string xml)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        return XtceDocumentReader.LoadWithRecovery(stream);
    }

    private const string MathAlgorithmDocument = """
        <?xml version="1.0" encoding="UTF-8"?>
        <SpaceSystem xmlns="http://www.omg.org/spec/XTCE/20180204" name="Sat">
          <TelemetryMetaData>
            <ParameterTypeSet><FloatParameterType name="F" sizeInBits="32"/></ParameterTypeSet>
            <ParameterSet>
              <Parameter name="IN1" parameterTypeRef="F"/>
              <Parameter name="OUT1" parameterTypeRef="F"/>
            </ParameterSet>
            <AlgorithmSet>
              <MathAlgorithm name="Avg">
                <MathOperation outputParameterRef="OUT1">
                  <ParameterInstanceRefOperand parameterRef="IN1"/>
                  <ThisParameterOperand/>
                  <Operator>+</Operator>
                  <ValueOperand>2</ValueOperand>
                  <Operator>/</Operator>
                  <TriggerSet name="T"><OnParameterUpdateTrigger parameterRef="IN1"/></TriggerSet>
                </MathOperation>
              </MathAlgorithm>
              <CustomAlgorithm name="Custom1">
                <AlgorithmText language="python"><![CDATA[
        def run(x):
            return x < 2 and x > 0  # angle brackets on purpose
        ]]></AlgorithmText>
                <InputSet>
                  <InputParameterInstanceRef parameterRef="IN1" inputName="x"/>
                </InputSet>
                <OutputSet>
                  <OutputParameterRef parameterRef="OUT1" outputName="y"/>
                </OutputSet>
              </CustomAlgorithm>
            </AlgorithmSet>
          </TelemetryMetaData>
          <CommandMetaData>
            <MetaCommandSet>
              <MetaCommand name="Cmd"><CommandContainer name="C"/></MetaCommand>
            </MetaCommandSet>
            <AlgorithmSet>
              <CustomAlgorithm name="CmdSide">
                <AlgorithmText language="JavaScript">var s = "&lt;tag&gt;";</AlgorithmText>
              </CustomAlgorithm>
            </AlgorithmSet>
          </CommandMetaData>
          <SpaceSystem name="Bus"/>
        </SpaceSystem>
        """;

    [Test]
    public void AlgorithmSet_LoadsAndIsPreservedVerbatim()
    {
        XtceLoadResult result = null!;
        MustCompleteWithin(TimeSpan.FromSeconds(10), () => result = LoadText(MathAlgorithmDocument), "MathAlgorithm load");

        Assert.Equal(0, result.Diagnostics.Count);
        var telemetryPreserved = result.Document!.TelemetryMetaData!.Preserved!;
        Assert.True(telemetryPreserved.Any(f => f.ElementName == "AlgorithmSet" && f.OuterXml.Contains("MathAlgorithm")));
        Assert.True(telemetryPreserved.Single(f => f.ElementName == "AlgorithmSet").OuterXml.Contains("def run(x):"));
        Assert.True(result.Document.CommandMetaData!.Preserved!.Any(f => f.OuterXml.Contains("CmdSide")));
        // The child system after the algorithm sets must still parse — nothing consumed past it.
        Assert.Equal("Bus", result.Document.Children.Single().Name);
    }

    [Test]
    public void AlgorithmSet_RoundTripsThroughTheWriter()
    {
        var result = LoadText(MathAlgorithmDocument);
        string written = null!;
        MustCompleteWithin(TimeSpan.FromSeconds(10),
            () => written = XtceDocumentWriter.Write(result.Document!), "algorithm round-trip write");

        Assert.Contains("MathAlgorithm", written);
        Assert.Contains("def run(x):", written);

        var reloaded = LoadText(written);
        Assert.Equal(0, reloaded.Diagnostics.Count);
        Assert.Equal("Bus", reloaded.Document!.Children.Single().Name);
    }

    [Test]
    public void DeeplyNestedAndRepetitiveAlgorithms_TerminateQuickly()
    {
        var algorithms = new StringBuilder();
        for (var i = 0; i < 2000; i++)
        {
            algorithms.Append($"<CustomAlgorithm name=\"A{i}\"><AlgorithmText language=\"python\">x = {i}</AlgorithmText>")
                .Append("<InputSet><InputParameterInstanceRef parameterRef=\"IN1\" inputName=\"x\"/></InputSet></CustomAlgorithm>");
        }
        var nested = new StringBuilder();
        for (var i = 0; i < 60; i++) nested.Append("<Ancillary><Deep>");
        nested.Append("leaf");
        for (var i = 0; i < 60; i++) nested.Append("</Deep></Ancillary>");
        var xml = "<SpaceSystem xmlns=\"http://www.omg.org/spec/XTCE/20180204\" name=\"Sat\"><TelemetryMetaData>"
            + "<ParameterTypeSet><FloatParameterType name=\"F\"/></ParameterTypeSet>"
            + "<ParameterSet><Parameter name=\"IN1\" parameterTypeRef=\"F\"/></ParameterSet>"
            + $"<AlgorithmSet>{algorithms}<CustomAlgorithm name=\"Deep\"><AlgorithmText>{nested}</AlgorithmText></CustomAlgorithm></AlgorithmSet>"
            + "</TelemetryMetaData></SpaceSystem>";

        MustCompleteWithin(TimeSpan.FromSeconds(20), () =>
        {
            var result = LoadText(xml);
            Assert.Equal(0, result.Diagnostics.Count);
        }, "2000-algorithm load");
    }

    [Test]
    public void UnusualNodeTypesAroundAlgorithms_DoNotWedgeTheLoops()
    {
        // Processing instructions, comments, and stray whitespace between every element —
        // the classic hang shape is a loop branch that fails to advance on one of these.
        var xml = """
            <?xml version="1.0"?>
            <?pi data?>
            <SpaceSystem xmlns="http://www.omg.org/spec/XTCE/20180204" name="Sat">
              <?inner pi?>
              <TelemetryMetaData>
                <?deeper pi?>
                <ParameterTypeSet><?x?><IntegerParameterType name="T"/><?y?></ParameterTypeSet>
                <ParameterSet><!-- c --><?z?><Parameter name="P" parameterTypeRef="T"/></ParameterSet>
                <AlgorithmSet><?a?><!-- b --><CustomAlgorithm name="A1"><AlgorithmText>t</AlgorithmText></CustomAlgorithm></AlgorithmSet>
              </TelemetryMetaData>
              <?tail pi?>
            </SpaceSystem>
            """;

        MustCompleteWithin(TimeSpan.FromSeconds(10), () =>
        {
            var result = LoadText(xml);
            Assert.Equal(0, result.Diagnostics.Count);
            Assert.Equal("P", result.Document!.TelemetryMetaData!.ParameterSet.Single().Name);
        }, "processing-instruction load");
    }
}
