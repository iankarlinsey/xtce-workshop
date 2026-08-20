using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation.Tests;

/// <summary>R16/R17/R18/R19/R20 — the rules promoted by the CCSDS 660.1-G-2 mining (#38/#39).</summary>
public class GreenBookRulesTests
{
    private const string R16 = "XTCE-1.2-R16-no-inheritance-cycles";
    private const string R18 = "XTCE-1.2-R18-type-inheritance-override-restrictions";
    private const string R20 = "XTCE-1.2-R20-telemetered-parameter-requires-encoding";
    private const string Ns = "http://www.omg.org/spec/XTCE/20180204";

    private static ParameterTypeDefinition TypeWithBase(string name, string baseType, ParameterTypeKind kind = ParameterTypeKind.Integer,
        long? sizeInBits = null, bool? signed = null) =>
        new(name, kind, SizeInBits: sizeInBits, Signed: signed,
            PreservedAttributes: [new RawAttribute("baseType", baseType)]);

    [Test]
    public void ParameterTypeBaseTypeCycle_IsFlaggedOnEveryMember()
    {
        var telemetry = new TelemetryMetaData(
            [TypeWithBase("A", "B"), TypeWithBase("B", "A")], []);

        var issues = XtceValidator.Validate(new SpaceSystem("S", [], telemetry))
            .Where(i => i.RuleId == R16).ToList();

        Assert.Equal(2, issues.Count);
    }

    [Test]
    public void SelfReferencingBaseType_IsFlagged()
    {
        var telemetry = new TelemetryMetaData([TypeWithBase("A", "A")], []);

        var issues = XtceValidator.Validate(new SpaceSystem("S", [], telemetry));

        Assert.Single(issues, i => i.RuleId == R16);
    }

    [Test]
    public void LinearBaseTypeChain_IsClean()
    {
        var telemetry = new TelemetryMetaData(
            [
                new ParameterTypeDefinition("Root", ParameterTypeKind.Integer),
                TypeWithBase("Mid", "Root"),
                TypeWithBase("Leaf", "Mid"),
            ], []);

        var issues = XtceValidator.Validate(new SpaceSystem("S", [], telemetry));

        Assert.DoesNotContain(issues, i => i.RuleId == R16);
    }

    [Test]
    public void MetaCommandCycle_IsFlagged()
    {
        var commandMetaData = new CommandMetaData(
        [
            new MetaCommand("A", BaseMetaCommandRef: "B"),
            new MetaCommand("B", BaseMetaCommandRef: "A"),
        ]);

        var issues = XtceValidator.Validate(new SpaceSystem("S", [], CommandMetaData: commandMetaData))
            .Where(i => i.RuleId == R16).ToList();

        Assert.Equal(2, issues.Count);
    }

    [Test]
    public void UnlikeKindDerivation_IsFlaggedByR18()
    {
        var telemetry = new TelemetryMetaData(
            [
                new ParameterTypeDefinition("Base_S", ParameterTypeKind.String),
                TypeWithBase("Derived_I", "Base_S", ParameterTypeKind.Integer),
            ], []);

        var issues = XtceValidator.Validate(new SpaceSystem("S", [], telemetry));

        var issue = Assert.Single(issues, i => i.RuleId == R18);
        Assert.Contains("like type", issue.Message);
    }

    [Test]
    public void SizeOverrideMatchingParentEffectiveDefault_IsFlagged()
    {
        // Parent leaves sizeInBits unset (effective 32); child setting 16 differs and
        // "cannot override the parent, including default values".
        var telemetry = new TelemetryMetaData(
            [
                new ParameterTypeDefinition("Base_I", ParameterTypeKind.Integer),
                TypeWithBase("Derived_I", "Base_I", sizeInBits: 16),
            ], []);

        var issues = XtceValidator.Validate(new SpaceSystem("S", [], telemetry));

        Assert.Single(issues, i => i.RuleId == R18 && i.Message.Contains("sizeInBits"));
    }

    [Test]
    public void ChildRestatingParentEffectiveValue_IsClean()
    {
        var telemetry = new TelemetryMetaData(
            [
                new ParameterTypeDefinition("Base_I", ParameterTypeKind.Integer, SizeInBits: 16),
                TypeWithBase("Derived_I", "Base_I", sizeInBits: 16),
            ], []);

        var issues = XtceValidator.Validate(new SpaceSystem("S", [], telemetry));

        Assert.DoesNotContain(issues, i => i.RuleId == R18);
    }

    [Test]
    public void ImpliedTelemeteredDataSource_IsDeliberatelyNotFlaggedByR20()
    {
        // The green book's "implied" case is the recorded partial gap — a parameter with
        // no ParameterProperties must NOT warn, or every minimal document lights up.
        var telemetry = new TelemetryMetaData(
            [new ParameterTypeDefinition("T", ParameterTypeKind.Integer)],
            [new Parameter("P", "T")]);

        var issues = XtceValidator.Validate(new SpaceSystem("S", [], telemetry));

        Assert.DoesNotContain(issues, i => i.RuleId == R20);
    }

    [Test]
    public void ExplicitTelemeteredWithoutEncoding_WarnsViaR20()
    {
        var telemetry = new TelemetryMetaData(
            [new ParameterTypeDefinition("T", ParameterTypeKind.Integer)],
            [new Parameter("P", "T", Preserved:
                [new RawXmlFragment("ParameterProperties", $"""<ParameterProperties dataSource="telemetered" xmlns="{Ns}"/>""")])]);

        var issues = XtceValidator.Validate(new SpaceSystem("S", [], telemetry));

        var issue = Assert.Single(issues, i => i.RuleId == R20);
        Assert.Equal(ValidationSeverity.Warning, issue.Severity);
    }
}
