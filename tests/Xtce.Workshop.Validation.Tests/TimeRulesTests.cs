using Xtce.Workshop.Model;
using Xunit;

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

    [Fact]
    public void TimeTypeWithEncoding_IsClean()
    {
        var type = new ParameterTypeDefinition("T", ParameterTypeKind.RelativeTime, Preserved: [Encoding("seconds")]);

        var issues = XtceValidator.Validate(WithType(type));

        Assert.DoesNotContain(issues, i => i.RuleId == R14);
        Assert.DoesNotContain(issues, i => i.RuleId == R01);
    }

    [Fact]
    public void TimeTypeWithoutEncodingOrBaseType_IsFlaggedByR14()
    {
        var type = new ParameterTypeDefinition("T", ParameterTypeKind.AbsoluteTime);

        var issues = XtceValidator.Validate(WithType(type));

        var issue = Assert.Single(issues, i => i.RuleId == R14);
        Assert.Equal(ValidationSeverity.Error, issue.Severity);
        Assert.Equal("S/ParameterTypeSet/T", issue.Location);
    }

    [Fact]
    public void TimeTypeWithBaseType_IsNotFlagged_EncodingMayBeInherited()
    {
        var type = new ParameterTypeDefinition("T", ParameterTypeKind.AbsoluteTime,
            PreservedAttributes: [new RawAttribute("baseType", "SomeBase")]);

        var issues = XtceValidator.Validate(WithType(type));

        Assert.DoesNotContain(issues, i => i.RuleId == R14);
    }

    [Fact]
    public void NonTimeTypeWithoutEncoding_IsNotR14Business()
    {
        var type = new ParameterTypeDefinition("T", ParameterTypeKind.Integer);

        var issues = XtceValidator.Validate(WithType(type));

        Assert.DoesNotContain(issues, i => i.RuleId == R14);
    }

    [Theory]
    [InlineData("days")]
    [InlineData("months")]
    [InlineData("years")]
    public void CalendarVariableUnits_AreFlaggedByR01AsWarning(string units)
    {
        var type = new ParameterTypeDefinition("T", ParameterTypeKind.AbsoluteTime, Preserved: [Encoding(units)]);

        var issues = XtceValidator.Validate(WithType(type));

        var issue = Assert.Single(issues, i => i.RuleId == R01);
        Assert.Equal(ValidationSeverity.Warning, issue.Severity);
        Assert.Contains(units, issue.Message);
    }

    [Theory]
    [InlineData("seconds")]
    [InlineData("picoSeconds")]
    public void FixedLengthUnits_AreNotFlagged(string units)
    {
        var type = new ParameterTypeDefinition("T", ParameterTypeKind.RelativeTime, Preserved: [Encoding(units)]);

        var issues = XtceValidator.Validate(WithType(type));

        Assert.DoesNotContain(issues, i => i.RuleId == R01);
    }

    [Theory]
    [InlineData("Binary", "CAFEBABE", true)]
    [InlineData("Binary", "0DDBA11", false)]  // odd digit count
    [InlineData("Binary", "NOTHEX", false)]
    [InlineData("RelativeTime", "P1DT2H", true)]
    [InlineData("RelativeTime", "tomorrow", false)]
    [InlineData("AbsoluteTime", "2026-08-19T12:00:00Z", true)]
    [InlineData("AbsoluteTime", "yesterday", false)]
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
