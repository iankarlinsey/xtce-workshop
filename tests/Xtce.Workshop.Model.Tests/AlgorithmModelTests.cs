using System.Text;

namespace Xtce.Workshop.Model.Tests;

/// <summary>
/// Issue #103: AlgorithmSet is modeled on both metadata sides — CustomAlgorithm's
/// flattened inheritance stack (text, inputs, outputs, trigger attributes) and
/// MathAlgorithm with its MathOperation preserved.
/// </summary>
public class AlgorithmModelTests
{
    private const string Ns = "http://www.omg.org/spec/XTCE/20180204";

    private static SpaceSystem Load(string xml) =>
        XtceDocumentReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    private static string Sample => $"""
        <SpaceSystem xmlns="{Ns}" name="S">
          <TelemetryMetaData>
            <ParameterTypeSet><FloatParameterType name="F"/></ParameterTypeSet>
            <ParameterSet>
              <Parameter name="IN1" parameterTypeRef="F"/>
              <Parameter name="OUT1" parameterTypeRef="F"/>
            </ParameterSet>
            <AlgorithmSet>
              <CustomAlgorithm name="Smooth" thread="true" triggerContainer="Frame" priority="3">
                <AlgorithmText language="python">y = 0.9 * y + 0.1 * x</AlgorithmText>
                <InputSet>
                  <InputParameterInstanceRef parameterRef="IN1" inputName="x" instance="-1"/>
                  <Constant constantName="k" value="0.1"/>
                </InputSet>
                <OutputSet>
                  <OutputParameterRef parameterRef="OUT1" outputName="y"/>
                </OutputSet>
                <TriggerSet><OnPeriodicRateTrigger fireRateInSeconds="1"/></TriggerSet>
              </CustomAlgorithm>
              <MathAlgorithm name="Double">
                <MathOperation outputParameterRef="OUT1">
                  <ParameterInstanceRefOperand parameterRef="IN1"/>
                  <ValueOperand>2</ValueOperand>
                  <Operator>*</Operator>
                  <TriggerSet><OnParameterUpdateTrigger parameterRef="IN1"/></TriggerSet>
                </MathOperation>
              </MathAlgorithm>
            </AlgorithmSet>
          </TelemetryMetaData>
        </SpaceSystem>
        """;

    [Test]
    public void Load_ModelsTheCustomAlgorithmStack()
    {
        var algorithms = Load(Sample).TelemetryMetaData!.AlgorithmSet!;

        var smooth = algorithms.Single(a => a.Name == "Smooth");
        Assert.Equal((AlgorithmKind.Custom, true, "Frame", 3L),
            (smooth.Kind, smooth.Thread!.Value, smooth.TriggerContainer, smooth.Priority!.Value));
        Assert.Equal(("python", "y = 0.9 * y + 0.1 * x"), (smooth.Language, smooth.AlgorithmText));
        var input = Assert.Single(smooth.Inputs!);
        Assert.Equal(("IN1", "x"), (input.ParameterRef, input.Name));
        Assert.Equal("instance", Assert.Single(input.PreservedAttributes!).Name);
        // The Constant rides preserved INSIDE the InputSet.
        Assert.Equal(["Constant"], smooth.PreservedInputs!.Select(f => f.ElementName).ToList());
        Assert.Equal(["TriggerSet"], smooth.Preserved!.Select(f => f.ElementName).ToList());
    }

    [Test]
    public void RoundTrip_IsLosslessAndSchemaValid()
    {
        var loaded = Load(Sample);

        var xml = XtceDocumentWriter.Write(loaded);
        var reloaded = Load(xml);

        Assert.Equal(loaded, reloaded);
        var errors = XsdValidation.Validate(xml);
        Assert.True(errors.Count == 0, "Writer output failed XSD validation:\n" + string.Join("\n", errors));
        // SimpleAlgorithmType order: AlgorithmText, then InputSet, OutputSet, TriggerSet.
        var indexes = new[] { "<AlgorithmText", "<InputSet", "<OutputSet", "<TriggerSet" }
            .Select(tag => xml.IndexOf(tag, StringComparison.Ordinal)).ToList();
        Assert.True(indexes.All(i => i >= 0) && indexes.SequenceEqual(indexes.OrderBy(i => i)),
            "Algorithm children out of schema order: " + string.Join(", ", indexes));
    }
}
