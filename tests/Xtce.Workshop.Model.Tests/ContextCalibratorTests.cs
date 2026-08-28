using System.Text;

namespace Xtce.Workshop.Model.Tests;

/// <summary>
/// Modeled ContextCalibratorList entries (#117): ContextMatch + Calibrator per entry,
/// evaluated in list order; unmodelable entries ride raw in position.
/// </summary>
public class ContextCalibratorTests
{
    private const string Ns = "http://www.omg.org/spec/XTCE/20180204";

    private static SpaceSystem Load(string xml) =>
        XtceDocumentReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    private static string WrapEncodingChildren(string children) => $"""
        <SpaceSystem xmlns="{Ns}" name="S">
          <TelemetryMetaData>
            <ParameterTypeSet>
              <IntegerParameterType name="Temp" signed="false" sizeInBits="16">
                <UnitSet/>
                <IntegerDataEncoding sizeInBits="12">
                  {children}
                </IntegerDataEncoding>
              </IntegerParameterType>
              <IntegerParameterType name="ModeType" signed="false" sizeInBits="8">
                <UnitSet/>
              </IntegerParameterType>
            </ParameterTypeSet>
            <ParameterSet>
              <Parameter name="Mode" parameterTypeRef="ModeType"/>
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

    private const string TwoEntryList = """
        <DefaultCalibrator>
          <PolynomialCalibrator><Term coefficient="0" exponent="0"/></PolynomialCalibrator>
        </DefaultCalibrator>
        <ContextCalibratorList>
          <ContextCalibrator>
            <ContextMatch>
              <Comparison parameterRef="Mode" value="1"/>
            </ContextMatch>
            <Calibrator>
              <PolynomialCalibrator><Term coefficient="2.5" exponent="1"/></PolynomialCalibrator>
            </Calibrator>
          </ContextCalibrator>
          <ContextCalibrator>
            <ContextMatch>
              <ComparisonList>
                <Comparison parameterRef="Mode" value="2"/>
                <Comparison parameterRef="Mode" comparisonOperator="&lt;=" value="5"/>
              </ComparisonList>
            </ContextMatch>
            <Calibrator>
              <SplineCalibrator order="1">
                <SplinePoint raw="0" calibrated="0"/>
                <SplinePoint raw="100" calibrated="1.5"/>
              </SplineCalibrator>
            </Calibrator>
          </ContextCalibrator>
        </ContextCalibratorList>
        """;

    [Test]
    public void Load_ModelsContextCalibrators_InListOrder()
    {
        var type = Load(WrapEncodingChildren(TwoEntryList))
            .TelemetryMetaData!.ParameterTypeSet.Single(t => t.Name == "Temp");
        var encoding = type.DataEncoding!;

        Assert.NotNull(encoding.DefaultCalibrator);
        Assert.Null(encoding.Preserved);
        Assert.Equal(2, encoding.ContextCalibrators!.Count);

        var first = encoding.ContextCalibrators[0];
        Assert.Equal("Mode", first.Context!.Comparison!.ParameterRef);
        Assert.Equal("1", first.Context.Comparison.Value);
        Assert.Equal(CalibratorKind.Polynomial, first.Calibrator!.Kind);
        Assert.Equal("2.5", Assert.Single(first.Calibrator.Terms!).Coefficient);

        var second = encoding.ContextCalibrators[1];
        Assert.Equal(2, second.Context!.ComparisonList!.Count);
        Assert.Equal("<=", second.Context.ComparisonList[1].ComparisonOperator);
        Assert.Equal(CalibratorKind.Spline, second.Calibrator!.Kind);
        Assert.Equal(2, second.Calibrator.Points!.Count);
    }

    [Test]
    public void RoundTrip_ContextCalibrators_IsLosslessAndSchemaValid()
    {
        var loaded = Load(WrapEncodingChildren(TwoEntryList));

        var xml = RoundTrip(loaded, out var reloaded);

        Assert.Equal(loaded, reloaded);
        Assert.Empty(XsdValidation.Validate(xml));
    }

    [Test]
    public void Load_MathOperationEntry_IsModeledInPosition()
    {
        var loaded = Load(WrapEncodingChildren("""
            <ContextCalibratorList>
              <ContextCalibrator>
                <ContextMatch><Comparison parameterRef="Mode" value="1"/></ContextMatch>
                <Calibrator>
                  <PolynomialCalibrator><Term coefficient="1" exponent="1"/></PolynomialCalibrator>
                </Calibrator>
              </ContextCalibrator>
              <ContextCalibrator>
                <ContextMatch><Comparison parameterRef="Mode" value="2"/></ContextMatch>
                <Calibrator>
                  <MathOperationCalibrator><ValueOperand>64</ValueOperand></MathOperationCalibrator>
                </Calibrator>
              </ContextCalibrator>
              <ContextCalibrator>
                <ContextMatch><Comparison parameterRef="Mode" value="3"/></ContextMatch>
                <Calibrator>
                  <PolynomialCalibrator><Term coefficient="3" exponent="1"/></PolynomialCalibrator>
                </Calibrator>
              </ContextCalibrator>
            </ContextCalibratorList>
            """));
        var encoding = loaded.TelemetryMetaData!.ParameterTypeSet.Single(t => t.Name == "Temp").DataEncoding!;

        Assert.Equal(3, encoding.ContextCalibrators!.Count);
        Assert.Null(encoding.ContextCalibrators[0].RawXml);
        // Position 1 is the math entry — modeled since #125, still in evaluation order.
        var mathEntry = encoding.ContextCalibrators[1];
        Assert.Null(mathEntry.RawXml);
        Assert.Equal(CalibratorKind.MathOperation, mathEntry.Calibrator!.Kind);
        Assert.Equal("2", mathEntry.Context!.Comparison!.Value);
        Assert.Equal("3", Assert.Single(encoding.ContextCalibrators[2].Calibrator!.Terms!).Coefficient);

        RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
    }

    [Test]
    public void Load_CommentBetweenEntries_PreservesTheWholeList()
    {
        var loaded = Load(WrapEncodingChildren("""
            <ContextCalibratorList>
              <!-- coarse mode first -->
              <ContextCalibrator>
                <ContextMatch><Comparison parameterRef="Mode" value="1"/></ContextMatch>
                <Calibrator>
                  <PolynomialCalibrator><Term coefficient="1" exponent="1"/></PolynomialCalibrator>
                </Calibrator>
              </ContextCalibrator>
            </ContextCalibratorList>
            """));
        var encoding = loaded.TelemetryMetaData!.ParameterTypeSet.Single(t => t.Name == "Temp").DataEncoding!;

        Assert.Null(encoding.ContextCalibrators);
        Assert.Equal("ContextCalibratorList", Assert.Single(encoding.Preserved!).ElementName);

        RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
    }

    [Test]
    public void Load_BooleanExpressionContext_StaysModeledWithPreservedCriteria()
    {
        var loaded = Load(WrapEncodingChildren("""
            <ContextCalibratorList>
              <ContextCalibrator>
                <ContextMatch>
                  <BooleanExpression>
                    <Condition><ParameterInstanceRef parameterRef="Mode"/><ComparisonOperator>==</ComparisonOperator><Value>1</Value></Condition>
                  </BooleanExpression>
                </ContextMatch>
                <Calibrator>
                  <PolynomialCalibrator><Term coefficient="1" exponent="1"/></PolynomialCalibrator>
                </Calibrator>
              </ContextCalibrator>
            </ContextCalibratorList>
            """));
        var entry = Assert.Single(loaded.TelemetryMetaData!.ParameterTypeSet
            .Single(t => t.Name == "Temp").DataEncoding!.ContextCalibrators!);

        Assert.Null(entry.RawXml);
        Assert.Null(entry.Context!.Comparison);
        // Modeled as a tree since #124 — no fragment left behind.
        Assert.Null(entry.Context.Preserved);
        Assert.Equal("Mode", entry.Context.BooleanExpression!.Left!.ParameterRef);

        RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
    }

    [Test]
    public void Load_EntryMissingItsCalibratorHalf_RidesRaw()
    {
        var loaded = Load(WrapEncodingChildren("""
            <ContextCalibratorList>
              <ContextCalibrator>
                <ContextMatch><Comparison parameterRef="Mode" value="1"/></ContextMatch>
              </ContextCalibrator>
            </ContextCalibratorList>
            """));
        var entry = Assert.Single(loaded.TelemetryMetaData!.ParameterTypeSet
            .Single(t => t.Name == "Temp").DataEncoding!.ContextCalibrators!);

        Assert.Equal("ContextCalibrator", entry.RawXml!.ElementName);

        RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
    }

    [Test]
    public void Load_ContextCalibratorsOnArgumentTypes_Too()
    {
        var loaded = Load($"""
            <SpaceSystem xmlns="{Ns}" name="S">
              <CommandMetaData>
                <ArgumentTypeSet>
                  <FloatArgumentType name="Gain">
                    <UnitSet/>
                    <FloatDataEncoding>
                      <ContextCalibratorList>
                        <ContextCalibrator>
                          <ContextMatch><Comparison parameterRef="Mode" value="1"/></ContextMatch>
                          <Calibrator>
                            <SplineCalibrator><SplinePoint raw="0" calibrated="0"/><SplinePoint raw="1" calibrated="10"/></SplineCalibrator>
                          </Calibrator>
                        </ContextCalibrator>
                      </ContextCalibratorList>
                    </FloatDataEncoding>
                  </FloatArgumentType>
                </ArgumentTypeSet>
              </CommandMetaData>
            </SpaceSystem>
            """);
        var entry = Assert.Single(loaded.CommandMetaData!.ArgumentTypeSet!
            .Single(t => t.Name == "Gain").DataEncoding!.ContextCalibrators!);

        Assert.Equal(CalibratorKind.Spline, entry.Calibrator!.Kind);

        var xml = RoundTrip(loaded, out var reloaded);
        Assert.Equal(loaded, reloaded);
        Assert.Empty(XsdValidation.Validate(xml));
    }
}
