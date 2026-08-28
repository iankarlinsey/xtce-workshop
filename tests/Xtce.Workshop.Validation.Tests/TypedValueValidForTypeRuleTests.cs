using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation.Tests;

public class TypedValueValidForTypeRuleTests
{
    private const string RuleId = "XTCE-1.2-R15-typed-value-valid-for-type";

    private static SpaceSystem WithParameter(ParameterTypeDefinition type, string? parameterInitialValue)
    {
        var telemetryMetaData = new TelemetryMetaData(
            [type],
            [new Parameter("P", type.Name, parameterInitialValue)]);
        return new SpaceSystem("Root", [], telemetryMetaData);
    }

    [TestCase("42", true)]
    [TestCase("not-a-number", false)]
    public void Validate_IntegerParameter_ChecksParseability(string value, bool expectedValid)
    {
        var type = new ParameterTypeDefinition("Int_Type", ParameterTypeKind.Integer, Signed: true, SizeInBits: 32);

        var issues = XtceValidator.Validate(WithParameter(type, value));

        Assert.Equal(expectedValid, !issues.Any(i => i.RuleId == RuleId));
    }

    [TestCase(true, 8, "127", true)]   // signed 8-bit max
    [TestCase(true, 8, "128", false)]  // one past signed 8-bit max
    [TestCase(true, 8, "-128", true)]  // signed 8-bit min
    [TestCase(true, 8, "-129", false)] // one past signed 8-bit min
    [TestCase(false, 8, "255", true)]  // unsigned 8-bit max
    [TestCase(false, 8, "256", false)] // one past unsigned 8-bit max
    [TestCase(false, 8, "-1", false)]  // unsigned can't be negative
    public void Validate_IntegerParameter_ChecksRange(bool signed, long sizeInBits, string value, bool expectedValid)
    {
        var type = new ParameterTypeDefinition("Int_Type", ParameterTypeKind.Integer, Signed: signed, SizeInBits: sizeInBits);

        var issues = XtceValidator.Validate(WithParameter(type, value));

        Assert.Equal(expectedValid, !issues.Any(i => i.RuleId == RuleId));
    }

    [TestCase("3.14", true)]
    [TestCase("1.5e2", true)]
    [TestCase("not-a-float", false)]
    public void Validate_FloatParameter_ChecksParseability(string value, bool expectedValid)
    {
        var type = new ParameterTypeDefinition("Float_Type", ParameterTypeKind.Float, SizeInBits: 32);

        var issues = XtceValidator.Validate(WithParameter(type, value));

        Assert.Equal(expectedValid, !issues.Any(i => i.RuleId == RuleId));
    }

    [Test]
    public void Validate_StringParameter_AlwaysValid()
    {
        var type = new ParameterTypeDefinition("String_Type", ParameterTypeKind.String);

        var issues = XtceValidator.Validate(WithParameter(type, "anything at all"));

        Assert.DoesNotContain(issues, i => i.RuleId == RuleId);
    }

    [TestCase("On", true)]
    [TestCase("Off", true)]
    [TestCase("Maybe", false)]
    public void Validate_BooleanParameter_ChecksAgainstOneAndZeroStringValues(string value, bool expectedValid)
    {
        var type = new ParameterTypeDefinition("Bool_Type", ParameterTypeKind.Boolean, OneStringValue: "On", ZeroStringValue: "Off");

        var issues = XtceValidator.Validate(WithParameter(type, value));

        Assert.Equal(expectedValid, !issues.Any(i => i.RuleId == RuleId));
    }

    [TestCase("SAFE", true)]
    [TestCase("UNKNOWN", false)]
    public void Validate_EnumeratedParameter_ChecksAgainstEnumerationList(string value, bool expectedValid)
    {
        var type = new ParameterTypeDefinition(
            "Enum_Type", ParameterTypeKind.Enumerated, Enumerations: [new EnumerationEntry(0, "SAFE")]);

        var issues = XtceValidator.Validate(WithParameter(type, value));

        Assert.Equal(expectedValid, !issues.Any(i => i.RuleId == RuleId));
    }

    [Test]
    public void Validate_ParameterWithNoInitialValue_ReportsNoIssue()
    {
        var type = new ParameterTypeDefinition("Int_Type", ParameterTypeKind.Integer);

        var issues = XtceValidator.Validate(WithParameter(type, null));

        Assert.DoesNotContain(issues, i => i.RuleId == RuleId);
    }

    [Test]
    public void Validate_ParameterTypeRefDoesNotResolveLocally_ReportsNoIssue()
    {
        // Simulates a reference to a type defined elsewhere in the SpaceSystem tree — this
        // rule intentionally doesn't attempt cross-subsystem name resolution (that's R11's
        // job), so an unresolvable ref must be silently skipped, not flagged.
        var telemetryMetaData = new TelemetryMetaData(
            [],
            [new Parameter("P", "SomeOtherSubsystem/Some_Type", "not-even-a-number")]);
        var spaceSystem = new SpaceSystem("Root", [], telemetryMetaData);

        var issues = XtceValidator.Validate(spaceSystem);

        Assert.DoesNotContain(issues, i => i.RuleId == RuleId);
    }

    [Test]
    public void Validate_IntegerInitialValue_ReportsLocationAndRuleId()
    {
        var type = new ParameterTypeDefinition("Int_Type", ParameterTypeKind.Integer, SizeInBits: 8, Signed: true);

        var issues = XtceValidator.Validate(WithParameter(type, "1000"));

        var issue = Assert.Single(issues, i => i.RuleId == RuleId);
        Assert.Equal(ValidationSeverity.Error, issue.Severity);
        Assert.Equal("Root/ParameterSet/P", issue.Location);
        Assert.Contains("1000", issue.Message);
    }

    [Test]
    public void Validate_SizeInBitsOmitted_DefaultsToThirtyTwoPerXsd()
    {
        var type = new ParameterTypeDefinition("Int_Type", ParameterTypeKind.Integer, Signed: true);

        var withinDefault32 = XtceValidator.Validate(WithParameter(type, "2000000000"));
        var beyondDefault32 = XtceValidator.Validate(WithParameter(type, "3000000000"));

        Assert.DoesNotContain(withinDefault32, i => i.RuleId == RuleId);
        Assert.Contains(beyondDefault32, i => i.RuleId == RuleId);
    }

    [Test]
    public void Validate_ModeledExpressionConditionLiteral_IsChecked()
    {
        // #124: BooleanExpression condition values in context significances used to be
        // caught by the command-fragment scan; the modeled tree must keep the check.
        var telemetry = new TelemetryMetaData(
            [new ParameterTypeDefinition("Int_Type", ParameterTypeKind.Integer, Signed: false, SizeInBits: 8)],
            [new Parameter("Mode", "Int_Type")]);
        var command = new CommandMetaData(
        [
            new MetaCommand("Thrust", ContextSignificances:
            [
                new ContextSignificance(
                    new MatchCriteria(BooleanExpression: new BooleanExpressionNode(
                        BooleanNodeKind.Condition,
                        new ParameterInstanceRef("Mode"), "==", "not-a-number")),
                    new Significance(ConsequenceLevel: "critical")),
            ]),
        ]);
        var document = new SpaceSystem("Root", [], telemetry, CommandMetaData: command);

        var issues = XtceValidator.Validate(document);

        var issue = Assert.Single(issues, i => i.RuleId == RuleId);
        Assert.Contains("Condition against 'Mode'", issue.Message);
    }
}
