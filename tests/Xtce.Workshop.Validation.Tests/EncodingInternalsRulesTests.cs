using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation.Tests;

/// <summary>R13 (spline order vs point count) and R03 (custom checksum needs algorithm).</summary>
public class EncodingInternalsRulesTests
{
    private const string R13 = "XTCE-1.2-R13-spline-order-requires-min-points";
    private const string R03 = "XTCE-1.2-R03-checksum-custom-requires-inputalgorithm";
    private const string Ns = "http://www.omg.org/spec/XTCE/20180204";

    private static SpaceSystem WithTypeFragment(string encodingXml) =>
        new("S", [], new TelemetryMetaData(
            [
                new ParameterTypeDefinition("T", ParameterTypeKind.Integer,
                    Preserved: [new RawXmlFragment("IntegerDataEncoding", encodingXml)]),
            ],
            []));

    private static string SplineEncoding(long? order, int points)
    {
        var orderAttribute = order is null ? "" : $" order=\"{order}\"";
        var pointXml = string.Join("", Enumerable.Range(0, points).Select(i =>
            $"<SplinePoint raw=\"{i}\" calibrated=\"{i}\"/>"));
        return $"""<IntegerDataEncoding xmlns="{Ns}"><DefaultCalibrator><SplineCalibrator{orderAttribute}>{pointXml}</SplineCalibrator></DefaultCalibrator></IntegerDataEncoding>""";
    }

    [TestCase(1, 2)] // linear, 2 points — fine
    [TestCase(2, 3)] // quadratic, 3 points — fine
    [TestCase(0, 2)] // flat, needs 1+ — fine
    public void SplineWithEnoughPoints_IsClean(long order, int points)
    {
        var issues = XtceValidator.Validate(WithTypeFragment(SplineEncoding(order, points)));

        Assert.DoesNotContain(issues, i => i.RuleId == R13);
    }

    [TestCase(2, 2)]
    [TestCase(3, 2)]
    public void SplineWithTooFewPointsForItsOrder_IsFlagged(long order, int points)
    {
        var issues = XtceValidator.Validate(WithTypeFragment(SplineEncoding(order, points)));

        var issue = Assert.Single(issues, i => i.RuleId == R13);
        Assert.Contains($"order {order}", issue.Message);
        Assert.Equal("S/ParameterTypeSet/T", issue.Location);
    }

    [Test]
    public void OmittedOrderDefaultsToOne_TwoPointsClean()
    {
        var issues = XtceValidator.Validate(WithTypeFragment(SplineEncoding(null, 2)));

        Assert.DoesNotContain(issues, i => i.RuleId == R13);
    }

    [Test]
    public void SplineInsidePreservedCommandMetaData_IsAlsoChecked()
    {
        var commandMetaData = $"""
            <CommandMetaData xmlns="{Ns}">
              <ArgumentTypeSet>
                <IntegerArgumentType name="A">
                  <IntegerDataEncoding>
                    <DefaultCalibrator>
                      <SplineCalibrator order="3"><SplinePoint raw="0" calibrated="0"/><SplinePoint raw="1" calibrated="1"/></SplineCalibrator>
                    </DefaultCalibrator>
                  </IntegerDataEncoding>
                </IntegerArgumentType>
              </ArgumentTypeSet>
            </CommandMetaData>
            """;
        var spaceSystem = new SpaceSystem("S", [],
            Preserved: [new RawXmlFragment("CommandMetaData", commandMetaData)]);

        var issues = XtceValidator.Validate(spaceSystem);

        var issue = Assert.Single(issues, i => i.RuleId == R13);
        Assert.Equal("S/CommandMetaData", issue.Location);
    }

    [Test]
    public void CustomChecksumWithoutInputAlgorithm_IsFlagged()
    {
        var encoding = $"""<BinaryDataEncoding xmlns="{Ns}"><ErrorDetectCorrect><Checksum name="custom" bitsFromReference="0"/></ErrorDetectCorrect></BinaryDataEncoding>""";
        var issues = XtceValidator.Validate(WithTypeFragment(encoding));

        var issue = Assert.Single(issues, i => i.RuleId == R03);
        Assert.Contains("InputAlgorithm", issue.Message);
    }

    [Test]
    public void CustomChecksumWithInputAlgorithm_IsClean()
    {
        var encoding = $"""<BinaryDataEncoding xmlns="{Ns}"><ErrorDetectCorrect><Checksum name="custom"><InputAlgorithm name="myAlgo"><AlgorithmText>x</AlgorithmText></InputAlgorithm></Checksum></ErrorDetectCorrect></BinaryDataEncoding>""";
        var issues = XtceValidator.Validate(WithTypeFragment(encoding));

        Assert.DoesNotContain(issues, i => i.RuleId == R03);
    }

    [Test]
    public void StandardChecksum_IsNotR03Business()
    {
        var encoding = $"""<BinaryDataEncoding xmlns="{Ns}"><ErrorDetectCorrect><Checksum name="sum16"/></ErrorDetectCorrect></BinaryDataEncoding>""";
        var issues = XtceValidator.Validate(WithTypeFragment(encoding));

        Assert.DoesNotContain(issues, i => i.RuleId == R03);
    }
}
