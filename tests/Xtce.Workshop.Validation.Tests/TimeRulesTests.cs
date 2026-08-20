using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation.Tests;

/// <summary>R14 (time-datatype-requires-encoding) and R01 (ambiguous-time-units, warning).</summary>
public class TimeRulesTests
{
    private const string R14 = "XTCE-1.2-R14-time-datatype-requires-encoding";
    private const string R01 = "XTCE-1.2-R01-ambiguous-time-units-flagged";

    private static RawXmlFragment Encoding(string units) => new(
        "Encoding",
        $"""<Encoding units="{units}" xmlns="http://www.omg.org/spec/XTCE/20180204"><IntegerDataEncoding/></Encoding>""");

    private static SpaceSystem WithType(ParameterTypeDefinition type) =>
        new("S", [], new TelemetryMetaData([type], []));

    [Test]
    public void TimeTypeWithEncoding_IsClean()
    {
        var type = new ParameterTypeDefinition("T", ParameterTypeKind.RelativeTime, Preserved: [Encoding("seconds")]);

        var issues = XtceValidator.Validate(WithType(type));

        Assert.DoesNotContain(issues, i => i.RuleId == R14);
        Assert.DoesNotContain(issues, i => i.RuleId == R01);
    }

    [Test]
    public void TimeTypeWithoutEncodingOrBaseType_IsFlaggedByR14()
    {
        var type = new ParameterTypeDefinition("T", ParameterTypeKind.AbsoluteTime);

        var issues = XtceValidator.Validate(WithType(type));

        var issue = Assert.Single(issues, i => i.RuleId == R14);
        Assert.Equal(ValidationSeverity.Error, issue.Severity);
        Assert.Equal("S/ParameterTypeSet/T", issue.Location);
    }

    [Test]
    public void TimeTypeWithBaseType_IsNotFlagged_EncodingMayBeInherited()
    {
        var type = new ParameterTypeDefinition("T", ParameterTypeKind.AbsoluteTime,
            PreservedAttributes: [new RawAttribute("baseType", "SomeBase")]);

        var issues = XtceValidator.Validate(WithType(type));

        Assert.DoesNotContain(issues, i => i.RuleId == R14);
    }

    [Test]
    public void NonTimeTypeWithoutEncoding_IsNotR14Business()
    {
        var type = new ParameterTypeDefinition("T", ParameterTypeKind.Integer);

        var issues = XtceValidator.Validate(WithType(type));

        Assert.DoesNotContain(issues, i => i.RuleId == R14);
    }

    [TestCase("days")]
    [TestCase("months")]
    [TestCase("years")]
    public void CalendarVariableUnits_AreFlaggedByR01AsWarning(string units)
    {
        var type = new ParameterTypeDefinition("T", ParameterTypeKind.AbsoluteTime, Preserved: [Encoding(units)]);

        var issues = XtceValidator.Validate(WithType(type));

        var issue = Assert.Single(issues, i => i.RuleId == R01);
        Assert.Equal(ValidationSeverity.Warning, issue.Severity);
        Assert.Contains(units, issue.Message);
    }

    [TestCase("seconds")]
    [TestCase("picoSeconds")]
    public void FixedLengthUnits_AreNotFlagged(string units)
    {
        var type = new ParameterTypeDefinition("T", ParameterTypeKind.RelativeTime, Preserved: [Encoding(units)]);

        var issues = XtceValidator.Validate(WithType(type));

        Assert.DoesNotContain(issues, i => i.RuleId == R01);
    }

    [TestCase("Binary", "CAFEBABE", true)]
    [TestCase("Binary", "0DDBA11", false)]  // odd digit count
    [TestCase("Binary", "NOTHEX", false)]
    [TestCase("RelativeTime", "P1DT2H", true)]
    [TestCase("RelativeTime", "tomorrow", false)]
    [TestCase("AbsoluteTime", "2026-08-19T12:00:00Z", true)]
    [TestCase("AbsoluteTime", "yesterday", false)]
    public void R15_ChecksInitialValuesForNewKinds(string kindName, string value, bool expectedValid)
    {
        var kind = Enum.Parse<ParameterTypeKind>(kindName);
        var telemetry = new TelemetryMetaData(
            [new ParameterTypeDefinition("T", kind)],
            [new Parameter("P", "T", value)]);
        var spaceSystem = new SpaceSystem("S", [], telemetry);

        var issues = XtceValidator.Validate(spaceSystem);

        Assert.Equal(expectedValid,
            !issues.Any(i => i.RuleId == "XTCE-1.2-R15-typed-value-valid-for-type"));
    }
}
