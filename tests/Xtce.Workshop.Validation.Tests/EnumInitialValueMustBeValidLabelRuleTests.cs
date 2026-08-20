using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation.Tests;

public class EnumInitialValueMustBeValidLabelRuleTests
{
    private static SpaceSystem WithEnumType(string? initialValue, params EnumerationEntry[] entries)
    {
        var type = new ParameterTypeDefinition(
            "State_Type", ParameterTypeKind.Enumerated, InitialValue: initialValue, Enumerations: entries);
        var telemetryMetaData = new TelemetryMetaData([type], []);
        return new SpaceSystem("Root", [], telemetryMetaData);
    }

    [Test]
    public void Validate_InitialValueMatchesALabel_ReportsNoIssue()
    {
        var spaceSystem = WithEnumType("SAFE", new EnumerationEntry(0, "SAFE"), new EnumerationEntry(1, "FAULT"));

        var issues = XtceValidator.Validate(spaceSystem);

        Assert.DoesNotContain(issues, i => i.RuleId == "XTCE-1.2-R07-enum-initial-value-must-be-valid-label");
    }

    [Test]
    public void Validate_InitialValueDoesNotMatchAnyLabel_ReportsIssue()
    {
        var spaceSystem = WithEnumType("UNKNOWN", new EnumerationEntry(0, "SAFE"), new EnumerationEntry(1, "FAULT"));

        var issues = XtceValidator.Validate(spaceSystem);

        var issue = Assert.Single(issues, i => i.RuleId == "XTCE-1.2-R07-enum-initial-value-must-be-valid-label");
        Assert.Equal(ValidationSeverity.Error, issue.Severity);
        Assert.Equal("Root/ParameterTypeSet/State_Type", issue.Location);
        Assert.Contains("UNKNOWN", issue.Message);
    }

    [Test]
    public void Validate_NoInitialValueSet_ReportsNoIssue()
    {
        var spaceSystem = WithEnumType(null, new EnumerationEntry(0, "SAFE"));

        var issues = XtceValidator.Validate(spaceSystem);

        Assert.DoesNotContain(issues, i => i.RuleId == "XTCE-1.2-R07-enum-initial-value-must-be-valid-label");
    }

    [Test]
    public void Validate_NonEnumeratedTypeWithInitialValue_ReportsNoIssue()
    {
        var type = new ParameterTypeDefinition("Int_Type", ParameterTypeKind.Integer, InitialValue: "anything");
        var telemetryMetaData = new TelemetryMetaData([type], []);
        var spaceSystem = new SpaceSystem("Root", [], telemetryMetaData);

        var issues = XtceValidator.Validate(spaceSystem);

        Assert.DoesNotContain(issues, i => i.RuleId == "XTCE-1.2-R07-enum-initial-value-must-be-valid-label");
    }

    [Test]
    public void Validate_FindsIssuesInNestedSpaceSystems()
    {
        var badChild = WithEnumType("UNKNOWN", new EnumerationEntry(0, "SAFE")) with { Name = "Child" };
        var root = new SpaceSystem("Root", [badChild]);

        var issues = XtceValidator.Validate(root);

        var issue = Assert.Single(issues, i => i.RuleId == "XTCE-1.2-R07-enum-initial-value-must-be-valid-label");
        Assert.Equal("Root/Child/ParameterTypeSet/State_Type", issue.Location);
    }

    [Test]
    public void Validate_DocumentWithNoTelemetryMetaData_ReportsNoIssues()
    {
        var spaceSystem = new SpaceSystem("Root", []);

        var issues = XtceValidator.Validate(spaceSystem);

        Assert.Empty(issues);
    }
}
