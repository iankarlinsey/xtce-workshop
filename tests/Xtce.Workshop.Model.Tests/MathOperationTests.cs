using System.Text;

namespace Xtce.Workshop.Model.Tests;

/// <summary>
/// Modeled MathOperation postfix programs (#125): MathOperationCalibrator beside
/// Polynomial/Spline, and MathAlgorithm's MathOperation body.
/// </summary>
public class MathOperationTests
{
    private const string Ns = "http://www.omg.org/spec/XTCE/20180204";

    private static SpaceSystem Load(string xml) =>
        XtceDocumentReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    private static string WrapCalibrator(string calibratorChildren) => $"""
        <SpaceSystem xmlns="{Ns}" name="S">
          <TelemetryMetaData>
            <ParameterTypeSet>
              <IntegerParameterType name="Temp" signed="false" sizeInBits="16">
                <UnitSet/>
                <IntegerDataEncoding sizeInBits="12">
                  <DefaultCalibrator>
                    {calibratorChildren}
                  </DefaultCalibrator>
                </IntegerDataEncoding>
              </IntegerParameterType>
            </ParameterTypeSet>
            <ParameterSet>
              <Parameter name="Gain" parameterTypeRef="Temp"/>
            </ParameterSet>
          </TelemetryMetaData>
        </SpaceSystem>
        """;

    private static string RoundTrip(SpaceSystem loaded, out SpaceSystem reloaded)
    {
        var xml = XtceDocumentWriter.Write(loaded);
        reloaded = Load(xml);
        return xml;
    }

    [Test]
    public void Load_ModelsMathCalibrator_AllOperandKindsInOrder()
    {
        var loaded = Load(WrapCalibrator("""
            <MathOperationCalibrator>
              <ThisParameterOperand/>
              <ParameterInstanceRefOperand parameterRef="Gain" instance="-1" useCalibratedValue="false"/>
              <Operator>*</Operator>
              <ValueOperand>1.5</ValueOperand>
              <Operator>+</Operator>
            </MathOperationCalibrator>
            """));
        var calibrator = loaded.TelemetryMetaData!.ParameterTypeSet.Single(t => t.Name == "Temp")
            .DataEncoding!.DefaultCalibrator!;

        Assert.Equal(CalibratorKind.MathOperation, calibrator.Kind);
        Assert.Equal(5, calibrator.MathTerms!.Count);
        Assert.Equal(MathOperandKind.ThisParameter, calibrator.MathTerms[0].Kind);
        var operand = calibrator.MathTerms[1];
        Assert.Equal("Gain", operand.InstanceRef!.ParameterRef);
        Assert.Equal(-1, operand.InstanceRef.Instance);
        Assert.Equal(false, operand.InstanceRef.UseCalibratedValue);
        Assert.Equal((MathOperandKind.Operator, "*"), (calibrator.MathTerms[2].Kind, calibrator.MathTerms[2].Text));
        Assert.Equal((MathOperandKind.Value, "1.5"), (calibrator.MathTerms[3].Kind, calibrator.MathTerms[3].Text));

        var xml = RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
        Assert.Empty(XsdValidation.Validate(xml));
    }

    [Test]
    public void Load_CommentInsideMathCalibrator_PreservesTheWholeDefaultCalibrator()
    {
        var loaded = Load(WrapCalibrator("""
            <MathOperationCalibrator>
              <!-- scale first -->
              <ValueOperand>2</ValueOperand>
            </MathOperationCalibrator>
            """));
        var encoding = loaded.TelemetryMetaData!.ParameterTypeSet.Single(t => t.Name == "Temp").DataEncoding!;

        Assert.Null(encoding.DefaultCalibrator);
        Assert.Equal("DefaultCalibrator", Assert.Single(encoding.Preserved!).ElementName);

        RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
    }

    [Test]
    public void Load_ForeignOperand_PreservesTheWholeDefaultCalibrator()
    {
        var loaded = Load(WrapCalibrator("""
            <MathOperationCalibrator>
              <ValueOperand>2</ValueOperand>
              <MysteryOperand/>
            </MathOperationCalibrator>
            """));
        var encoding = loaded.TelemetryMetaData!.ParameterTypeSet.Single(t => t.Name == "Temp").DataEncoding!;

        Assert.Null(encoding.DefaultCalibrator);
        Assert.Equal("DefaultCalibrator", Assert.Single(encoding.Preserved!).ElementName);

        RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
    }

    [Test]
    public void Load_ModelsMathAlgorithmOperation_WithTriggerSetFragment()
    {
        var loaded = Load($"""
            <SpaceSystem xmlns="{Ns}" name="S">
              <TelemetryMetaData>
                <ParameterTypeSet>
                  <FloatParameterType name="F"><UnitSet/></FloatParameterType>
                </ParameterTypeSet>
                <ParameterSet>
                  <Parameter name="RawVolt" parameterTypeRef="F"/>
                  <Parameter name="SmoothVolt" parameterTypeRef="F"/>
                </ParameterSet>
                <AlgorithmSet>
                  <MathAlgorithm name="Double">
                    <MathOperation outputParameterRef="SmoothVolt">
                      <ParameterInstanceRefOperand parameterRef="RawVolt"/>
                      <ValueOperand>2</ValueOperand>
                      <Operator>*</Operator>
                      <TriggerSet><OnParameterUpdateTrigger parameterRef="RawVolt"/></TriggerSet>
                    </MathOperation>
                  </MathAlgorithm>
                </AlgorithmSet>
              </TelemetryMetaData>
            </SpaceSystem>
            """);
        var algorithm = loaded.TelemetryMetaData!.AlgorithmSet!.Single();

        var operation = algorithm.MathOperation!;
        Assert.Equal("SmoothVolt", operation.OutputParameterRef);
        Assert.Equal(3, operation.Terms.Count);
        Assert.Equal("RawVolt", operation.Terms[0].InstanceRef!.ParameterRef);
        Assert.Equal("TriggerSet", operation.TriggerSet!.ElementName);
        Assert.Null(algorithm.Preserved);

        var xml = RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
        Assert.Empty(XsdValidation.Validate(xml));
    }

    [Test]
    public void RoundTrip_AlgorithmsSampleFixture_StaysLosslessAndSchemaValid()
    {
        using var stream = File.OpenRead(Path.Combine(TestPaths.RepoRoot, "samples", "algorithms-1.2.xml"));
        var loaded = XtceDocumentReader.Load(stream);

        var xml = RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
        Assert.Empty(XsdValidation.Validate(xml));
    }

    [Test]
    public void Load_MathOperationWithCommentInside_StaysPreservedOnTheAlgorithm()
    {
        var loaded = Load($"""
            <SpaceSystem xmlns="{Ns}" name="S">
              <TelemetryMetaData>
                <AlgorithmSet>
                  <MathAlgorithm name="Double">
                    <MathOperation outputParameterRef="Out">
                      <!-- multiply by two -->
                      <ValueOperand>2</ValueOperand>
                      <Operator>*</Operator>
                      <TriggerSet><OnParameterUpdateTrigger parameterRef="In"/></TriggerSet>
                    </MathOperation>
                  </MathAlgorithm>
                </AlgorithmSet>
              </TelemetryMetaData>
            </SpaceSystem>
            """);
        var algorithm = loaded.TelemetryMetaData!.AlgorithmSet!.Single();

        Assert.Null(algorithm.MathOperation);
        Assert.Equal("MathOperation", Assert.Single(algorithm.Preserved!).ElementName);

        RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
    }
}
